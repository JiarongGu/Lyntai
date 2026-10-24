using Lyntai.Inference;
using Lyntai;
using Lyntai.Inference.Budgeting;
using Lyntai.Inference.Caching;
using Lyntai.Memory;
using Lyntai.Storage.Postgres;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Memory;
using Xunit;

namespace Lyntai.Tests.Storage;

/// <summary>What only a persistent Postgres governance or semantic-memory backend can show, against the real
/// container (Testcontainers, pgvector image); the portable contracts run in <c>PostgresContractTests</c>.
/// Skips when Docker is unavailable, and scopes to unique keys so it shares the one migrated database.</summary>
[Collection("postgres")]
public sealed class PostgresGovernanceStoreTests(PostgresFixture pg)
{
    private static string Uid() => Guid.NewGuid().ToString("N");

    /// <summary>A reply survives the INSTANCE that cached it — what a persistent backend is for, and the one
    /// property the portable contract cannot state, since only a persistent cache has two handles over one
    /// store.</summary>
    [SkippableFact]
    public async Task ResponseCache_persists_a_reply_across_store_instances()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var options = new LyntaiOptions();
        var key = Uid();
        await new PostgresResponseCache(pg.Factory, options)
            .SetAsync(key, new TextResponse("pg cached", ProviderVerdict.Ok, new TextUsage(3, 4, CostUsd: 0.05)));

        var got = await new PostgresResponseCache(pg.Factory, options).GetAsync(key); // fresh instance

        Assert.NotNull(got);
        Assert.Equal("pg cached", got!.Text);
        Assert.Equal(0.05, got.Usage!.CostUsd);
    }

    [SkippableFact]
    public async Task ResponseCache_evicts_the_oldest_beyond_max_entries()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var options = new LyntaiOptions();
        options.Cache.MaxEntries = 2;
        // Clock far in the FUTURE: the trim keeps "the newest @max" TABLE-wide, so this test's three
        // entries must strictly outrank other tests' present-time rows on the shared container (tests in
        // the postgres collection run serially, so evicting older leftovers is harmless).
        var clock = new MutableClock { Now = new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        var cache = new PostgresResponseCache(pg.Factory, options, clock.Get);
        var (a, b, c) = (Uid(), Uid(), Uid());

        await cache.SetAsync(a, new TextResponse("a", ProviderVerdict.Ok)); clock.Advance(TimeSpan.FromSeconds(1));
        await cache.SetAsync(b, new TextResponse("b", ProviderVerdict.Ok)); clock.Advance(TimeSpan.FromSeconds(1));
        await cache.SetAsync(c, new TextResponse("c", ProviderVerdict.Ok)); // over cap → oldest (a) trimmed

        Assert.Null(await cache.GetAsync(a));
        Assert.NotNull(await cache.GetAsync(b));
        Assert.NotNull(await cache.GetAsync(c));

        // hygiene: don't leave far-future rows outranking later tests' entries in the shared table
        await cache.RemoveAsync(b);
        await cache.RemoveAsync(c);
    }

    [SkippableFact]
    public async Task Semantic_memory_works_over_the_pgvector_store()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var task = Uid();
        var mem = new SemanticMemory([new FakeVectorProvider()], new PostgresVectorStore(pg.Factory));
        await mem.RememberAsync(task, "s", "cancel my subscription anytime");
        await mem.RememberAsync(task, "s", "our pizza menu today");

        var hits = await mem.RecallAsync(task, "s", "how do I cancel", k: 3, minScore: 0.0001);
        Assert.NotEmpty(hits);
        Assert.Contains("cancel", hits[0].Content);
        Assert.DoesNotContain(hits, h => h.Content.Contains("pizza"));
    }
}
