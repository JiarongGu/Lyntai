using System.Data.Common;
using Lyntai.Storage.Relational;
using Microsoft.Data.Sqlite;

namespace Lyntai.Storage.Sqlite;

/// <summary>Opens pooled SQLite connections with the family pragmas applied:
/// WAL journal, 5s busy timeout, foreign keys ON.</summary>
public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    static SqliteConnectionFactory() => DapperConventions.Register();

    private readonly string _connectionString;

    public SqliteConnectionFactory(string dbPath)
    {
        DbPath = dbPath;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
    }

    /// <summary>The database path this factory was constructed with — for diagnostics, backup, or locating
    /// the file a BYO caller handed in. Connections are opened from the connection string built from it in
    /// the constructor, not from this property.</summary>
    public string DbPath { get; }

    // journal_mode persists in the db but is idempotent; busy_timeout + foreign_keys are per-connection
    private const string Pragmas = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";

    // Renting from the driver's pool is serialized, process-wide because the pool is keyed by connection string:
    // the driver marks a fresh handle active BEFORE it records who holds it, and a rent racing that gap reads the
    // handle as leaked, returns it to the pool and hands it out a second time — two callers on one handle, which
    // surfaces as "cannot start a transaction within a transaction" or a disposed 'SQLitePCL.sqlite3'. A rent is a
    // pool pop or one file open and never waits on a database lock, so the serialization is cheap. The driver fixed
    // this in 10.0.13 (dotnet/efcore#39008); the lock stays for a consumer that pins an older patch.
    private static readonly object RentLock = new();

    private static void Rent(SqliteConnection conn)
    {
        lock (RentLock) conn.Open();
    }

    public DbConnection Open()
    {
        var conn = New();
        try
        {
            Rent(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = Pragmas;
            cmd.ExecuteNonQuery();
            return conn;
        }
        catch
        {
            conn.Dispose(); // a half-open connection would hold the file until a finalizer ran
            throw;
        }
    }

    public async Task<DbConnection> OpenAsync(CancellationToken ct = default)
    {
        var conn = New();
        try
        {
            ct.ThrowIfCancellationRequested();
            Rent(conn); // the driver's OpenAsync is Open() run synchronously, so nothing is lost by calling it direct
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = Pragmas;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return conn;
        }
        catch
        {
            await conn.DisposeAsync().ConfigureAwait(false); // as above
            throw;
        }
    }

    // Two INDEPENDENT lock-wait layers: PRAGMA busy_timeout (5s, inside SQLite) and the driver's own
    // busy/locked retry loop bounded by the command timeout (30s here, set rather than inherited so the
    // worst-case wait ceiling is a documented choice).
    private SqliteConnection New() => new(_connectionString) { DefaultTimeout = 30 };
}
