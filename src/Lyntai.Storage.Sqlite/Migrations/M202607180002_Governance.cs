using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>Persistent backends for the front-door governance + semantic-memory seams: the response cache
/// (<c>IResponseCache</c>), usage accounting (<c>IUsageTracker</c>), and the vector store
/// (<c>IVectorStore</c>). Timestamps are TEXT (ISO-8601, sortable → chronological string compares, like
/// the job store); cost is REAL (wrap in <c>CAST(x AS REAL)</c> on SELECT — SQLite integer affinity).</summary>
[Migration(202607180002)]
[Tags(nameof(StorageFeature.Governance), StorageFeatures.AllTag)]
public sealed class M202607180002_Governance : Migration
{
    public override void Up()
    {
        // response cache: key → the serialized reply + its expiry
        Execute.Sql("""
            CREATE TABLE lyntai_response_cache (
                cache_key  TEXT PRIMARY KEY,
                reply_json TEXT NOT NULL,
                expires_at TEXT NOT NULL,
                created_at TEXT NOT NULL
            )
            """);
        // eviction probes expiry (freshness) and created_at (oldest-first overflow trim)
        Execute.Sql("CREATE INDEX ix_lyntai_response_cache_expiry ON lyntai_response_cache(expires_at)");
        Execute.Sql("CREATE INDEX ix_lyntai_response_cache_created ON lyntai_response_cache(created_at)");

        // usage accounting: one row per consumer; the global total is a SUM aggregate across rows
        Execute.Sql("""
            CREATE TABLE lyntai_usage (
                consumer      TEXT PRIMARY KEY,
                input_tokens  INTEGER NOT NULL DEFAULT 0,
                output_tokens INTEGER NOT NULL DEFAULT 0,
                cost_usd      REAL    NOT NULL DEFAULT 0,
                calls         INTEGER NOT NULL DEFAULT 0
            )
            """);

        // semantic-memory vectors: brute-force cosine (a whole collection is loaded per search), so this is
        // just a keyed store of the vector (JSON float array) + its text payload, one collection per task+scope
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
