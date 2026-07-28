using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>Baseline (1.0 squash) — the front-door governance + semantic-memory backends: the response
/// cache (+ its expiry/created eviction indexes), per-consumer usage accounting, and the brute-force vector
/// store. Raw SQL reproduces the exact stored DDL of the accreted pre-1.0 migrations (byte-identical net
/// schema). Timestamps are ISO-8601 TEXT; cost is REAL (CAST on SELECT — SQLite integer affinity).</summary>
[Migration(202607280008)]
[Tags(nameof(StorageFeature.Governance), StorageFeatures.AllTag)]
public sealed class M202607280008_Governance : Migration
{
    public override void Up()
    {
        Execute.Sql("""
            CREATE TABLE lyntai_response_cache (
                cache_key  TEXT PRIMARY KEY,
                reply_json TEXT NOT NULL,
                expires_at TEXT NOT NULL,
                created_at TEXT NOT NULL
            )
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_response_cache_expiry ON lyntai_response_cache(expires_at)");
        Execute.Sql("CREATE INDEX ix_lyntai_response_cache_created ON lyntai_response_cache(created_at)");

        Execute.Sql("""
            CREATE TABLE lyntai_usage (
                consumer      TEXT PRIMARY KEY,
                input_tokens  INTEGER NOT NULL DEFAULT 0,
                output_tokens INTEGER NOT NULL DEFAULT 0,
                cost_usd      REAL    NOT NULL DEFAULT 0,
                calls         INTEGER NOT NULL DEFAULT 0
            )
            """);

        Execute.Sql("""
            CREATE TABLE lyntai_vector (
                collection TEXT NOT NULL,
                vec_id     TEXT NOT NULL,
                vector     TEXT NOT NULL,
                payload    TEXT NOT NULL,
                PRIMARY KEY (collection, vec_id)
            )
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE IF EXISTS lyntai_vector");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_usage");
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_response_cache_created");
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_response_cache_expiry");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_response_cache");
    }
}
