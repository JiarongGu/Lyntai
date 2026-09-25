using Lyntai.Cortex;
using Lyntai.Storage.Postgres;

namespace Lyntai.Tests.Storage;

/// <summary>The score aggregate and export order by byte on Postgres as they do everywhere else — under the
/// database's own locale, <c>a…</c> sorts before <c>B…</c>; in byte order it is the other way round. Both
/// reads are table-wide, so each assertion looks only at this test's own rows.</summary>
[Collection("postgres")]
public sealed class PostgresScoreOrderTests(PostgresFixture pg)
{
    [SkippableFact]
    public async Task Aggregate_and_export_order_by_byte_not_by_locale()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresScoreStore(pg.Factory);
        var uid = Guid.NewGuid().ToString("N");
        string[] byteOrder = [$"B-{uid}", $"a-{uid}"];

        await store.SaveAsync($"s-{uid}", [.. byteOrder.Reverse().Select(id => new ScoredResult(id, id, "g", false, 0.5))]);

        var aggregated = (await store.AggregateAsync()).Select(a => a.ScorerId).Where(id => id.EndsWith(uid, StringComparison.Ordinal));
        var exported = (await store.ExportAsync()).Select(e => e.ScorerId).Where(id => id.EndsWith(uid, StringComparison.Ordinal));

        Assert.Equal(byteOrder, aggregated);
        Assert.Equal(byteOrder, exported);
    }
}
