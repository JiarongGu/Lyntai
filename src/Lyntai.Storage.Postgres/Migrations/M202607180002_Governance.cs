using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>Persistent backends for the front-door governance seams: the response cache
/// (<c>IResponseCache</c>) and usage accounting (<c>IUsageTracker</c>). Parallels the SQLite migration of
/// the same number. Timestamps are native <c>timestamptz</c>; cost is <c>double precision</c>.
/// <para>The vector store's table is created LAZILY by <c>PostgresVectorStore</c> (it needs the
/// <c>vector</c>/pgvector extension), NOT here — so <c>UsePostgresStorage</c> does not require pgvector;
/// only <c>UsePostgresVectorStore</c> does.</para></summary>
[Migration(202607180002)]
[Tags(nameof(StorageFeature.Governance), StorageFeatures.AllTag)]
public sealed class M202607180002_Governance : Migration
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
        Delete.Table("lyntai_usage");
        Delete.Table("lyntai_response_cache");
    }
}
