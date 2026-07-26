using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>CMEM3 — adds the optional short display label <c>title</c> to the curated catalog
/// (<c>CuratedMemory.Title</c>). Nullable, no backfill — null = untitled, existing rows unchanged.
/// A SEPARATE numbered migration (not folded into the shipped curated-memory ones) because the table
/// already shipped released — folding would never re-run for adopters who applied the released version.</summary>
[Migration(202607270001)]
[Tags(nameof(StorageFeature.CuratedMemory), StorageFeatures.AllTag)]
public sealed class M202607270001_CuratedMemoryTitle : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE lyntai_curated_memory ADD COLUMN title TEXT NULL");
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE lyntai_curated_memory DROP COLUMN title");
    }
}
