using Lyntai;
using Lyntai.Llm;
using Lyntai.Llm.Budgeting;
using Lyntai.Memory;
using Lyntai.Storage.Postgres;
using Lyntai.Tests.Fakes;
using Xunit;

namespace Lyntai.Tests.Storage;

/// <summary>The persistent Postgres backends for the governance + semantic-memory seams against the real
/// container (Testcontainers, pgvector image). Skips when Docker is unavailable. Scopes to unique
/// keys/consumers/collections so they share the one migrated database (hence per-consumer totals only, no
/// global-SUM assertions).</summary>
[Collection("postgres")]
public sealed class PostgresGovernanceStoreTests(PostgresFixture pg)
{
    private static string Uid() => Guid.NewGuid().ToString("N");

    [SkippableFact]
    public async Task ResponseCache_persists_across_instances_and_expires()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var options = new LyntaiOptions();
        var clock = new MutableClock();
        var key = Uid();
        var cache = new PostgresResponseCache(pg.Factory, options, clock.Get);
        await cache.SetAsync(key, new LlmReply("pg cached", LlmVerdict.Ok, new LlmUsage(3, 4, CostUsd: 0.05)), TimeSpan.FromMinutes(5));

        var got = await new PostgresResponseCache(pg.Factory, options, clock.Get).GetAsync(key); // fresh instance
        Assert.NotNull(got);
        Assert.Equal("pg cached", got!.Text);
        Assert.Equal(0.05, got.Usage!.CostUsd);

        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Null(await cache.GetAsync(key)); // expired
    }

    [SkippableFact]
    public async Task ResponseCache_remove_evicts_one_entry_and_a_missing_key_is_a_no_op()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var options = new LyntaiOptions();
        var cache = new PostgresResponseCache(pg.Factory, options);
        var keep = Uid();
        var poisoned = Uid();
        await cache.SetAsync(keep, new LlmReply("keep", LlmVerdict.Ok));
        await cache.SetAsync(poisoned, new LlmReply("bad", LlmVerdict.Ok));

        await cache.RemoveAsync(poisoned);
        await cache.RemoveAsync(Uid()); // no-op, no throw

        Assert.Null(await new PostgresResponseCache(pg.Factory, options).GetAsync(poisoned));
        Assert.NotNull(await new PostgresResponseCache(pg.Factory, options).GetAsync(keep));
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

        await cache.SetAsync(a, new LlmReply("a", LlmVerdict.Ok)); clock.Advance(TimeSpan.FromSeconds(1));
        await cache.SetAsync(b, new LlmReply("b", LlmVerdict.Ok)); clock.Advance(TimeSpan.FromSeconds(1));
        await cache.SetAsync(c, new LlmReply("c", LlmVerdict.Ok)); // over cap → oldest (a) trimmed

        Assert.Null(await cache.GetAsync(a));
        Assert.NotNull(await cache.GetAsync(b));
        Assert.NotNull(await cache.GetAsync(c));

        // hygiene: don't leave far-future rows outranking later tests' entries in the shared table
        await cache.RemoveAsync(b);
        await cache.RemoveAsync(c);
    }

    [SkippableFact] // R6's Postgres leg: totals AGGREGATE across consumer casings (lower(consumer) SUM)
    public async Task UsageTracker_consumer_totals_aggregate_across_casings()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var t = new PostgresUsageTracker(pg.Factory);
        var seed = Uid();
        await t.RecordAsync("App-" + seed, new LlmUsage(10, 0, CostUsd: 0.10));
        await t.RecordAsync("app-" + seed, new LlmUsage(20, 0, CostUsd: 0.20));

        Assert.Equal(2, (await t.TotalAsync("APP-" + seed)).Calls);       // ONE consumer identity, any casing
        Assert.Equal(30, (await t.TotalAsync("app-" + seed)).InputTokens);
        Assert.Equal(0.30, (await t.TotalAsync("App-" + seed)).CostUsd, 5);
    }

    [SkippableFact]
    public async Task UsageTracker_accumulates_per_consumer_and_resets()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var a = Uid();
        await new PostgresUsageTracker(pg.Factory).RecordAsync(a, new LlmUsage(10, 5, CostUsd: 0.10));
        await new PostgresUsageTracker(pg.Factory).RecordAsync(a, new LlmUsage(20, 5, CostUsd: 0.20));

        var ta = (await new PostgresUsageTracker(pg.Factory).TotalAsync(a)); // fresh instance reads persisted totals
        Assert.Equal(30, ta.InputTokens);
        Assert.Equal(0.30, ta.CostUsd, 5);
        Assert.Equal(2, ta.Calls);

        await new PostgresUsageTracker(pg.Factory).ResetAsync(a);
        Assert.Equal(UsageTotals.Empty, (await new PostgresUsageTracker(pg.Factory).TotalAsync(a)));
    }

    [SkippableFact]
    public async Task VectorStore_pgvector_ranks_by_cosine_dedups_and_removes()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var c = Uid();
        var store = new PostgresVectorStore(pg.Factory);
        await store.UpsertAsync(c, "a", [1f, 0f, 0f], "A");
        await store.UpsertAsync(c, "b", [0f, 1f, 0f], "B");
        await store.UpsertAsync(c, "a", [1f, 0f, 0f], "A2"); // same id → dedup (payload updated)

        var hits = await store.SearchAsync(c, [0.9f, 0.1f, 0f], k: 5);
        Assert.Equal(2, hits.Count);            // a (deduped) + b
        Assert.Equal("A2", hits[0].Payload);    // nearest to the query, latest payload
        Assert.True(hits[0].Score > hits[1].Score);

        await store.RemoveCollectionAsync(c);
        Assert.Empty(await store.SearchAsync(c, [1f, 0f, 0f], k: 5));
    }

    [SkippableFact]
    public async Task Semantic_memory_works_over_the_pgvector_store()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var task = Uid();
        var mem = new SemanticMemory(new FakeEmbedder(), new PostgresVectorStore(pg.Factory));
        await mem.RememberAsync(task, "s", "cancel my subscription anytime");
        await mem.RememberAsync(task, "s", "our pizza menu today");

        var hits = await mem.RecallAsync(task, "s", "how do I cancel", k: 3, minScore: 0.0001);
        Assert.NotEmpty(hits);
        Assert.Contains("cancel", hits[0].Content);
        Assert.DoesNotContain(hits, h => h.Content.Contains("pizza"));
    }
}
