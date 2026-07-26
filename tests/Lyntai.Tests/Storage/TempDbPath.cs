using Lyntai.Tests.Fakes;
using Microsoft.Data.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>Per-test fresh, UN-migrated SQLite db path under devtools/_test-dbs (family rule: scratch
/// under devtools/_*, never OS temp) — for tests that own the schema story themselves (SchemaMigration.None,
/// deferred / selective migration). Dispose clears this db's pool and deletes db + -wal + -shm; see
/// <see cref="TempDb"/> for the migrated variant.</summary>
public sealed class TempDbPath : IDisposable
{
    public TempDbPath(string prefix = "test") =>
        Path = System.IO.Path.Combine(TestPaths.TestDbsDir, $"{prefix}-{Guid.NewGuid():N}.db");

    public string Path { get; }

    public void Dispose()
    {
        // Clear ONLY this db's pool. SqliteConnection.ClearAllPools() is process-global — under the
        // parallel xUnit runner it evicts other concurrently-running tests' pooled connections mid-query,
        // which surfaced as intermittent, unrelated storage-test failures (each green in isolation).
        using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path }.ToString()))
            SqliteConnection.ClearPool(c);
        foreach (var f in new[] { Path, Path + "-wal", Path + "-shm" })
        {
            try { File.Delete(f); } catch { /* still pooled somewhere — gitignored scratch anyway */ }
        }
    }
}
