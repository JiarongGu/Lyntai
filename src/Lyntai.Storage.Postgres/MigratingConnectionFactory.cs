using System.Data.Common;
using Lyntai.Storage.Postgres.Migrations;

namespace Lyntai.Storage.Postgres;

/// <summary>Runs the migrations once, lazily, on the first <see cref="Open"/> — so
/// <c>UsePostgresStorage(conn, migrateOnFirstUse: true)</c> does no I/O during DI composition.
/// Thread-safe: concurrent first-opens block until the single migration completes.</summary>
public sealed class MigratingConnectionFactory : IDbConnectionFactory
{
    private readonly PostgresConnectionFactory _inner;
    private readonly Lazy<bool> _migrated;

    public MigratingConnectionFactory(string connectionString)
    {
        _inner = new PostgresConnectionFactory(connectionString);
        _migrated = new Lazy<bool>(() =>
        {
            MigrationRunnerService.MigrateUp(connectionString);
            return true;
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public DbConnection Open()
    {
        _ = _migrated.Value; // migrate-once on the first real connection
        return _inner.Open();
    }
}
