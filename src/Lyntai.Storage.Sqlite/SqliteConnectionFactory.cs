using System.Data;
using System.Data.Common;
using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Lyntai.Storage.Sqlite;

/// <summary>Opens pooled SQLite connections with the family pragmas applied:
/// WAL journal, 5s busy timeout, foreign keys ON.</summary>
public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    static SqliteConnectionFactory()
    {
        // snake_case columns ↔ PascalCase properties (family convention)
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        // DateTimeOffset ↔ UTC. Dapper's type-handler registry is PROCESS-GLOBAL and keyed by type, so
        // this collides with Lyntai.Storage.Postgres's handler when both backends load — the two MUST be
        // behaviorally IDENTICAL (this exact class body) so whichever static ctor wins, both providers
        // round-trip correctly. SetValue=UtcDateTime is the one form that works for both (SQLite stores
        // the DateTime as ISO TEXT; Npgsql binds it to timestamptz).
        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
    }

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

    public DbConnection Open()
    {
        var conn = New();
        try
        {
            conn.Open();
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
            await conn.OpenAsync(ct).ConfigureAwait(false);
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

    // KEEP IDENTICAL to Lyntai.Storage.Postgres's DateTimeOffsetHandler: Dapper's registry is process-global,
    // so whichever backend registers last wins for BOTH (sql-storage.md §Connections). `internal` rather than
    // private is what lets DateTimeOffsetHandlerParityTests hold them to it — that test is the mechanism
    // here, not this note.
    internal sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value) =>
            parameter.Value = value.UtcDateTime;

        public override DateTimeOffset Parse(object value) => value switch
        {
            DateTimeOffset offset => offset.ToUniversalTime(),
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
            _ => throw new DataException($"cannot convert {value.GetType()} to DateTimeOffset"),
        };
    }
}
