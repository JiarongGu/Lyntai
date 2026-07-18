using Lyntai;
using Lyntai.Llm;
using Lyntai.Llm.Budgeting;
using Lyntai.Memory;
using Lyntai.Storage.Postgres;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Jobs;
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

    [Fact]
    public async Task ResponseCache_persists_across_instances_and_expires()
    {
        if (!pg.Available) return;
        var options = new LyntaiOptions();
        var clock = new MutableClock();
        var key = Uid();
        var cache = new PostgresResponseCache(pg.Factory, options, clock.Get);
        await cache.SetAsync(key, new LlmReply("pg cached", LlmVerdict.Ok, new LlmUsage(3, 4, CostUsd: 0.05)), TimeSpan.FromMinutes(5));

        var got = await new PostgresResponseCache(pg.Factory, options, clock.Get).TryGetAsync(key); // fresh instance
        Assert.NotNull(got);
        Assert.Equal("pg cached", got!.Text);
        Assert.Equal(0.05, got.Usage!.CostUsd);

        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Null(await cache.TryGetAsync(key)); // expired
    }

    [Fact]
    public async Task UsageTracker_accumulates_per_consumer_and_resets()
    {
        if (!pg.Available) return;
        var a = Uid();
        new PostgresUsageTracker(pg.Factory).Record(a, new LlmUsage(10, 5, CostUsd: 0.10));
        new PostgresUsageTracker(pg.Factory).Record(a, new LlmUsage(20, 5, CostUsd: 0.20));

        var ta = new PostgresUsageTracker(pg.Factory).Total(a); // fresh instance reads persisted totals
        Assert.Equal(30, ta.InputTokens);
        Assert.Equal(0.30, ta.CostUsd, 5);
        Assert.Equal(2, ta.Calls);

        new PostgresUsageTracker(pg.Factory).Reset(a);
        Assert.Equal(UsageTotals.Empty, new PostgresUsageTracker(pg.Factory).Total(a));
    }

    [Fact]
    public async Task VectorStore_pgvector_ranks_by_cosine_dedups_and_removes()
    {
        if (!pg.Available) return;
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

    [Fact]
    public async Task Semantic_memory_works_over_the_pgvector_store()
    {
        if (!pg.Available) return;
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
