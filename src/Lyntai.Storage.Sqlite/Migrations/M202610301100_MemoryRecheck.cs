using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>A host's re-check text (<c>recheck</c>, opaque, never read by the library) and the instant its check last held
/// (<c>confirmed_at</c>). Both NULLABLE, and NULL is right for every earlier row: nothing was ever checked. Neither is indexed — the
/// due read filters a task's entries that carry one, which the task scan already bounds.</summary>
[Migration(202610301100)]
[Tags(nameof(StorageFeature.Memory), StorageFeatures.AllTag)]
public sealed class M202610301100_MemoryRecheck : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE lyntai_memory_node ADD COLUMN recheck TEXT NULL");
        Execute.Sql("ALTER TABLE lyntai_memory_node ADD COLUMN confirmed_at TEXT NULL");
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE lyntai_memory_node DROP COLUMN confirmed_at");
        Execute.Sql("ALTER TABLE lyntai_memory_node DROP COLUMN recheck");
    }
}
