using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>Durable jobs (design §9): the <c>lyntai_job</c> queue. Parallels the SQLite migration of the
/// same number. Timestamps are real <c>timestamptz</c> (Npgsql-native — no string-compare trap); id +
/// status are TEXT (same as SQLite, so no native-uuid/Dapper-handler divergence).</summary>
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
                available_at TIMESTAMPTZ NOT NULL,
                claimed_at TIMESTAMPTZ NULL,
                claimed_by TEXT NULL,
                created_at TIMESTAMPTZ NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL
            )
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_job_claim ON lyntai_job(lane, status, available_at)");
    }

    public override void Down()
    {
        Delete.Table("lyntai_job");
    }
}
