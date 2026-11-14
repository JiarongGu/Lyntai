using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>A job's place in its partition (<c>queued_at</c>): its first availability, which a retry or a poll leaves
/// where it was while moving <c>available_at</c>, so a retrying or polling head keeps the partition's later jobs
/// waiting. Backfilled from <c>available_at</c>, so a row already retrying here keeps its retry time as its place.</summary>
[Migration(202611151000)]
[Tags(nameof(StorageFeature.Jobs), StorageFeatures.AllTag)]
public sealed class M202611151000_JobQueuedAt : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE lyntai_job ADD COLUMN queued_at TIMESTAMPTZ NULL");
        Execute.Sql("UPDATE lyntai_job SET queued_at = available_at");
    }

    public override void Down() => Execute.Sql("ALTER TABLE lyntai_job DROP COLUMN queued_at");
}
