using Lyntai.Storage.Sqlite;
using Lyntai.Storage.Sqlite.Migrations;

namespace Lyntai.Tests.Storage;

/// <summary>Per-test SQLite db: created + migrated under devtools/_test-dbs (family rule: scratch
/// under devtools/_*, never OS temp), deleted on dispose. The path + per-db pool-clear cleanup live in
/// <see cref="TempDbPath"/> (the un-migrated variant).</summary>
public sealed class TempDb : IDisposable
{
    private readonly TempDbPath _path = new();

    public TempDb()
    {
        MigrationRunnerService.MigrateUp(Path);
        Factory = new SqliteConnectionFactory(Path);
    }

    public string Path => _path.Path;

    public SqliteConnectionFactory Factory { get; }

    public void Dispose() => _path.Dispose();
}
