namespace Lyntai.Storage;

/// <summary>The relational <see cref="IKeyValueStore"/> statements that run unchanged on both backends. The
/// prefix listing stays per backend, because the two dialects need opposite fixes: SQLite's <c>LIKE</c> is
/// case-INsensitive (so it matches with <c>substr</c>), and Postgres orders by the database locale unless told
/// <c>COLLATE "C"</c>.</summary>
public static class KeyValueStoreSql
{
    public const string Get = "SELECT value FROM lyntai_kv WHERE key = @key";

    public const string Set = """
        INSERT INTO lyntai_kv (key, value, updated_at) VALUES (@key, @value, @now)
        ON CONFLICT (key) DO UPDATE SET value = @value, updated_at = @now
        """;

    public const string Delete = "DELETE FROM lyntai_kv WHERE key = @key";
}
