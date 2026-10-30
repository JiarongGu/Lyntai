using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>Why an entry was ended (<c>ended_reason</c>) and a counter that moves when its record does
/// (<c>revision</c>), so an end can be applied only to the entry as it was reviewed.
/// <para><b><c>ended_reason</c> is NULLABLE</b>, and NULL is right for every row written before this: nothing ended them
/// through the call that records a reason. <b><c>revision</c> is <c>NOT NULL DEFAULT 0</c></b>: an existing row starts at
/// zero, which is as good a starting point as any since a revision is compared and never read. Neither is indexed —
/// the engine reads both off the node.</para></summary>
[Migration(202610301000)]
[Tags(nameof(StorageFeature.Memory), StorageFeatures.AllTag)]
public sealed class M202610301000_MemoryEndReason : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE lyntai_memory_node ADD COLUMN ended_reason TEXT NULL");
        Execute.Sql("ALTER TABLE lyntai_memory_node ADD COLUMN revision INTEGER NOT NULL DEFAULT 0");
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE lyntai_memory_node DROP COLUMN revision");
        Execute.Sql("ALTER TABLE lyntai_memory_node DROP COLUMN ended_reason");
    }
}
