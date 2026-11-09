namespace Lyntai.Storage.Sqlite;

/// <summary>What a database path may name. An in-memory database lives and dies with ONE connection, and Lyntai
/// opens one per operation beside the migrator's own, so each would see a private empty database and the schema
/// the migrator built would be gone when a store looked for it.</summary>
internal static class SqliteDatabasePath
{
    /// <summary>Throws <see cref="ArgumentException"/> for <c>:memory:</c> and a <c>file:</c> URI naming one
    /// (<c>file::memory:</c>, <c>mode=memory</c>), shared cache included.</summary>
    /// <param name="dbPath">The path as the caller gave it.</param>
    /// <param name="paramName">The parameter to name in the exception.</param>
    public static void RequireFile(string dbPath, string paramName)
    {
        ArgumentNullException.ThrowIfNull(dbPath, paramName);
        var path = dbPath.Trim();
        var inMemory = path.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
            || (path.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                && (path.Contains(":memory:", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("mode=memory", StringComparison.OrdinalIgnoreCase)));
        if (inMemory)
            throw new ArgumentException(
                $"'{dbPath}' names an in-memory SQLite database, which each connection would see as its own empty one; " +
                "give a file path.", paramName);
    }
}
