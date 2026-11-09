using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>An index leading with <c>node_id</c> on <c>lyntai_memory_subject</c>. Deleting a node cascades to its subjects
/// through <c>node_id</c>, which SQLite runs as <c>DELETE … WHERE node_id = ?</c> once per node; the table's key leads with
/// <c>engine</c> and its lookup index with <c>engine, subject</c>, so without this every node delete scanned the whole table.
/// Postgres has no twin: its plan was not measured (<c>docs/DECISIONS.md</c> D296).</summary>
[Migration(202611091017)]
[Tags(nameof(StorageFeature.Memory), StorageFeatures.AllTag)]
public sealed class M202611091017_MemorySubjectNodeIndex : Migration
{
    public override void Up()
    {
        Execute.Sql("CREATE INDEX ix_lyntai_memory_subject_node ON lyntai_memory_subject(node_id)");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX ix_lyntai_memory_subject_node");
    }
}
