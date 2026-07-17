using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>lyntai_memory_entry + external-content FTS5 <c>trigram</c> index (indexed CJK *substring*
/// recall — unicode61 would treat a whole CJK phrase as one token), kept in sync by AFTER triggers
/// and backfilled in this same migration.</summary>
[Migration(202607170003)]
public sealed class M202607170003_Memory : Migration
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
            )
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_memory_task_scope ON lyntai_memory_entry(task_key, scope)");

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
            CREATE TRIGGER lyntai_memory_entry_au AFTER UPDATE ON lyntai_memory_entry BEGIN
                INSERT INTO lyntai_memory_fts(lyntai_memory_fts, rowid, content) VALUES ('delete', old.id, old.content);
                INSERT INTO lyntai_memory_fts(rowid, content) VALUES (new.id, new.content);
            END
            """);

        // backfill (no-op on a fresh db, load-bearing if the table ever pre-exists the index)
        Execute.Sql("INSERT INTO lyntai_memory_fts(rowid, content) SELECT id, content FROM lyntai_memory_entry");
    }

    public override void Down()
    {
        Execute.Sql("DROP TRIGGER IF EXISTS lyntai_memory_entry_ai");
        Execute.Sql("DROP TRIGGER IF EXISTS lyntai_memory_entry_ad");
        Execute.Sql("DROP TRIGGER IF EXISTS lyntai_memory_entry_au");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_memory_fts");
        Delete.Table("lyntai_memory_entry");
    }
}
