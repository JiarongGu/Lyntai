using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>CMEM6 — generalise curated payload into one opaque JSON <c>metadata</c> TEXT column plus a plain
/// relational <c>lyntai_curated_meta(memory_id, key, value)</c> query index, and RETIRE the purpose-built
/// <c>source</c>/<c>title</c> columns by folding their data into <c>metadata</c> (data-preserving:
/// backfill → drop the title trigram index → drop the columns). No <c>jsonb</c> — the relational index does
/// the querying. The Postgres analogue of the SQLite migration of the same number. See
/// <c>docs/2026-07-27-curated-metadata-design.md</c>.</summary>
[Migration(202607270003)]
[Tags(nameof(StorageFeature.CuratedMemory), StorageFeatures.AllTag)]
public sealed class M202607270003_CuratedMetadata : Migration
{
    public override void Up()
    {
        // 1) the opaque JSON payload column + the relational query index (FK cascade clears the index on delete)
        Execute.Sql("ALTER TABLE lyntai_curated_memory ADD COLUMN metadata TEXT NULL");
        Execute.Sql("""
            CREATE TABLE lyntai_curated_meta (
                memory_id BIGINT NOT NULL REFERENCES lyntai_curated_memory(id) ON DELETE CASCADE,
                key       TEXT NOT NULL,
                value     TEXT NOT NULL,
                PRIMARY KEY (memory_id, key)
            )
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_curated_meta_kv ON lyntai_curated_meta(key, value)");

        // 2) fold source/title into the index rows (skip null/empty), then build the JSON column FROM those
        //    rows (sorted keys → canonical-ish form; whitespace differs from the C# codec but is harmless — the
        //    column is opaque, parsed on read).
        Execute.Sql("INSERT INTO lyntai_curated_meta(memory_id, key, value) SELECT id, 'source', source FROM lyntai_curated_memory WHERE source IS NOT NULL AND source <> ''");
        Execute.Sql("INSERT INTO lyntai_curated_meta(memory_id, key, value) SELECT id, 'title', title FROM lyntai_curated_memory WHERE title IS NOT NULL AND title <> ''");
        Execute.Sql("""
            UPDATE lyntai_curated_memory m
            SET metadata = (SELECT jsonb_object_agg(mm.key, mm.value ORDER BY mm.key)::text
                            FROM lyntai_curated_meta mm WHERE mm.memory_id = m.id)
            WHERE m.id IN (SELECT memory_id FROM lyntai_curated_meta)
            """);

        // 3) the title trigram index is gone (search is content-only); the content GIN stays
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_curated_title_trgm");

        // 4) drop the retired columns
        Execute.Sql("ALTER TABLE lyntai_curated_memory DROP COLUMN title, DROP COLUMN source");
    }

    public override void Down()
    {
        // best-effort reverse: re-add the columns, restore from metadata, rebuild the title trigram index
        Execute.Sql("ALTER TABLE lyntai_curated_memory ADD COLUMN source TEXT NULL");
        Execute.Sql("ALTER TABLE lyntai_curated_memory ADD COLUMN title TEXT NULL");
        Execute.Sql("""
            UPDATE lyntai_curated_memory
            SET source = metadata::jsonb ->> 'source', title = metadata::jsonb ->> 'title'
            WHERE metadata IS NOT NULL
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_curated_title_trgm ON lyntai_curated_memory USING gin (title gin_trgm_ops)");

        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_curated_meta_kv");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_curated_meta");
        Execute.Sql("ALTER TABLE lyntai_curated_memory DROP COLUMN metadata");
    }
}
