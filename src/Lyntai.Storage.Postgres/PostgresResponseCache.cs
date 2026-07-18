using Dapper;
using Lyntai.Llm;
using Lyntai.Llm.Caching;

namespace Lyntai.Storage.Postgres;

/// <summary>
/// PostgreSQL-backed <see cref="IResponseCache"/> — the response cache survives restarts and can be shared
/// across processes hitting the same database. Reply as JSON; expiry as native <c>timestamptz</c>. Eviction
/// on write: prune expired, then trim the oldest beyond <see cref="CacheOptions.MaxEntries"/>. Register with
/// <c>UsePostgresResponseCache()</c> (needs the factory + schema from <c>UsePostgresStorage</c>).
/// </summary>
public sealed class PostgresResponseCache(IDbConnectionFactory factory, LyntaiOptions options, Func<DateTimeOffset>? clock = null) : IResponseCache
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public async Task<LlmReply?> TryGetAsync(string key, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        var json = await conn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT reply_json FROM lyntai_response_cache WHERE cache_key = @key AND expires_at > @now",
            new { key, now = _clock() }, cancellationToken: ct)).ConfigureAwait(false);
        return json is null ? null : PostgresJson.Deserialize<LlmReply>(json);
    }

    public async Task SetAsync(string key, LlmReply reply, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var window = ttl ?? options.Cache.Ttl;
        if (window <= TimeSpan.Zero) return; // non-positive TTL disables caching
        var now = _clock();
        using var conn = factory.Open();
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO lyntai_response_cache (cache_key, reply_json, expires_at, created_at)
            VALUES (@key, @json, @expiresAt, @now)
            ON CONFLICT (cache_key) DO UPDATE SET reply_json = @json, expires_at = @expiresAt, created_at = @now
            """, new { key, json = PostgresJson.Serialize(reply), expiresAt = now + window, now }, cancellationToken: ct)).ConfigureAwait(false);

        // opportunistic eviction: drop expired, then trim the oldest beyond the size cap (OFFSET past the
        // newest @max rows → the surplus older ones)
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM lyntai_response_cache WHERE expires_at <= @now", new { now }, cancellationToken: ct)).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition("""
            DELETE FROM lyntai_response_cache WHERE cache_key IN (
                SELECT cache_key FROM lyntai_response_cache ORDER BY created_at DESC, cache_key OFFSET @max)
            """, new { max = Math.Max(1, options.Cache.MaxEntries) }, cancellationToken: ct)).ConfigureAwait(false);
    }
}
