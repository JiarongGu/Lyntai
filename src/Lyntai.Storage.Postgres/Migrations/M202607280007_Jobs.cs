using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>Baseline (1.0 squash) — the durable-jobs queue (Postgres leg, parallels the SQLite baseline of
/// the same number): <c>lyntai_job</c> (lane/status/checkpoint, the atomic-claim lease columns, priority,
/// live progress/step-log, actor partition key) with the claim index and the partition index. Timestamps
/// are native <c>timestamptz</c>; id + status are TEXT (same as SQLite).</summary>
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
                available_at TIMESTAMPTZ NOT NULL,
                claimed_at TIMESTAMPTZ NULL,
                claimed_by TEXT NULL,
                created_at TIMESTAMPTZ NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL,
                priority INTEGER NOT NULL DEFAULT 0,
                cancel_requested BOOLEAN NOT NULL DEFAULT FALSE,
                progress INTEGER NOT NULL DEFAULT 0,
                total INTEGER NOT NULL DEFAULT 0,
                stage TEXT NULL,
                step_log TEXT NULL,
                partition_key TEXT NULL
            )
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_job_claim ON lyntai_job(lane, status, priority DESC, available_at)");
        // the partition guard's self-referencing subqueries key on (lane, partition_key); this serves them
        Execute.Sql("CREATE INDEX ix_lyntai_job_partition ON lyntai_job(lane, partition_key)");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_job_partition");
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_job_claim");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_job CASCADE");
    }
}
