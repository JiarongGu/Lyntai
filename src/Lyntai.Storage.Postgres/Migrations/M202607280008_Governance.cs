using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>Baseline (1.0 squash) — the front-door governance backends (Postgres leg, parallels the SQLite
/// baseline of the same number): the response cache (+ its expiry/created eviction indexes) and
/// per-consumer usage accounting. Timestamps are native <c>timestamptz</c>; cost is <c>double precision</c>.
/// <para>The vector store's table (<c>lyntai_vector</c>) is created LAZILY by <c>PostgresVectorStore</c> (it
/// needs the <c>vector</c>/pgvector extension), NOT here — so <c>UsePostgresStorage</c> does not require
/// pgvector; only <c>UsePostgresVectorStore</c> does. (This is where the SQLite leg differs: SQLite's
/// vector store is a plain TEXT table, so it lands in that backend's Governance baseline.)</para></summary>
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
                expires_at TIMESTAMPTZ NOT NULL,
                created_at TIMESTAMPTZ NOT NULL
            )
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_response_cache_expiry ON lyntai_response_cache(expires_at)");
        Execute.Sql("CREATE INDEX ix_lyntai_response_cache_created ON lyntai_response_cache(created_at)");

        Execute.Sql("""
            CREATE TABLE lyntai_usage (
                consumer      TEXT PRIMARY KEY,
                input_tokens  BIGINT NOT NULL DEFAULT 0,
                output_tokens BIGINT NOT NULL DEFAULT 0,
                cost_usd      DOUBLE PRECISION NOT NULL DEFAULT 0,
                calls         BIGINT NOT NULL DEFAULT 0
            )
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE IF EXISTS lyntai_usage CASCADE");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_response_cache CASCADE");
    }
}
