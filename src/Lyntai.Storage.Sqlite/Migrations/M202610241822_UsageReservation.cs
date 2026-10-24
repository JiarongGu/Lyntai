using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>The usage ledger's in-flight holds (<c>lyntai_usage_reservation</c>): one row per reserved call, counted
/// against the caps beside <c>lyntai_usage</c> until it is reconciled or <c>expires_at</c> passes. A new table, so
/// nothing existing moves; under <c>Governance</c> with the ledger it sits beside.</summary>
[Migration(202610241822)]
[Tags(nameof(StorageFeature.Governance), StorageFeatures.AllTag)]
public sealed class M202610241822_UsageReservation : Migration
{
    public override void Up() =>
        Execute.Sql("""
            CREATE TABLE lyntai_usage_reservation (
                id            TEXT PRIMARY KEY,
                consumer      TEXT NOT NULL,
                input_tokens  INTEGER NOT NULL DEFAULT 0,
                output_tokens INTEGER NOT NULL DEFAULT 0,
                cost_usd      REAL    NOT NULL DEFAULT 0,
                expires_at    TEXT NOT NULL
            )
            """);

    public override void Down() => Execute.Sql("DROP TABLE IF EXISTS lyntai_usage_reservation");
}
