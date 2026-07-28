using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>Baseline (1.0 squash) — the durable-jobs queue (<c>lyntai_job</c>: lane/status/checkpoint,
/// the atomic-claim lease columns, priority, live progress/step-log, actor partition key) with the claim
/// index and the partition index. Raw SQL reproduces the exact stored DDL of the accreted pre-1.0
/// migrations (byte-identical net schema).</summary>
[Migration(202607280007)]
[Tags(nameof(StorageFeature.Jobs), StorageFeatures.AllTag)]
public sealed class M202607280007_Jobs : Migration
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
                updated_at TEXT NOT NULL,
                priority INTEGER NOT NULL DEFAULT 0,
                cancel_requested INTEGER NOT NULL DEFAULT 0,
                progress INTEGER NOT NULL DEFAULT 0,
                total INTEGER NOT NULL DEFAULT 0,
                stage TEXT NULL,
                step_log TEXT NULL,
                partition_key TEXT NULL
            )
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_job_claim ON lyntai_job(lane, status, priority DESC, available_at)");
        Execute.Sql("CREATE INDEX ix_lyntai_job_partition ON lyntai_job(lane, partition_key)");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_job_partition");
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_job_claim");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_job");
    }
}
