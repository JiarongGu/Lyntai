using Dapper;

namespace Lyntai.Storage.Sqlite;

/// <summary>KV store over Lyntai's own <c>lyntai_kv</c> table (Lyntai manages the schema). An app that needs
/// its own backend registers its own <see cref="IKeyValueStore"/> impl instead (it wins over the default —
/// the domain stores register with <c>TryAdd</c>).</summary>
public sealed class SqliteKeyValueStore(IDbConnectionFactory factory) : IKeyValueStore
{
    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition("SELECT value FROM lyntai_kv WHERE key = @key", new { key }, cancellationToken: ct))
            .ConfigureAwait(false);
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO lyntai_kv (key, value, updated_at) VALUES (@key, @value, @now)
            ON CONFLICT(key) DO UPDATE SET value = @value, updated_at = @now
            """, new { key, value, now = DateTimeOffset.UtcNow }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM lyntai_kv WHERE key = @key", new { key }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ListKeysAsync(string? prefix = null, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        // NOT LIKE — SQLite's LIKE is ASCII case-INSENSITIVE, breaking the interface's case-sensitive
        // contract. substr under the default BINARY collation is an exact (ordinal) starts-with, wildcard
        // chars are literals for free, and ORDER BY key is BINARY (ordinal) too.
        var keys = await conn.QueryAsync<string>(new CommandDefinition("""
            SELECT key FROM lyntai_kv
            WHERE (@prefix IS NULL OR substr(key, 1, length(@prefix)) = @prefix)
            ORDER BY key
            """, new { prefix }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. keys];
    }
}
