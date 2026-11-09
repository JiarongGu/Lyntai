using System.Data.Common;
using Npgsql;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>A session-level advisory lock, per DATABASE, around Lyntai's schema creation — the migration passes
/// and <c>PostgresVectorStore</c>'s <c>CREATE EXTENSION</c> — so two processes starting against an empty or old
/// database take turns instead of each reading the version table as unapplied. It owns a connection of its OWN
/// (never the migrator's or a store's), so the DDL it guards cannot release or inherit it.</summary>
internal sealed class MigrationLock : IDisposable, IAsyncDisposable
{
    // One fixed key for every Lyntai schema writer; the lock space is per database, so another application's
    // advisory locks only collide by choosing the same number.
    private const long Key = 0x4C796E7461694D31; // "LyntaiM1"

    private readonly DbConnection _connection;

    private MigrationLock(DbConnection connection) => _connection = connection;

    /// <summary>Waits for the lock on a new connection from <paramref name="connectionString"/>.</summary>
    public static MigrationLock Acquire(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            connection.Open();
            Run(connection, "SELECT pg_advisory_lock(@key)");
            return new MigrationLock(connection);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>Waits for the lock on <paramref name="connection"/>, which the lock now owns and disposes.</summary>
    public static async Task<MigrationLock> AcquireAsync(DbConnection connection)
    {
        try
        {
            await RunAsync(connection, "SELECT pg_advisory_lock(@key)").ConfigureAwait(false);
            return new MigrationLock(connection);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    // Unlocked explicitly: a pooled connection keeps its session when closed, and Npgsql's reset on return is a
    // setting a consumer may have turned off. A failed unlock still disposes the connection.
    public void Dispose()
    {
        try { Run(_connection, "SELECT pg_advisory_unlock(@key)"); }
        finally { _connection.Dispose(); }
    }

    public async ValueTask DisposeAsync()
    {
        try { await RunAsync(_connection, "SELECT pg_advisory_unlock(@key)").ConfigureAwait(false); }
        finally { await _connection.DisposeAsync().ConfigureAwait(false); }
    }

    private static void Run(DbConnection connection, string sql)
    {
        using var cmd = Command(connection, sql);
        cmd.ExecuteNonQuery();
    }

    private static async Task RunAsync(DbConnection connection, string sql)
    {
        await using var cmd = Command(connection, sql);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static DbCommand Command(DbConnection connection, string sql)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        var key = cmd.CreateParameter();
        key.ParameterName = "key";
        key.Value = Key;
        cmd.Parameters.Add(key);
        return cmd;
    }
}
