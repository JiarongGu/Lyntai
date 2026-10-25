using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>A node's valid time: when the fact held in the world (<c>valid_from</c>, <c>valid_to</c>, as its writer
/// stated them) and what ended it (<c>superseded_by</c>, <c>superseded_at</c>, as a later write recorded them).
/// <para><b>All four are NULLABLE, and NULL is the right value for every row written before this</b>: an entry
/// with no stated bound is valid at every instant, which is what each of them always was — so there is nothing to
/// backfill. <c>superseded_by</c> is a plain id, not a foreign key: removing a successor must not reopen what it
/// ended, and ids are never reissued. No index: the engine judges validity itself, as it judges burial.</para></summary>
[Migration(202610251239)]
[Tags(nameof(StorageFeature.Memory), StorageFeatures.AllTag)]
public sealed class M202610251239_MemoryValidTime : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE lyntai_memory_node ADD COLUMN valid_from TIMESTAMPTZ NULL");
        Execute.Sql("ALTER TABLE lyntai_memory_node ADD COLUMN valid_to TIMESTAMPTZ NULL");
        Execute.Sql("ALTER TABLE lyntai_memory_node ADD COLUMN superseded_by BIGINT NULL");
        Execute.Sql("ALTER TABLE lyntai_memory_node ADD COLUMN superseded_at TIMESTAMPTZ NULL");
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE lyntai_memory_node DROP COLUMN superseded_at");
        Execute.Sql("ALTER TABLE lyntai_memory_node DROP COLUMN superseded_by");
        Execute.Sql("ALTER TABLE lyntai_memory_node DROP COLUMN valid_to");
        Execute.Sql("ALTER TABLE lyntai_memory_node DROP COLUMN valid_from");
    }
}
