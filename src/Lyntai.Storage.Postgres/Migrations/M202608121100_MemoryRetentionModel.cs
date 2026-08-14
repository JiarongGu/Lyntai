using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>The whole 3.0 memory-retention schema as ONE migration — Postgres leg, parallel to the SQLite
/// migration of the same number. See that migration's doc comment for the full reasoning behind the squash
/// (<c>docs/DECISIONS.md</c> <b>D9</b>: all six migrations it replaces landed after <c>v2.5.0</c> was cut, so
/// no consumer database ever applied one), for what is deliberately NOT folded in
/// (<c>M202608081215_MemoryGraph</c> shipped in 2.5.0, so these stay <c>ALTER</c>s), and for the direction of
/// each backfill. This one repeats only what differs.
///
/// <para><b>Dialect differences</b>: <c>BIGINT</c>/<c>DOUBLE PRECISION</c> rather than SQLite's dynamically
/// typed <c>INTEGER</c>/<c>REAL</c>, <c>TIMESTAMPTZ</c> rather than <c>TEXT</c> for the two timestamps,
/// <c>JSONB</c> rather than <c>TEXT</c> for the signals bag, and <c>BIGSERIAL</c> rather than
/// <c>INTEGER PRIMARY KEY AUTOINCREMENT</c> for the review log's id.</para>
///
/// <para><b>One deliberate divergence that is NOT dialect</b>: there is no <c>salience</c> index here. Both of
/// this backend's seed paths lead with a computed boolean that no such prefix can satisfy, so the index SQLite
/// gains would be unread — pure write amplification on the hottest table in the schema. SQLite earns its index
/// because its FTS-merge path runs a separate exact-facts sub-query whose <c>ORDER BY</c> genuinely leads with
/// <c>salience DESC</c>.</para></summary>
[Migration(202608121100)]
[Tags(nameof(StorageFeature.Memory), StorageFeatures.AllTag)]
public sealed class M202608121100_MemoryRetentionModel : Migration
{
    public override void Up()
    {
        // ---- signals bag + promoted salience --------------------------------------------------------
        // NOT NULL DEFAULT 1: 1 is the neutral value, so every pre-existing row migrates to "no opinion". A
        // nullable column would be actively wrong here — ORDER BY salience DESC puts NULLs FIRST on this
        // backend, so every legacy row would silently outrank every judged one.
        Execute.Sql("ALTER TABLE lyntai_memory_node ADD COLUMN signals JSONB NULL");
        Execute.Sql("ALTER TABLE lyntai_memory_node ADD COLUMN salience DOUBLE PRECISION NOT NULL DEFAULT 1");

        // ---- the three policy-independent age primitives, node side ---------------------------------
        Execute.Sql("ALTER TABLE lyntai_memory_node ADD COLUMN encoding_ordinal BIGINT NOT NULL DEFAULT 0");
        Execute.Sql("ALTER TABLE lyntai_memory_node ADD COLUMN encoding_chars BIGINT NOT NULL DEFAULT 0");
        Execute.Sql("""
            ALTER TABLE lyntai_memory_node
            ADD COLUMN encoding_at TIMESTAMPTZ NOT NULL DEFAULT '1970-01-01T00:00:00Z'
            """);

        // Exact backfill, ordered by (created_at, id) within each engine — id is a unique tiebreaker so two
        // writes landing in the same instant still produce a stable, monotone ordinal. The three disclosures
        // the SQLite twin documents (never-touched assumption, density over surviving rows only, the
        // codepoint-vs-code-unit note) apply identically.
        Execute.Sql("""
            UPDATE lyntai_memory_node
            SET encoding_ordinal = ranked.ordinal,
                encoding_chars = ranked.cumulative_chars,
                encoding_at = lyntai_memory_node.created_at
            FROM (
                SELECT id,
                       ROW_NUMBER() OVER (PARTITION BY engine ORDER BY created_at, id) AS ordinal,
                       SUM(LENGTH(content)) OVER (PARTITION BY engine ORDER BY created_at, id) AS cumulative_chars
                FROM lyntai_memory_node
            ) AS ranked
            WHERE ranked.id = lyntai_memory_node.id
            """);

        Execute.Sql("ALTER TABLE lyntai_memory_position ADD COLUMN ordinal BIGINT NOT NULL DEFAULT 0");
        Execute.Sql("ALTER TABLE lyntai_memory_position ADD COLUMN chars BIGINT NOT NULL DEFAULT 0");
        Execute.Sql("""
            ALTER TABLE lyntai_memory_position
            ADD COLUMN encoded_at TIMESTAMPTZ NOT NULL DEFAULT '1970-01-01T00:00:00Z'
            """);

        Execute.Sql("""
            UPDATE lyntai_memory_position
            SET ordinal = totals.max_ordinal,
                chars = totals.max_chars,
                encoded_at = totals.max_at
            FROM (
                SELECT engine,
                       MAX(encoding_ordinal) AS max_ordinal,
                       MAX(encoding_chars) AS max_chars,
                       MAX(encoding_at) AS max_at
                FROM lyntai_memory_node
                GROUP BY engine
            ) AS totals
            WHERE totals.engine = lyntai_memory_position.engine
            """);

        // ---- provenance ------------------------------------------------------------------------------
        Execute.Sql(
            "ALTER TABLE lyntai_memory_node ADD COLUMN provenance_retrievability BIGINT NOT NULL DEFAULT 0");
        Execute.Sql(
            "ALTER TABLE lyntai_memory_node ADD COLUMN provenance_salience BIGINT NOT NULL DEFAULT 0");

        // ---- live difficulty -------------------------------------------------------------------------
        // DEFAULT 5, the neutral MID-POINT of FSRS's 1-10 scale, never the floor 1 — see the SQLite twin for
        // why starting a migrated row at the floor pins it there permanently.
        Execute.Sql("ALTER TABLE lyntai_memory_node ADD COLUMN difficulty DOUBLE PRECISION NOT NULL DEFAULT 5");

        // ---- the review log --------------------------------------------------------------------------
        Execute.Sql("""
            CREATE TABLE lyntai_memory_review (
                id               BIGSERIAL PRIMARY KEY,
                engine           TEXT NOT NULL,
                node_id          BIGINT NOT NULL,
                batch_id         TEXT NOT NULL,
                created_at       TIMESTAMPTZ NOT NULL,
                pre_age          DOUBLE PRECISION NOT NULL,
                pre_stability    DOUBLE PRECISION NOT NULL,
                pre_difficulty   DOUBLE PRECISION NOT NULL,
                pre_strength     DOUBLE PRECISION NOT NULL,
                pre_strength_age DOUBLE PRECISION NOT NULL,
                grade            DOUBLE PRECISION NULL,
                post_stability   DOUBLE PRECISION NOT NULL,
                post_difficulty  DOUBLE PRECISION NOT NULL,
                provenance_retrievability BIGINT NOT NULL DEFAULT 0,
                -- What an IMemoryVerificationPolicy judged about this entry for the recall that logged it:
                -- true = it answered the query, false = it did not, NULL = no verifier ran. The three states
                -- are NOT collapsible: `grade` above is derived from the curve's own prediction, so a fit
                -- against it recovers whatever produced the log (DECISIONS D51). This is the external
                -- observation that breaks that circularity — and, because a row is now written for entries
                -- that were NOT reinforced, it is what lets the log contain FAILURES rather than only
                -- successes, which was D51's second and harder blocker.
                verified BOOLEAN NULL
            )
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_memory_review_engine_id ON lyntai_memory_review(engine, id)");

        // ---- subjects: what a node is ABOUT ------------------------------------------------------------
        // The SQLite twin carries the full reasoning. In short: steers LINKING and never recall, and is its
        // own table rather than extra searchable text on the node, because appending subjects to the content
        // index would silently change what every ordinary recall matches.
        Execute.Sql("""
            CREATE TABLE lyntai_memory_subject (
                engine   TEXT NOT NULL,
                node_id  BIGINT NOT NULL,
                task_key TEXT NOT NULL,
                scope    TEXT NOT NULL,
                subject  TEXT NOT NULL,
                PRIMARY KEY (engine, node_id, subject)
            )
            """);
        Execute.Sql("""
            CREATE INDEX ix_lyntai_memory_subject_lookup
            ON lyntai_memory_subject(engine, subject, task_key, scope, node_id DESC)
            """);

        // ---- the three age primitives, EDGE side -----------------------------------------------------
        // MUST stay after the age-primitive block: the backfill below reads lyntai_memory_position.ordinal.
        Execute.Sql("ALTER TABLE lyntai_memory_edge ADD COLUMN strengthened_ordinal BIGINT NOT NULL DEFAULT 0");
        Execute.Sql("ALTER TABLE lyntai_memory_edge ADD COLUMN strengthened_chars BIGINT NOT NULL DEFAULT 0");
        Execute.Sql("""
            ALTER TABLE lyntai_memory_edge
            ADD COLUMN strengthened_at TIMESTAMPTZ NOT NULL DEFAULT '1970-01-01T00:00:00Z'
            """);

        // Every pre-existing edge is treated as strengthened at migration time — the safe direction, because a
        // fresher-looking edge only ever lengthens effective stability and so can only ever RETAIN. See the
        // SQLite twin for the full argument.
        Execute.Sql("""
            UPDATE lyntai_memory_edge
            SET strengthened_ordinal = totals.ordinal,
                strengthened_chars = totals.chars,
                strengthened_at = totals.encoded_at
            FROM (
                SELECT n.id AS node_id, p.ordinal, p.chars, p.encoded_at
                FROM lyntai_memory_node n
                JOIN lyntai_memory_position p ON p.engine = n.engine
            ) AS totals
            WHERE totals.node_id = lyntai_memory_edge.from_id
            """);
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE lyntai_memory_edge DROP COLUMN strengthened_at");
        Execute.Sql("ALTER TABLE lyntai_memory_edge DROP COLUMN strengthened_chars");
        Execute.Sql("ALTER TABLE lyntai_memory_edge DROP COLUMN strengthened_ordinal");

        Execute.Sql("DROP TABLE IF EXISTS lyntai_memory_subject");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_memory_review");

        Execute.Sql("ALTER TABLE lyntai_memory_node DROP COLUMN difficulty");

        Execute.Sql("ALTER TABLE lyntai_memory_node DROP COLUMN provenance_salience");
        Execute.Sql("ALTER TABLE lyntai_memory_node DROP COLUMN provenance_retrievability");

        Execute.Sql("ALTER TABLE lyntai_memory_position DROP COLUMN encoded_at");
        Execute.Sql("ALTER TABLE lyntai_memory_position DROP COLUMN chars");
        Execute.Sql("ALTER TABLE lyntai_memory_position DROP COLUMN ordinal");
        Execute.Sql("ALTER TABLE lyntai_memory_node DROP COLUMN encoding_at");
        Execute.Sql("ALTER TABLE lyntai_memory_node DROP COLUMN encoding_chars");
        Execute.Sql("ALTER TABLE lyntai_memory_node DROP COLUMN encoding_ordinal");

        Execute.Sql("ALTER TABLE lyntai_memory_node DROP COLUMN salience");
        Execute.Sql("ALTER TABLE lyntai_memory_node DROP COLUMN signals");
    }
}
