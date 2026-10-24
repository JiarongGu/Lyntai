using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>A curated entry's revision (<c>revision</c>): 1 when added, one more on every update, and what a
/// revision-checked update compares against. <c>NOT NULL DEFAULT 1</c>, so every row written before this reads
/// revision 1 — never 0, which reads as a store that does not track revisions.</summary>
[Migration(202610241721)]
[Tags(nameof(StorageFeature.CuratedMemory), StorageFeatures.AllTag)]
public sealed class M202610241721_CuratedRevision : Migration
{
    public override void Up() =>
        Execute.Sql("ALTER TABLE lyntai_curated_memory ADD COLUMN revision INTEGER NOT NULL DEFAULT 1");

    public override void Down() => Execute.Sql("ALTER TABLE lyntai_curated_memory DROP COLUMN revision");
}
