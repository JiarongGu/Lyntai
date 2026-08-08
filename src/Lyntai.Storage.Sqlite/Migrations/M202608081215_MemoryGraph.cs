using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>Graph memory (MEM2b): <c>lyntai_memory_node</c> + <c>lyntai_memory_edge</c>, with the node's
/// headline and content mirrored into an external-content FTS5 <b>trigram</b> index (indexed CJK
/// <i>substring</i> recall — unicode61 would treat a whole CJK phrase as one token and silently match
/// nothing), kept in sync by the three AFTER triggers and backfilled here.
/// <para>The edge's composite primary key and both foreign keys are declared INLINE at table creation —
/// SQLite has no <c>ALTER ADD CONSTRAINT</c>, and the <c>ON DELETE CASCADE</c> is what stops a deleted node
/// leaving edges that would resurrect it on the next traversal. That cascade only fires because every
/// connection comes from <see cref="SqliteConnectionFactory"/>, which applies <c>foreign_keys=ON</c>
/// per connection.</para>
/// <para>First migration numbered <c>yyyyMMddHHmm</c>; the nine baseline migrations keep their historical
/// <c>YYYYMMDDNNNN</c> numbers, which sort below this one and are never renumbered — a number that has been
/// applied is recorded in <c>lyntai_version_info</c>, so changing it would re-run the migration against a
/// database that already has its tables.</para></summary>
[Migration(202608081215)]
[Tags(nameof(StorageFeature.Memory), StorageFeatures.AllTag)]
public sealed class M202608081215_MemoryGraph : Migration
{
    public override void Up()
    {
        Execute.Sql("""
            CREATE TABLE lyntai_memory_node (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                engine TEXT NOT NULL,
                task_key TEXT NOT NULL,
                scope TEXT NOT NULL,
                headline TEXT NOT NULL,
                content TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                grade INTEGER NOT NULL,
                metadata TEXT NULL,
                created_at TEXT NOT NULL,
                last_recalled_at TEXT NOT NULL,
                recall_count INTEGER NOT NULL DEFAULT 0,
                stability REAL NOT NULL
            )
            """);
        // dedup on a HASH, not the content text: a unique index over full content would be large, and
        // SQLite caps indexed row size
        Execute.Sql("""
            CREATE UNIQUE INDEX ux_lyntai_memory_node_dedup
            ON lyntai_memory_node(engine, task_key, scope, content_hash)
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_memory_node_scope ON lyntai_memory_node(engine, task_key, scope)");

        Execute.Sql("""
            CREATE TABLE lyntai_memory_edge (
                from_id INTEGER NOT NULL REFERENCES lyntai_memory_node(id) ON DELETE CASCADE,
                to_id INTEGER NOT NULL REFERENCES lyntai_memory_node(id) ON DELETE CASCADE,
                kind TEXT NOT NULL DEFAULT '',
                weight REAL NOT NULL,
                strengthened_at TEXT NOT NULL,
                PRIMARY KEY (from_id, to_id, kind)
            )
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_memory_edge_to ON lyntai_memory_edge(to_id)");

        Execute.Sql("""
            CREATE VIRTUAL TABLE lyntai_memory_node_fts USING fts5(
                headline, content, content='lyntai_memory_node', content_rowid='id', tokenize='trigram')
            """);

        // THREE triggers, and the 'delete' command row on UPDATE as well as DELETE — miss it and the index
        // corrupts silently, with stale rows matching forever. Two indexed columns, so both are supplied.
        Execute.Sql("""
            CREATE TRIGGER lyntai_memory_node_ai AFTER INSERT ON lyntai_memory_node BEGIN
                INSERT INTO lyntai_memory_node_fts(rowid, headline, content)
                VALUES (new.id, new.headline, new.content);
            END
            """);
        Execute.Sql("""
            CREATE TRIGGER lyntai_memory_node_ad AFTER DELETE ON lyntai_memory_node BEGIN
                INSERT INTO lyntai_memory_node_fts(lyntai_memory_node_fts, rowid, headline, content)
                VALUES ('delete', old.id, old.headline, old.content);
            END
            """);
        Execute.Sql("""
            CREATE TRIGGER lyntai_memory_node_au AFTER UPDATE OF headline, content ON lyntai_memory_node BEGIN
                INSERT INTO lyntai_memory_node_fts(lyntai_memory_node_fts, rowid, headline, content)
                VALUES ('delete', old.id, old.headline, old.content);
                INSERT INTO lyntai_memory_node_fts(rowid, headline, content)
                VALUES (new.id, new.headline, new.content);
            END
            """);

        // backfill in the SAME migration (no-op on a fresh db, load-bearing if the table pre-exists)
        Execute.Sql("""
            INSERT INTO lyntai_memory_node_fts(rowid, headline, content)
            SELECT id, headline, content FROM lyntai_memory_node
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TRIGGER IF EXISTS lyntai_memory_node_au");
        Execute.Sql("DROP TRIGGER IF EXISTS lyntai_memory_node_ad");
        Execute.Sql("DROP TRIGGER IF EXISTS lyntai_memory_node_ai");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_memory_node_fts");
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_memory_edge_to");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_memory_edge");
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_memory_node_scope");
        Execute.Sql("DROP INDEX IF EXISTS ux_lyntai_memory_node_dedup");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_memory_node");
    }
}
