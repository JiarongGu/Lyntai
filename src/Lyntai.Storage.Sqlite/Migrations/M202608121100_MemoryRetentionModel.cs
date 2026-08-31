using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>The whole 3.0 memory-retention schema, as ONE migration — signals + promoted salience, the three
/// policy-independent age primitives (node and edge), provenance, live difficulty, and the review log.
///
/// <para><b>Why this is a squash and not six migrations.</b> Every one of the six it replaces landed AFTER
/// <c>v2.5.0</c> was cut, so no released version ever carried them and no consumer database has ever applied
/// one. That is exactly the condition <c>docs/DECISIONS.md</c> <b>D9</b> names: a pre-release migration folds
/// into its owner, and only a RELEASED table needs a new one. The 1.0 release did the same at larger scale,
/// collapsing the accreted 0.x set into the nine per-domain baselines this schema still starts from.</para>
///
/// <para><b>What is deliberately NOT folded in.</b> <c>M202608081215_MemoryGraph</c> shipped in 2.5.0, so the
/// tables below are RELEASED and every change here stays an <c>ALTER</c>. Folding these columns into that
/// <c>CREATE TABLE</c> would be the migration-number trap in <c>.claude/knowledge/storage.md</c>: FluentMigrator
/// records an applied migration by NUMBER, so a 2.5.0 database — which already recorded 202608081215 — would
/// silently skip the edited version and never receive a single one of these columns.</para>
///
/// <para><b>The one population this squash breaks, stated rather than discovered:</b> a database that already
/// applied SOME of the six — only ever a local development or test database, never a consumer's, since the six
/// were never released — will fail here on a duplicate column. Delete it and re-migrate; every test database is
/// created fresh per test, so nothing in the suite is affected. The equivalence of the squash itself is proved
/// rather than asserted: <c>PgMigrationSchemaSnapshotTests</c>/<c>MigrationSchemaSnapshotTests</c> compare a
/// freshly-migrated schema against a golden captured from the PRE-squash set, and both still match.</para>
///
/// <para><b>Ordering inside this migration is load-bearing in exactly one place.</b> The edge backfill at the
/// end reads <c>lyntai_memory_position.ordinal/chars/encoded_at</c>, which the age-primitive block above it
/// adds — so that block must stay ahead of it. Everything else is an independent column add.</para></summary>
[Migration(202608121100)]
[Tags(nameof(StorageFeature.Memory), StorageFeatures.AllTag)]
public sealed class M202608121100_MemoryRetentionModel : Migration
{
    public override void Up()
    {
        // ---- signals bag + promoted salience --------------------------------------------------------
        // The bag is one JSON object rather than a column per dimension, so a future retention dimension needs
        // no migration at all. Salience is the exception, and the rule it illustrates: a signal earns a column
        // exactly when the DATABASE must sort on it, and seeding orders by salience because a salient entry
        // must be admitted as a candidate even when it matches the query poorly. No portable index reaches
        // into a JSON blob.
        //
        // NOT NULL DEFAULT 1, deliberately: 1 is the neutral value, so every pre-existing row migrates to "no
        // opinion" and orders exactly as it did before. A nullable column would be worse than untidy —
        // ORDER BY salience DESC puts NULLs FIRST on Postgres, so every legacy row would silently outrank
        // every judged one. Wrong data rather than an error.
        Execute.Sql("ALTER TABLE lyntai_memory_node ADD COLUMN signals TEXT NULL");
        Execute.Sql("ALTER TABLE lyntai_memory_node ADD COLUMN salience REAL NOT NULL DEFAULT 1");
        // NOT a general seed-ordering index: the main seed paths lead with the COMPUTED
        // (grade = @authoritative) boolean, which this index cannot satisfy as a prefix, so they still sort.
        // What this prefix genuinely matches is the FTS-merge path's separate exact-facts sub-query, whose
        // WHERE already restricts to grade = authoritative exactly, so its ORDER BY leads with salience DESC.
        Execute.Sql("""
            CREATE INDEX ix_lyntai_memory_node_salience
            ON lyntai_memory_node(engine, task_key, scope, salience DESC)
            """);

        // ---- the three policy-independent age primitives, node side ---------------------------------
        // Tracked UNCONDITIONALLY on every write regardless of which IMemoryAgePolicy is installed, so they can
        // never drift into a mixed-unit value the way a single Advance-driven position can (design doc §5.7).
        // GraphNode.Age and GraphNodeWrite.Advance are UNCHANGED — these are an additional, coexisting view.
        Execute.Sql("ALTER TABLE lyntai_memory_node ADD COLUMN encoding_ordinal INTEGER NOT NULL DEFAULT 0");
        Execute.Sql("ALTER TABLE lyntai_memory_node ADD COLUMN encoding_chars INTEGER NOT NULL DEFAULT 0");
        Execute.Sql("""
            ALTER TABLE lyntai_memory_node
            ADD COLUMN encoding_at TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.0000000+00:00'
            """);

        // Exact backfill, ordered by (created_at, id) within each engine — id is a unique tiebreaker so two
        // writes landing in the same instant still produce a stable, monotone ordinal.
        //
        // Three properties of this backfill are disclosed rather than hidden. (1) It necessarily treats every
        // pre-migration row as if never TOUCHED since creation: a touch's historical ordinal/timestamp was
        // never persisted anywhere, and deriving from last_recalled_position would just move the
        // reinterpretation risk into the backfill. (2) It is dense over SURVIVING rows only — a row pruned
        // before this ran is invisible, so a pre-migration entry's ordinal DISTANCE from another is compressed
        // by however many rows in between were already gone. (3) SQLite's LENGTH() counts Unicode codepoints
        // while the .NET store maintaining this column counts UTF-16 code units (string.Length, matching
        // ContentSizeAgePolicy's own unit); they agree for the whole BMP and diverge by one per character only
        // for a codepoint outside it, on pre-migration rows only.
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

        Execute.Sql("ALTER TABLE lyntai_memory_position ADD COLUMN ordinal INTEGER NOT NULL DEFAULT 0");
        Execute.Sql("ALTER TABLE lyntai_memory_position ADD COLUMN chars INTEGER NOT NULL DEFAULT 0");
        Execute.Sql("""
            ALTER TABLE lyntai_memory_position
            ADD COLUMN encoded_at TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.0000000+00:00'
            """);

        // The totals as of the last write each engine's SURVIVING rows can see. An engine whose every node has
        // since been deleted keeps the placeholder default; its next write starts the count over, which is
        // harmless because nothing left in the table references the old numbers.
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
        // Which policy computed this row's retrievability-owned state (stability AND difficulty), and which
        // salience polic(ies) produced its signals — so "never computed" is distinguishable from "computed as
        // zero". Plain integers: the uniqueness/single-bit guard belongs to MemoryProvenance and the two flags
        // enums, in code, not to the schema.
        Execute.Sql(
            "ALTER TABLE lyntai_memory_node ADD COLUMN provenance_retrievability INTEGER NOT NULL DEFAULT 0");
        Execute.Sql(
            "ALTER TABLE lyntai_memory_node ADD COLUMN provenance_salience INTEGER NOT NULL DEFAULT 0");

        // ---- live difficulty -------------------------------------------------------------------------
        // Unlike salience this is never merely a promoted VIEW of the bag: DsrRetrievability.Reinforce reads
        // and writes it on every review, exactly as it does stability.
        //
        // NOT NULL DEFAULT 5 — the neutral MID-POINT of FSRS's 1-10 scale, NOT the floor 1. A brief 3.0
        // pre-release window defaulted this to 1 and that was a genuine defect: starting a migrated row at the
        // floor, combined with the derived grade being overwhelmingly Easy-leaning on the fresh successful
        // recalls that keep an entry alive (this library's own corpus measured 89.6%), pins it there
        // PERMANENTLY the moment it is next recalled. 5 is far enough from either bound that the same delta
        // lands inside [1, 10] without clamping, so the row genuinely moves.
        //
        // No index, unlike salience: nothing sorts or filters on this column — it feeds a retrievability
        // policy's arithmetic, never a seed ordering — so an index would be pure write amplification on the
        // hottest table in the schema. A NULL here would be worse than untidy for a second reason salience
        // does not have: Reinforce's difficulty law does arithmetic on it (D + (10-D)·ΔD/9).
        Execute.Sql("ALTER TABLE lyntai_memory_node ADD COLUMN difficulty REAL NOT NULL DEFAULT 5");

        // ---- the review log --------------------------------------------------------------------------
        // DATA for a future parameter-fitting task; nothing in the recall, ranking or prune paths reads it.
        // `grade` is the one deliberate NULL in this schema — null means no grade-driven update happened at
        // all, and a numeric sentinel outside [2,4] would be LESS honest, letting a lazy reader mistake "no
        // grade" for a real if unusual value.
        //
        // No index beyond (engine, id): RecordReviewsAsync's bounded-eviction trim reads "the Nth-newest row
        // for this engine" and ReviewsAsync reads "every row for this engine, oldest first" — both genuinely
        // plan against it. Nothing sorts or filters on node_id, batch_id or grade today.
        Execute.Sql("""
            CREATE TABLE lyntai_memory_review (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                engine TEXT NOT NULL,
                node_id INTEGER NOT NULL,
                batch_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                pre_age REAL NOT NULL,
                pre_stability REAL NOT NULL,
                pre_difficulty REAL NOT NULL,
                pre_strength REAL NOT NULL,
                pre_strength_age REAL NOT NULL,
                grade REAL NULL,
                post_stability REAL NOT NULL,
                post_difficulty REAL NOT NULL,
                provenance_retrievability INTEGER NOT NULL DEFAULT 0,
                -- What an IMemoryVerificationPolicy judged about this entry for the recall that logged it:
                -- 1 = it answered the query, 0 = it did not, NULL = no verifier ran.
                --
                -- NULLABLE, and the three states are NOT collapsible. `grade` above is derived from the
                -- curve's own prediction, so a fit against it recovers whatever produced the log (design
                -- DECISIONS D51). This column is the external observation that breaks that circularity —
                -- and, because a row is now written for entries that were NOT reinforced, it is also what
                -- lets the log contain FAILURES rather than only successes, which was D51's second and
                -- harder blocker.
                verified INTEGER NULL
            )
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_memory_review_engine_id ON lyntai_memory_review(engine, id)");

        // ---- subjects: what a node is ABOUT ------------------------------------------------------------
        // Its own table rather than extra searchable text on the node: appending subjects to the content
        // index would let the existing seed path find them with no new code at all, and would silently change
        // what every ordinary recall matches — a far larger blast radius than an index needs. Recall reaches
        // a subject through an exact-handle lookup instead (SubjectSeedSource).
        //
        // The primary key IS the uniqueness rule (one row per node per subject); the lookup index is what
        // NodesBySubjectAsync plans against — subject first, because that is the equality predicate, then
        // task/scope which narrow it, then id DESC for the newest-first order it returns.
        //
        // subject is stored lowercased by the store, so the equality below is a plain one on every backend
        // rather than depending on each dialect's collation rules.
        Execute.Sql("""
            CREATE TABLE lyntai_memory_subject (
                engine TEXT NOT NULL,
                node_id INTEGER NOT NULL REFERENCES lyntai_memory_node(id) ON DELETE CASCADE,
                task_key TEXT NOT NULL,
                scope TEXT NOT NULL,
                subject TEXT NOT NULL,
                PRIMARY KEY (engine, node_id, subject)
            )
            """);
        Execute.Sql("""
            CREATE INDEX ix_lyntai_memory_subject_lookup
            ON lyntai_memory_subject(engine, subject, task_key, scope, node_id DESC)
            """);

        // ---- the three age primitives, EDGE side -----------------------------------------------------
        // The strength-side counterpart of the node block above. Without these, a Derivable age policy could
        // re-derive a node's own Age from swap-safe primitives while its StrengthAge still spoke whatever unit
        // was in force at strengthening time — two units inside one retrievability expression, which is why
        // GraphMemoryEngine.PruneAsync had to refuse to delete any connected entry at all.
        //
        // MUST stay after the age-primitive block: the backfill below reads lyntai_memory_position.ordinal.
        Execute.Sql("ALTER TABLE lyntai_memory_edge ADD COLUMN strengthened_ordinal INTEGER NOT NULL DEFAULT 0");
        Execute.Sql("ALTER TABLE lyntai_memory_edge ADD COLUMN strengthened_chars INTEGER NOT NULL DEFAULT 0");
        Execute.Sql("""
            ALTER TABLE lyntai_memory_edge
            ADD COLUMN strengthened_at TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.0000000+00:00'
            """);

        // An existing edge's true strength age is unrecoverable — only strengthened_position was ever
        // persisted — so every pre-existing edge is treated as strengthened at migration time. That direction
        // is chosen, not incidental: a fresher-looking edge only ever LENGTHENS effective stability, so it can
        // only ever RETAIN an entry, while the opposite backfill would collapse the connection boost to 1x and
        // hand PruneAsync a reason to DELETE genuinely retrievable entries. Deleting a retrievable memory is
        // unrecoverable; keeping a prunable one is not. The overstatement is self-correcting: the first real
        // strengthening of an edge replaces all three values with measured ones. (The from-node's own
        // encoding_* columns are deliberately not borrowed — an edge is strengthened at or after its node was
        // written, often long after, so they systematically overstate age.)
        //
        // An edge carries no engine of its own; it reaches one through its from-node, which is also how every
        // read path already scopes edges.
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

    // SQLite gained ALTER TABLE DROP COLUMN in 3.35 and the bundled provider is newer. Reverse order of Up,
    // and an index is dropped before the column carrying it.
    public override void Down()
    {
        Execute.Sql("ALTER TABLE lyntai_memory_edge DROP COLUMN strengthened_at");
        Execute.Sql("ALTER TABLE lyntai_memory_edge DROP COLUMN strengthened_chars");
        Execute.Sql("ALTER TABLE lyntai_memory_edge DROP COLUMN strengthened_ordinal");

        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_memory_review_engine_id");
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

        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_memory_subject_lookup");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_memory_subject");

        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_memory_node_salience");
        Execute.Sql("ALTER TABLE lyntai_memory_node DROP COLUMN salience");
        Execute.Sql("ALTER TABLE lyntai_memory_node DROP COLUMN signals");
    }
}
