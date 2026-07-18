using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>The curated memory CATALOG (<c>ICuratedMemoryStore</c>) — hand-managed entries grouped by
/// <c>kind</c>, each individually enable/disable-able (vs the automatic remember/recall
/// <c>lyntai_memory_entry</c> log). Timestamps are ISO-8601 TEXT (sortable); <c>enabled</c> is an INTEGER
/// bool. No FTS/cap/TTL — the catalog is small and deliberate.</summary>
[Migration(202607180003)]
public sealed class M202607180003_CuratedMemory : Migration
{
    public override void Up()
    {
        Execute.Sql("""
            CREATE TABLE lyntai_curated_memory (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                kind       TEXT NOT NULL,
                content    TEXT NOT NULL,
                source     TEXT NULL,
                enabled    INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            )
            """);
        // ListAsync filters by kind (+ enabled) and orders by kind
        Execute.Sql("CREATE INDEX ix_lyntai_curated_memory_kind ON lyntai_curated_memory(kind, enabled)");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_curated_memory_kind");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_curated_memory");
    }
}
