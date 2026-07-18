using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>The curated memory CATALOG (<c>ICuratedMemoryStore</c>) — hand-managed entries grouped by
/// <c>kind</c>, each individually enable/disable-able (vs the automatic <c>lyntai_memory_entry</c> log).
/// Parallels the SQLite migration of the same number; <c>enabled</c> is a native BOOLEAN and timestamps
/// are <c>timestamptz</c>.</summary>
[Migration(202607180003)]
public sealed class M202607180003_CuratedMemory : Migration
{
    public override void Up()
    {
        Execute.Sql("""
            CREATE TABLE lyntai_curated_memory (
                id         BIGSERIAL PRIMARY KEY,
                kind       TEXT NOT NULL,
                content    TEXT NOT NULL,
                source     TEXT NULL,
                enabled    BOOLEAN NOT NULL DEFAULT TRUE,
                created_at TIMESTAMPTZ NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL
            )
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_curated_memory_kind ON lyntai_curated_memory(kind, enabled)");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_curated_memory_kind");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_curated_memory");
    }
}
