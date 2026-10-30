using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>Why an entry was ended (<c>ended_reason</c>) and a counter that moves when its record does
/// (<c>revision</c>), so an end can be applied only to the entry as it was reviewed. NULL reason is right for every
/// earlier row, and <c>revision</c> starts at zero on them; neither is indexed.</summary>
[Migration(202610301000)]
[Tags(nameof(StorageFeature.Memory), StorageFeatures.AllTag)]
public sealed class M202610301000_MemoryEndReason : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE lyntai_memory_node ADD COLUMN ended_reason TEXT NULL");
        Execute.Sql("ALTER TABLE lyntai_memory_node ADD COLUMN revision BIGINT NOT NULL DEFAULT 0");
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE lyntai_memory_node DROP COLUMN revision");
        Execute.Sql("ALTER TABLE lyntai_memory_node DROP COLUMN ended_reason");
    }
}
