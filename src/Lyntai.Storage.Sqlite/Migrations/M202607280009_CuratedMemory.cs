using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>Baseline (1.0 squash) — the curated memory CATALOG (<c>ICuratedMemoryStore</c>): the
/// <c>lyntai_curated_memory</c> table with its kind/task list indexes, the relational
/// <c>lyntai_curated_meta</c> query index, and the content-only external-content FTS5 <c>trigram</c> search
/// index kept in sync by the three AFTER triggers and backfilled here. Raw SQL reproduces the EXACT stored
/// DDL of the accreted pre-1.0 migrations (byte-identical net schema): the catalog table keeps the
/// ALTER-appended <c>task</c>/<c>scope</c>/<c>metadata</c> trailing form (the retired
/// <c>source</c>/<c>title</c> columns are gone). FK-referenced <c>lyntai_curated_memory</c> is created
/// before <c>lyntai_curated_meta</c>.</summary>
[Migration(202607280009)]
[Tags(nameof(StorageFeature.CuratedMemory), StorageFeatures.AllTag)]
public sealed class M202607280009_CuratedMemory : Migration
{
    public override void Up()
    {
        Execute.Sql("""
            CREATE TABLE lyntai_curated_memory (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                kind       TEXT NOT NULL,
                content    TEXT NOT NULL,
                enabled    INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            , task TEXT NULL, scope TEXT NULL, metadata TEXT NULL)
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_curated_memory_kind ON lyntai_curated_memory(kind, enabled)");
        Execute.Sql("CREATE INDEX ix_lyntai_curated_memory_task ON lyntai_curated_memory(task, enabled)");

        Execute.Sql("""
            CREATE TABLE lyntai_curated_meta (
                memory_id INTEGER NOT NULL REFERENCES lyntai_curated_memory(id) ON DELETE CASCADE,
                key       TEXT NOT NULL,
                value     TEXT NOT NULL,
                PRIMARY KEY (memory_id, key)
            )
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_curated_meta_kv ON lyntai_curated_meta(key, value)");

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

        // backfill (no-op on a fresh db, load-bearing if the catalog ever pre-exists the index)
        Execute.Sql("INSERT INTO lyntai_curated_fts(rowid, content) SELECT id, content FROM lyntai_curated_memory");
    }

    public override void Down()
    {
        Execute.Sql("DROP TRIGGER IF EXISTS lyntai_curated_memory_au");
        Execute.Sql("DROP TRIGGER IF EXISTS lyntai_curated_memory_ad");
        Execute.Sql("DROP TRIGGER IF EXISTS lyntai_curated_memory_ai");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_curated_fts");
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_curated_meta_kv");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_curated_meta");
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_curated_memory_task");
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_curated_memory_kind");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_curated_memory");
    }
}
