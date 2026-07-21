using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>CM1 — adds optional per-consumer <c>task</c> + per-variant <c>scope</c> to the curated catalog
/// (<c>ICuratedMemoryStore.ForCompositionAsync</c>). Both nullable; null = "applies everywhere", so existing
/// rows keep their historical global behavior (no backfill). Parallels the SQLite migration of the same
/// number; a SEPARATE migration (not folded into <c>M202607180003</c>) because that table shipped released.</summary>
[Migration(202607220001)]
[Tags(nameof(StorageFeature.CuratedMemory), StorageFeatures.AllTag)]
public sealed class M202607220001_CuratedMemoryTaskScope : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE lyntai_curated_memory ADD COLUMN task TEXT NULL");
        Execute.Sql("ALTER TABLE lyntai_curated_memory ADD COLUMN scope TEXT NULL");
        Execute.Sql("CREATE INDEX ix_lyntai_curated_memory_task ON lyntai_curated_memory(task, enabled)");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_curated_memory_task");
        Execute.Sql("ALTER TABLE lyntai_curated_memory DROP COLUMN IF EXISTS scope");
        Execute.Sql("ALTER TABLE lyntai_curated_memory DROP COLUMN IF EXISTS task");
    }
}
