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
        // DateTimeOffset ↔ ISO-8601 TEXT — deterministic storage, no provider-specific magic
        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
    }

    private readonly string _connectionString;

    public SqliteConnectionFactory(string dbPath)
    {
        DbPath = dbPath;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
    }

    public string DbPath { get; }

    public DbConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        // journal_mode persists in the db but is idempotent; busy_timeout + foreign_keys are per-connection
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();
        return conn;
    }

    private sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value) =>
            parameter.Value = value.ToString("O", CultureInfo.InvariantCulture);

        public override DateTimeOffset Parse(object value) =>
            DateTimeOffset.Parse((string)value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}
