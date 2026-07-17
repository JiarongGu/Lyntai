using System.Data;
using System.Data.Common;
using System.Globalization;
using Dapper;
using Npgsql;

namespace Lyntai.Storage.Postgres;

/// <summary>Opens Npgsql connections. Connection pooling is on by default in the connection string;
/// each <see cref="Open"/> returns a ready pooled connection.</summary>
public sealed class PostgresConnectionFactory : IDbConnectionFactory
{
    static PostgresConnectionFactory()
    {
        // snake_case columns ↔ PascalCase properties (family convention — a global Dapper setting)
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        // timestamptz ↔ DateTimeOffset. Dapper's type-handler registry is PROCESS-GLOBAL, so this
        // collides with Lyntai.Storage.Sqlite's handler when both backends load — the two MUST stay
        // behaviorally IDENTICAL (this exact class body) so whichever wins, both round-trip correctly.
        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
    }

    private readonly string _connectionString;

    public PostgresConnectionFactory(string connectionString) => _connectionString = connectionString;

    public DbConnection Open()
    {
        var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        return conn;
    }

    // KEEP IDENTICAL to Lyntai.Storage.Sqlite's DateTimeOffsetHandler (shared global Dapper registry).
    private sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
        {
            // write UTC into timestamptz (Npgsql requires UTC-kind DateTime for timestamptz)
            parameter.Value = value.UtcDateTime;
        }

        public override DateTimeOffset Parse(object value) => value switch
        {
            DateTimeOffset dto => dto.ToUniversalTime(),
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
            _ => throw new DataException($"cannot convert {value.GetType()} to DateTimeOffset"),
        };
    }
}
