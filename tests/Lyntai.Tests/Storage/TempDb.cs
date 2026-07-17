using Lyntai.Storage.Sqlite;
using Lyntai.Storage.Sqlite.Migrations;
using Microsoft.Data.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>Per-test SQLite db: created + migrated under devtools/_test-dbs (family rule: scratch
/// under devtools/_*, never OS temp), deleted on dispose.</summary>
public sealed class TempDb : IDisposable
{
    public TempDb()
    {
        var dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "devtools", "_test-dbs"));
        Directory.CreateDirectory(dir);
        Path = System.IO.Path.Combine(dir, $"test-{Guid.NewGuid():N}.db");
        MigrationRunnerService.MigrateUp(Path);
        Factory = new SqliteConnectionFactory(Path);
    }

    public string Path { get; }

    public SqliteConnectionFactory Factory { get; }

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
