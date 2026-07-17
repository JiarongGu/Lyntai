using Dapper;

namespace Lyntai.Storage.Postgres;

public sealed class PostgresKeyValueStore(IDbConnectionFactory factory) : IKeyValueStore
{
    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        return await conn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT value FROM lyntai_app_config WHERE key = @key", new { key }, cancellationToken: ct))
            .ConfigureAwait(false);
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO lyntai_app_config (key, value, updated_at) VALUES (@key, @value, @now)
            ON CONFLICT (key) DO UPDATE SET value = @value, updated_at = @now
            """, new { key, value, now = DateTimeOffset.UtcNow }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM lyntai_app_config WHERE key = @key", new { key }, cancellationToken: ct)).ConfigureAwait(false);
    }
}
