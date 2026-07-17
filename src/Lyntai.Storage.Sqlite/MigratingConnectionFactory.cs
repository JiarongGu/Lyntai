using System.Data.Common;
using Lyntai.Storage;
using Lyntai.Storage.Sqlite.Migrations;

namespace Lyntai.Storage.Sqlite;

/// <summary>
/// A connection factory that runs the migrations exactly once, lazily, on the FIRST successful
/// <see cref="Open"/> — so <c>UseSqliteStorage(path, migrateOnFirstUse: true)</c> does no I/O during DI
/// composition. Thread-safe: concurrent first-opens block until the single migration completes, and a
/// TRANSIENT first-migration failure is retried on the next Open (the flag flips only on success — no
/// permanently-cached exception, unlike a <see cref="Lazy{T}"/>).
/// </summary>
public sealed class MigratingConnectionFactory : IDbConnectionFactory
{
    private readonly SqliteConnectionFactory _inner;
    private readonly string _dbPath;
    private readonly Lock _gate = new();
    private volatile bool _migrated;

    public MigratingConnectionFactory(string dbPath)
    {
        _dbPath = dbPath;
        _inner = new SqliteConnectionFactory(dbPath);
    }

    public DbConnection Open()
    {
        if (!_migrated)
        {
            lock (_gate)
            {
                if (!_migrated)
                {
                    var dir = Path.GetDirectoryName(Path.GetFullPath(_dbPath));
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    MigrationRunnerService.MigrateUp(_dbPath); // throws → _migrated stays false → next Open retries
                    _migrated = true;
                }
            }
        }
        return _inner.Open();
    }
}
