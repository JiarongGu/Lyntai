using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>Adds job <c>priority</c> (higher runs first within a lane). The claim index is recreated to
/// lead with priority so the <c>ORDER BY priority DESC, available_at</c> pick stays index-served. The
/// dead-letter state (<c>Dead</c>) needs no schema change — <c>status</c> is already TEXT.</summary>
[Migration(202607180003)]
public sealed class M202607180003_JobPriority : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE lyntai_job ADD COLUMN priority INTEGER NOT NULL DEFAULT 0");
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_job_claim");
        Execute.Sql("CREATE INDEX ix_lyntai_job_claim ON lyntai_job(lane, status, priority DESC, available_at)");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_job_claim");
        Execute.Sql("CREATE INDEX ix_lyntai_job_claim ON lyntai_job(lane, status, available_at)");
        // SQLite < 3.35 can't DROP COLUMN; the column is harmless if left (defaults 0), so Down restores the index only.
    }
}
