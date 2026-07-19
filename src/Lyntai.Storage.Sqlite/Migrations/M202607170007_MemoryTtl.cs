using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>Adds optional per-entry expiry to memory. The FTS external-content index only mirrors
/// <c>content</c>, so a new nullable column needs no trigger changes.</summary>
[Migration(202607170007)]
[Tags(nameof(StorageFeature.Memory), StorageFeatures.AllTag)]
public sealed class M202607170007_MemoryTtl : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE lyntai_memory_entry ADD COLUMN expires_at TEXT NULL");
        // recall filters on (task, scope, expiry) — index the expiry probe alongside the scope filter
        Execute.Sql("CREATE INDEX ix_lyntai_memory_expiry ON lyntai_memory_entry(task_key, scope, expires_at)");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_memory_expiry");
        // SQLite can't DROP COLUMN before 3.35; recreating the table here is out of scope for a down —
        // the column is nullable and harmless if left, so Down just removes the index.
    }
}
