using System.Data;
using System.Globalization;
using Dapper;

namespace Lyntai.Storage.Relational;

/// <summary>The Dapper conventions every relational adapter relies on, compiled into each of them from this
/// ONE source. Dapper's type map and handler registry are PROCESS-GLOBAL, so whichever adapter registers
/// last wins for every connection of every backend — one source is what makes that harmless.</summary>
internal static class DapperConventions
{
    private static int _registered;

    /// <summary>snake_case columns ↔ PascalCase properties, and <see cref="DateTimeOffset"/> ↔ UTC. Idempotent.</summary>
    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1) return;
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
    }

    // UtcDateTime is the one write form both drivers take: SQLite stores it as ISO TEXT, and Npgsql binds a
    // UTC-kind DateTime to timestamptz and refuses any other kind.
    private sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
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
