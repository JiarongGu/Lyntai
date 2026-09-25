namespace Lyntai.Storage;

/// <summary>The relational <see cref="Lyntai.Inference.Caching.IResponseCache"/> statements that run unchanged
/// on both backends. The size-cap trim stays per backend: SQLite needs <c>LIMIT -1 OFFSET @max</c> where
/// Postgres takes a bare <c>OFFSET</c>.</summary>
public static class ResponseCacheSql
{
    public const string Get =
        "SELECT reply_json FROM lyntai_response_cache WHERE cache_key = @key AND expires_at > @now";

    public const string Set = """
        INSERT INTO lyntai_response_cache (cache_key, reply_json, expires_at, created_at)
        VALUES (@key, @json, @expiresAt, @now)
        ON CONFLICT (cache_key) DO UPDATE SET reply_json = @json, expires_at = @expiresAt, created_at = @now
        """;

    public const string DeleteExpired = "DELETE FROM lyntai_response_cache WHERE expires_at <= @now";

    public const string Remove = "DELETE FROM lyntai_response_cache WHERE cache_key = @key";
}
