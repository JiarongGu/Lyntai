using Dapper;

namespace Lyntai.Storage.Postgres;

/// <summary>KV store over the <c>lyntai_kv</c> table, with the insert-if-absent capability.</summary>
public sealed class PostgresKeyValueStore(IDbConnectionFactory factory) : IConditionalKeyValueStore
{
    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        key = Utf8Text.Scrub(key);
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            KeyValueStoreSql.Get, new { key }, cancellationToken: ct))
            .ConfigureAwait(false);
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        (key, value) = (Utf8Text.Scrub(key), Utf8Text.Scrub(value));
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(KeyValueStoreSql.Set, new { key, value, now = DateTimeOffset.UtcNow }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        key = Utf8Text.Scrub(key);
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(
            KeyValueStoreSql.Delete, new { key }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<bool> SetIfAbsentAsync(string key, string value, CancellationToken ct = default)
    {
        (key, value) = (Utf8Text.Scrub(key), Utf8Text.Scrub(value));
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteAsync(new CommandDefinition(KeyValueStoreSql.SetIfAbsent,
            new { key, value, now = DateTimeOffset.UtcNow }, cancellationToken: ct)).ConfigureAwait(false) == 1;
    }

    public async Task<IReadOnlyList<string>> ListKeysAsync(string? prefix = null, CancellationToken ct = default)
    {
        prefix = Utf8Text.Scrub(prefix);
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
