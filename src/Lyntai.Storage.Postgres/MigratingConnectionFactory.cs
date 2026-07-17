using System.Data.Common;
using Lyntai.Storage.Postgres.Migrations;

namespace Lyntai.Storage.Postgres;

/// <summary>Runs the migrations exactly once, lazily, on the FIRST successful <see cref="Open"/> — so
/// <c>UsePostgresStorage(conn, migrateOnFirstUse: true)</c> does no I/O during DI composition.
/// Thread-safe; a TRANSIENT first-migration failure is retried on the next Open (the flag flips only on
/// success — no permanently-cached exception, unlike a <see cref="Lazy{T}"/>).</summary>
public sealed class MigratingConnectionFactory : IDbConnectionFactory
{
    private readonly PostgresConnectionFactory _inner;
    private readonly string _connectionString;
    private readonly Lock _gate = new();
    private volatile bool _migrated;

    public MigratingConnectionFactory(string connectionString)
    {
        _connectionString = connectionString;
        _inner = new PostgresConnectionFactory(connectionString);
    }

    public DbConnection Open()
    {
        if (!_migrated)
        {
            lock (_gate)
            {
                if (!_migrated)
                {
                    MigrationRunnerService.MigrateUp(_connectionString); // throws → retried on next Open
                    _migrated = true;
                }
            }
        }
        return _inner.Open();
    }
}
