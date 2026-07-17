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
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { Path, Path + "-wal", Path + "-shm" })
        {
            try { File.Delete(f); } catch { /* still pooled somewhere — gitignored scratch anyway */ }
        }
    }
}
