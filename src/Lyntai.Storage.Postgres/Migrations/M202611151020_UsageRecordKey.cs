using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>The usage ledger's spent keys (<c>lyntai_usage_record_key</c>): one row per key
/// <c>IIdempotentUsageTracker.RecordOnceAsync</c> recorded, written in the transaction that adds the spend to
/// <c>lyntai_usage</c>, and removed only by <c>PruneRecordKeysAsync</c>. A new table, so nothing existing moves and
/// there is nothing to backfill; under <c>Governance</c> with the ledger it sits beside.</summary>
[Migration(202611151020)]
[Tags(nameof(StorageFeature.Governance), StorageFeatures.AllTag)]
public sealed class M202611151020_UsageRecordKey : Migration
{
    public override void Up() =>
        Execute.Sql("""
            CREATE TABLE lyntai_usage_record_key (
                record_key  TEXT PRIMARY KEY,
                recorded_at TIMESTAMPTZ NOT NULL
            )
            """);

    public override void Down() => Execute.Sql("DROP TABLE IF EXISTS lyntai_usage_record_key");
}
