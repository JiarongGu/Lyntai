using Dapper;
using Lyntai.Storage.Relational;
using Lyntai.Inference;
using Lyntai.Inference.Caching;

namespace Lyntai.Storage.Sqlite;

/// <summary>
/// SQLite-backed <see cref="IResponseCache"/> — the response cache survives process restarts (the in-memory
/// default in Core does not). The reply is stored as JSON; entries carry an expiry (TEXT ISO-8601, compared
/// chronologically). Eviction on write: expired rows are pruned, then the oldest are trimmed to
/// <see cref="CacheOptions.MaxEntries"/>. Register with <c>UseSqliteResponseCache()</c> (needs the SQLite
/// connection factory from <c>UseSqliteStorage</c>).
/// </summary>
public sealed class SqliteResponseCache(IDbConnectionFactory factory, LyntaiOptions options, Func<DateTimeOffset>? clock = null) : IResponseCache
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public async Task<TextResponse?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var json = await conn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(ResponseCacheSql.Get,
            new { key, now = _clock() }, cancellationToken: ct)).ConfigureAwait(false);
        return json is null ? null : ReflectionJson.Deserialize<TextResponse>(json);
    }

    public async Task SetAsync(string key, TextResponse reply, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var window = ttl ?? options.Cache.Ttl;
        if (window <= TimeSpan.Zero) return; // non-positive TTL disables caching
        var now = _clock();
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(ResponseCacheSql.Set, new { key, json = ReflectionJson.Serialize(reply), expiresAt = now + window, now }, cancellationToken: ct)).ConfigureAwait(false);

        // opportunistic eviction: drop expired, then trim the oldest beyond the size cap
        await conn.ExecuteAsync(new CommandDefinition(
            ResponseCacheSql.DeleteExpired, new { now }, cancellationToken: ct)).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition("""
            DELETE FROM lyntai_response_cache WHERE cache_key IN (
                SELECT cache_key FROM lyntai_response_cache ORDER BY created_at DESC, cache_key LIMIT -1 OFFSET @max)
            """, new { max = Math.Max(1, options.Cache.MaxEntries) }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(
            ResponseCacheSql.Remove, new { key }, cancellationToken: ct)).ConfigureAwait(false);
    }
}
