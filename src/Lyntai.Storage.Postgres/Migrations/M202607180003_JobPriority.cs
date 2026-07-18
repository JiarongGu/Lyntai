using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>Adds job <c>priority</c> (higher runs first within a lane). Parallels the SQLite migration of
/// the same number. The claim index is recreated to lead with priority; the dead-letter state (<c>Dead</c>)
/// needs no schema change (<c>status</c> is TEXT).</summary>
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
        Execute.Sql("ALTER TABLE lyntai_job DROP COLUMN priority");
    }
}
