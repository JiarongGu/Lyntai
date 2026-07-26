using System.Data.Common;
using Lyntai.Storage;
using Lyntai.Storage.Sqlite.Migrations;

namespace Lyntai.Storage.Sqlite;

/// <summary>
/// A connection factory that runs the migrations exactly once, lazily, on the FIRST successful open —
/// so <c>UseSqliteStorage(path, SchemaMigration.OnFirstUse)</c> does no I/O during DI composition. The
/// once-only/retry-on-transient-failure gate lives in Core's <see cref="LazyMigratingConnectionFactory"/>;
/// this wrapper supplies the SQLite specifics (create the db directory, run this package's migrations).
/// </summary>
public sealed class MigratingConnectionFactory : IDbConnectionFactory
{
    private readonly LazyMigratingConnectionFactory _core;

    public MigratingConnectionFactory(string dbPath, StorageFeature features = StorageFeature.All)
    {
        _core = new LazyMigratingConnectionFactory(new SqliteConnectionFactory(dbPath), () =>
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            MigrationRunnerService.MigrateUp(dbPath, features);
        });
    }

    public DbConnection Open() => _core.Open();

    public Task<DbConnection> OpenAsync(CancellationToken ct = default) => _core.OpenAsync(ct);
}
