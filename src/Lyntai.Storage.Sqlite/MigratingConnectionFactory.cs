using System.Data.Common;
using Lyntai.Storage;
using Lyntai.Storage.Sqlite.Migrations;

namespace Lyntai.Storage.Sqlite;

/// <summary>
/// A connection factory that runs the migrations exactly once, lazily, on the FIRST <see cref="Open"/>
/// — so <c>UseSqliteStorage(path, migrateOnFirstUse: true)</c> does no I/O during DI composition
/// (composition-time file access is a problem for AOT/startup-sensitive hosts and container health
/// checks). Thread-safe: concurrent first-opens block until the single migration completes.
/// </summary>
public sealed class MigratingConnectionFactory : IDbConnectionFactory
{
    private readonly SqliteConnectionFactory _inner;
    private readonly Lazy<bool> _migrated;

    public MigratingConnectionFactory(string dbPath)
    {
        _inner = new SqliteConnectionFactory(dbPath);
        _migrated = new Lazy<bool>(() =>
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            MigrationRunnerService.MigrateUp(dbPath);
            return true;
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public DbConnection Open()
    {
        _ = _migrated.Value; // migrate-once on the first real connection
        return _inner.Open();
    }
}
