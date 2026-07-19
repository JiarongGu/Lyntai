using Lyntai.Storage.Postgres;
using Lyntai.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>R15 — both SQL factories register a <c>DateTimeOffsetHandler</c> into Dapper's PROCESS-GLOBAL
/// handler registry, so whichever backend's static ctor runs last wins for the whole process. The two MUST
/// therefore stay behaviorally identical or a round-trip silently corrupts on one backend. This pins that
/// invariant directly (Docker-free — just the handlers), catching a drift the moment someone edits one.</summary>
public class DateTimeOffsetHandlerParityTests
{
    private static readonly SqliteConnectionFactory.DateTimeOffsetHandler Sqlite = new();
    private static readonly PostgresConnectionFactory.DateTimeOffsetHandler Postgres = new();

    public static TheoryData<object> ParseInputs() =>
    [
        new DateTimeOffset(2026, 7, 20, 8, 30, 0, TimeSpan.FromHours(5)),   // with offset
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),            // UTC
        new DateTime(2026, 7, 20, 3, 30, 0, DateTimeKind.Utc),            // UTC-kind DateTime
        "2026-07-20T03:30:00+00:00",                                      // ISO strings
        "2026-07-20T08:30:00+05:00",
    ];

    [Theory]
    [MemberData(nameof(ParseInputs))]
    public void Parse_is_identical_across_backends(object input) =>
        Assert.Equal(Sqlite.Parse(input), Postgres.Parse(input));

    [Fact]
    public void SetValue_writes_the_same_value_on_both()
    {
        var dto = new DateTimeOffset(2026, 7, 20, 8, 30, 0, TimeSpan.FromHours(5));
        var a = new SqliteParameter();
        var b = new SqliteParameter();

        Sqlite.SetValue(a, dto);
        Postgres.SetValue(b, dto);

        Assert.Equal(a.Value, b.Value); // both write value.UtcDateTime
    }
}
