using System.Data.Common;
using Lyntai.Storage;
using Lyntai.Storage.Postgres.Migrations;

namespace Lyntai.Storage.Postgres;

/// <summary>Runs the migrations exactly once, lazily, on the FIRST successful open — so
/// <c>UsePostgresStorage(conn, migrateOnFirstUse: true)</c> does no I/O during DI composition. The
/// once-only/retry-on-transient-failure gate lives in Core's <see cref="LazyMigratingConnectionFactory"/>;
/// this wrapper supplies the Postgres specifics (run this package's migrations).</summary>
public sealed class MigratingConnectionFactory : IDbConnectionFactory
{
    private readonly LazyMigratingConnectionFactory _core;

    public MigratingConnectionFactory(string connectionString, StorageFeature features = StorageFeature.All)
    {
        _core = new LazyMigratingConnectionFactory(new PostgresConnectionFactory(connectionString),
            () => MigrationRunnerService.MigrateUp(connectionString, features));
    }

    public DbConnection Open() => _core.Open();

    public Task<DbConnection> OpenAsync(CancellationToken ct = default) => _core.OpenAsync(ct);
}
