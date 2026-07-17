using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>Durable jobs (design §9): the <c>lyntai_job</c> queue with lane/status/checkpoint + the
/// lease columns the atomic claim keys on. Timestamps are ISO-8601 TEXT (the shared DateTimeOffset
/// handler); id + status are TEXT (no new Dapper type handler → no process-global registry collision).</summary>
[Migration(202607180001)]
public sealed class M202607180001_Jobs : Migration
{
    public override void Up()
    {
        Execute.Sql("""
            CREATE TABLE lyntai_job (
                id TEXT PRIMARY KEY,
                lane TEXT NOT NULL,
                type TEXT NOT NULL,
                payload TEXT NOT NULL,
                status TEXT NOT NULL,
                checkpoint TEXT NULL,
                attempts INTEGER NOT NULL,
                max_attempts INTEGER NOT NULL,
                last_error TEXT NULL,
                available_at TEXT NOT NULL,
                claimed_at TEXT NULL,
                claimed_by TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            )
            """);
        // the claim predicate filters by (lane, status, available_at); this index serves it
        Execute.Sql("CREATE INDEX ix_lyntai_job_claim ON lyntai_job(lane, status, available_at)");
    }

    public override void Down()
    {
        Delete.Table("lyntai_job");
    }
}
