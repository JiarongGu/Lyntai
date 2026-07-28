using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>Baseline (1.0 squash) — the remember/recall memory log: <c>lyntai_memory_entry</c> with dedup +
/// expiry indexes and its external-content FTS5 <c>trigram</c> index (indexed CJK *substring* recall —
/// unicode61 would treat a whole CJK phrase as one token), kept in sync by the three AFTER triggers and
/// backfilled here. Raw SQL reproduces the EXACT stored DDL of the accreted pre-1.0 migrations
/// (byte-identical net schema): the table text keeps the ALTER-appended <c>expires_at</c>/
/// <c>last_accessed_at</c> trailing form, and the update trigger is scoped <c>AFTER UPDATE OF content</c>.</summary>
[Migration(202607280003)]
[Tags(nameof(StorageFeature.Memory), StorageFeatures.AllTag)]
public sealed class M202607280003_Memory : Migration
{
    public override void Up()
    {
        Execute.Sql("""
            CREATE TABLE lyntai_memory_entry (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                task_key TEXT NOT NULL,
                scope TEXT NOT NULL,
                content TEXT NOT NULL,
                created_at TEXT NOT NULL
            , expires_at TEXT NULL, last_accessed_at TEXT NULL)
            """);
        Execute.Sql("CREATE UNIQUE INDEX ux_lyntai_memory_dedup ON lyntai_memory_entry(task_key, scope, content)");
        Execute.Sql("CREATE INDEX ix_lyntai_memory_expiry ON lyntai_memory_entry(task_key, scope, expires_at)");

        Execute.Sql("CREATE VIRTUAL TABLE lyntai_memory_fts USING fts5(content, content='lyntai_memory_entry', content_rowid='id', tokenize='trigram')");

        Execute.Sql("""
            CREATE TRIGGER lyntai_memory_entry_ai AFTER INSERT ON lyntai_memory_entry BEGIN
                INSERT INTO lyntai_memory_fts(rowid, content) VALUES (new.id, new.content);
            END
            """);
        Execute.Sql("""
            CREATE TRIGGER lyntai_memory_entry_ad AFTER DELETE ON lyntai_memory_entry BEGIN
                INSERT INTO lyntai_memory_fts(lyntai_memory_fts, rowid, content) VALUES ('delete', old.id, old.content);
            END
            """);
        Execute.Sql("""
            CREATE TRIGGER lyntai_memory_entry_au AFTER UPDATE OF content ON lyntai_memory_entry BEGIN
                INSERT INTO lyntai_memory_fts(lyntai_memory_fts, rowid, content) VALUES ('delete', old.id, old.content);
                INSERT INTO lyntai_memory_fts(rowid, content) VALUES (new.id, new.content);
            END
            """);

        // backfill (no-op on a fresh db, load-bearing if the table ever pre-exists the index)
        Execute.Sql("INSERT INTO lyntai_memory_fts(rowid, content) SELECT id, content FROM lyntai_memory_entry");
    }

    public override void Down()
    {
        Execute.Sql("DROP TRIGGER IF EXISTS lyntai_memory_entry_au");
        Execute.Sql("DROP TRIGGER IF EXISTS lyntai_memory_entry_ad");
        Execute.Sql("DROP TRIGGER IF EXISTS lyntai_memory_entry_ai");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_memory_fts");
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_memory_expiry");
        Execute.Sql("DROP INDEX IF EXISTS ux_lyntai_memory_dedup");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_memory_entry");
    }
}
