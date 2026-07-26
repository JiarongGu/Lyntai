using Dapper;

namespace Lyntai.Storage.Postgres;

public sealed class PostgresKeyValueStore(IDbConnectionFactory factory) : IKeyValueStore
{
    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT value FROM lyntai_kv WHERE key = @key", new { key }, cancellationToken: ct))
            .ConfigureAwait(false);
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO lyntai_kv (key, value, updated_at) VALUES (@key, @value, @now)
            ON CONFLICT (key) DO UPDATE SET value = @value, updated_at = @now
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
        // LikePattern escapes %/_/\ so the prefix matches literally; COLLATE "C" makes the order byte-
        // ordinal like SQLite/InMemory (the interface's ordinal-order contract), not the DB locale
        var keys = await conn.QueryAsync<string>(new CommandDefinition("""
            SELECT key FROM lyntai_kv
            WHERE (@pattern::text IS NULL OR key LIKE @pattern ESCAPE '\')
            ORDER BY key COLLATE "C"
            """, new { pattern = prefix is null ? null : LikePattern.StartsWith(prefix) },
            cancellationToken: ct)).ConfigureAwait(false);
        return [.. keys];
    }
}
