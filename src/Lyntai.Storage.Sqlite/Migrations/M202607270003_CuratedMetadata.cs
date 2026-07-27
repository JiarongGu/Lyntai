using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>CMEM6 — generalise curated payload into one opaque JSON <c>metadata</c> column plus a plain
/// relational <c>lyntai_curated_meta(memory_id, key, value)</c> query index, and RETIRE the purpose-built
/// <c>source</c>/<c>title</c> columns by folding their data into <c>metadata</c> (data-preserving:
/// backfill → rebuild the FTS content-only → drop the columns). Append-only over the unreleased
/// title/search migrations. See <c>docs/2026-07-27-curated-metadata-design.md</c>.</summary>
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
                memory_id INTEGER NOT NULL REFERENCES lyntai_curated_memory(id) ON DELETE CASCADE,
                key       TEXT NOT NULL,
                value     TEXT NOT NULL,
                PRIMARY KEY (memory_id, key)
            )
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_curated_meta_kv ON lyntai_curated_meta(key, value)");

        // 2) fold source/title into the index rows (skip null/empty), then build the JSON column FROM those
        //    rows (sorted keys → canonical form, matching CuratedMetadataJson)
        Execute.Sql("INSERT INTO lyntai_curated_meta(memory_id, key, value) SELECT id, 'source', source FROM lyntai_curated_memory WHERE source IS NOT NULL AND source <> ''");
        Execute.Sql("INSERT INTO lyntai_curated_meta(memory_id, key, value) SELECT id, 'title', title FROM lyntai_curated_memory WHERE title IS NOT NULL AND title <> ''");
        Execute.Sql("""
            UPDATE lyntai_curated_memory
            SET metadata = (SELECT json_group_object(key, value)
                            FROM (SELECT key, value FROM lyntai_curated_meta
                                  WHERE memory_id = lyntai_curated_memory.id ORDER BY key))
            WHERE id IN (SELECT memory_id FROM lyntai_curated_meta)
            """);

        // 3) rebuild the FTS content-only (drop the title-referencing triggers BEFORE dropping the column)
        Execute.Sql("DROP TRIGGER IF EXISTS lyntai_curated_memory_ai");
        Execute.Sql("DROP TRIGGER IF EXISTS lyntai_curated_memory_ad");
        Execute.Sql("DROP TRIGGER IF EXISTS lyntai_curated_memory_au");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_curated_fts");
        Execute.Sql("CREATE VIRTUAL TABLE lyntai_curated_fts USING fts5(content, content='lyntai_curated_memory', content_rowid='id', tokenize='trigram')");
        Execute.Sql("""
            CREATE TRIGGER lyntai_curated_memory_ai AFTER INSERT ON lyntai_curated_memory BEGIN
                INSERT INTO lyntai_curated_fts(rowid, content) VALUES (new.id, new.content);
            END
            """);
        Execute.Sql("""
            CREATE TRIGGER lyntai_curated_memory_ad AFTER DELETE ON lyntai_curated_memory BEGIN
                INSERT INTO lyntai_curated_fts(lyntai_curated_fts, rowid, content) VALUES ('delete', old.id, old.content);
            END
            """);
        Execute.Sql("""
            CREATE TRIGGER lyntai_curated_memory_au AFTER UPDATE ON lyntai_curated_memory BEGIN
                INSERT INTO lyntai_curated_fts(lyntai_curated_fts, rowid, content) VALUES ('delete', old.id, old.content);
                INSERT INTO lyntai_curated_fts(rowid, content) VALUES (new.id, new.content);
            END
            """);
        Execute.Sql("INSERT INTO lyntai_curated_fts(rowid, content) SELECT id, content FROM lyntai_curated_memory");

        // 4) drop the retired columns (no trigger/index references them now)
        Execute.Sql("ALTER TABLE lyntai_curated_memory DROP COLUMN title");
        Execute.Sql("ALTER TABLE lyntai_curated_memory DROP COLUMN source");
    }

    public override void Down()
    {
        // best-effort reverse: re-add the columns, restore from metadata, rebuild the title-aware FTS
        Execute.Sql("ALTER TABLE lyntai_curated_memory ADD COLUMN source TEXT NULL");
        Execute.Sql("ALTER TABLE lyntai_curated_memory ADD COLUMN title TEXT NULL");
        Execute.Sql("UPDATE lyntai_curated_memory SET source = json_extract(metadata, '$.source'), title = json_extract(metadata, '$.title') WHERE metadata IS NOT NULL");

        Execute.Sql("DROP TRIGGER IF EXISTS lyntai_curated_memory_ai");
        Execute.Sql("DROP TRIGGER IF EXISTS lyntai_curated_memory_ad");
        Execute.Sql("DROP TRIGGER IF EXISTS lyntai_curated_memory_au");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_curated_fts");
        Execute.Sql("CREATE VIRTUAL TABLE lyntai_curated_fts USING fts5(content, title, content='lyntai_curated_memory', content_rowid='id', tokenize='trigram')");
        Execute.Sql("""
            CREATE TRIGGER lyntai_curated_memory_ai AFTER INSERT ON lyntai_curated_memory BEGIN
                INSERT INTO lyntai_curated_fts(rowid, content, title) VALUES (new.id, new.content, new.title);
            END
            """);
        Execute.Sql("""
            CREATE TRIGGER lyntai_curated_memory_ad AFTER DELETE ON lyntai_curated_memory BEGIN
                INSERT INTO lyntai_curated_fts(lyntai_curated_fts, rowid, content, title) VALUES ('delete', old.id, old.content, old.title);
            END
            """);
        Execute.Sql("""
            CREATE TRIGGER lyntai_curated_memory_au AFTER UPDATE ON lyntai_curated_memory BEGIN
                INSERT INTO lyntai_curated_fts(lyntai_curated_fts, rowid, content, title) VALUES ('delete', old.id, old.content, old.title);
                INSERT INTO lyntai_curated_fts(rowid, content, title) VALUES (new.id, new.content, new.title);
            END
            """);
        Execute.Sql("INSERT INTO lyntai_curated_fts(rowid, content, title) SELECT id, content, title FROM lyntai_curated_memory");

        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_curated_meta_kv");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_curated_meta");
        Execute.Sql("ALTER TABLE lyntai_curated_memory DROP COLUMN metadata");
    }
}
