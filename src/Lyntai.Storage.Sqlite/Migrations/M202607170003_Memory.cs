using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>memory_entry + external-content FTS5 <c>trigram</c> index (indexed CJK *substring*
/// recall — unicode61 would treat a whole CJK phrase as one token), kept in sync by AFTER triggers
/// and backfilled in this same migration.</summary>
[Migration(202607170003)]
public sealed class M202607170003_Memory : Migration
{
    public override void Up()
    {
        Execute.Sql("""
            CREATE TABLE memory_entry (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                task_key TEXT NOT NULL,
                scope TEXT NOT NULL,
                content TEXT NOT NULL,
                created_at TEXT NOT NULL
            )
            """);
        Execute.Sql("CREATE INDEX ix_memory_task_scope ON memory_entry(task_key, scope)");

        Execute.Sql("CREATE VIRTUAL TABLE memory_fts USING fts5(content, content='memory_entry', content_rowid='id', tokenize='trigram')");

        Execute.Sql("""
            CREATE TRIGGER memory_entry_ai AFTER INSERT ON memory_entry BEGIN
                INSERT INTO memory_fts(rowid, content) VALUES (new.id, new.content);
            END
            """);
        Execute.Sql("""
            CREATE TRIGGER memory_entry_ad AFTER DELETE ON memory_entry BEGIN
                INSERT INTO memory_fts(memory_fts, rowid, content) VALUES ('delete', old.id, old.content);
            END
            """);
        Execute.Sql("""
            CREATE TRIGGER memory_entry_au AFTER UPDATE ON memory_entry BEGIN
                INSERT INTO memory_fts(memory_fts, rowid, content) VALUES ('delete', old.id, old.content);
                INSERT INTO memory_fts(rowid, content) VALUES (new.id, new.content);
            END
            """);

        // backfill (no-op on a fresh db, load-bearing if the table ever pre-exists the index)
        Execute.Sql("INSERT INTO memory_fts(rowid, content) SELECT id, content FROM memory_entry");
    }

    public override void Down()
    {
        Execute.Sql("DROP TRIGGER IF EXISTS memory_entry_ai");
        Execute.Sql("DROP TRIGGER IF EXISTS memory_entry_ad");
        Execute.Sql("DROP TRIGGER IF EXISTS memory_entry_au");
        Execute.Sql("DROP TABLE IF EXISTS memory_fts");
        Delete.Table("memory_entry");
    }
}
