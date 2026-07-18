using Lyntai;
using Lyntai.Llm;
using Lyntai.Llm.Budgeting;
using Lyntai.Memory;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Jobs;

namespace Lyntai.Tests.Storage;

/// <summary>The persistent SQLite backends for the front-door governance + semantic-memory seams
/// (response cache, usage tracker, vector store) against a real migrated temp db — round-trip, TTL/size
/// eviction, accounting, cosine ranking, and (the point) survival across a fresh store instance.</summary>
public class SqliteGovernanceStoreTests : IDisposable
{
    private readonly TempDb _db = new();
    public void Dispose() => _db.Dispose();

    // ---- response cache ------------------------------------------------------------------------------

    [Fact]
    public async Task ResponseCache_persists_a_reply_across_store_instances()
    {
        var options = new LyntaiOptions();
        var reply = new LlmReply("cached answer", LlmVerdict.Ok, new LlmUsage(10, 5, CostUsd: 0.02));
        await new SqliteResponseCache(_db.Factory, options).SetAsync("k", reply);

        // a FRESH store over the same db reads it back — proves it's on disk, not in the store instance
        var got = await new SqliteResponseCache(_db.Factory, options).TryGetAsync("k");
        Assert.NotNull(got);
        Assert.Equal("cached answer", got!.Text);
        Assert.Equal(LlmVerdict.Ok, got.Verdict);
        Assert.Equal(0.02, got.Usage!.CostUsd);
        Assert.Null(await new SqliteResponseCache(_db.Factory, options).TryGetAsync("missing"));
    }

    [Fact]
    public async Task ResponseCache_expires_by_ttl()
    {
        var clock = new MutableClock();
        var cache = new SqliteResponseCache(_db.Factory, new LyntaiOptions(), clock.Get);
        await cache.SetAsync("k", new LlmReply("x", LlmVerdict.Ok), TimeSpan.FromMinutes(5));
        clock.Advance(TimeSpan.FromMinutes(4));
        Assert.NotNull(await cache.TryGetAsync("k")); // still fresh
        clock.Advance(TimeSpan.FromMinutes(2));       // past 5m
        Assert.Null(await cache.TryGetAsync("k"));
    }

    [Fact]
    public async Task ResponseCache_evicts_the_oldest_beyond_max_entries()
    {
        var options = new LyntaiOptions();
        options.Cache.MaxEntries = 2;
        var clock = new MutableClock();
        var cache = new SqliteResponseCache(_db.Factory, options, clock.Get);
        await cache.SetAsync("a", new LlmReply("a", LlmVerdict.Ok)); clock.Advance(TimeSpan.FromSeconds(1));
        await cache.SetAsync("b", new LlmReply("b", LlmVerdict.Ok)); clock.Advance(TimeSpan.FromSeconds(1));
        await cache.SetAsync("c", new LlmReply("c", LlmVerdict.Ok)); // over cap → oldest ("a") trimmed

        Assert.Null(await cache.TryGetAsync("a"));
        Assert.NotNull(await cache.TryGetAsync("b"));
        Assert.NotNull(await cache.TryGetAsync("c"));
    }

    // ---- usage tracker -------------------------------------------------------------------------------

    [Fact]
    public void UsageTracker_accumulates_per_consumer_and_globally_persisted()
    {
        new SqliteUsageTracker(_db.Factory).Record("a", new LlmUsage(10, 5, CostUsd: 0.10));
        new SqliteUsageTracker(_db.Factory).Record("a", new LlmUsage(20, 5, CostUsd: 0.20));
        new SqliteUsageTracker(_db.Factory).Record("b", new LlmUsage(1, 1, CostUsd: 0.01));

        var tracker = new SqliteUsageTracker(_db.Factory); // fresh instance reads persisted totals
        var a = tracker.Total("a");
        Assert.Equal(30, a.InputTokens);
        Assert.Equal(0.30, a.CostUsd, 5);
        Assert.Equal(2, a.Calls);
        Assert.Equal(0.31, tracker.Total().CostUsd, 5);   // global SUM across rows
        Assert.Equal(UsageTotals.Empty, tracker.Total("never-seen"));
    }

    [Fact]
    public void UsageTracker_reset_clears_a_consumer_or_all()
    {
        var t = new SqliteUsageTracker(_db.Factory);
        t.Record("a", new LlmUsage(10, 0, CostUsd: 0.10));
        t.Record("b", new LlmUsage(20, 0, CostUsd: 0.20));

        t.Reset("a");
        Assert.Equal(UsageTotals.Empty, t.Total("a"));
        Assert.Equal(0.20, t.Total().CostUsd, 5);         // b remains

        t.Reset();
        Assert.Equal(UsageTotals.Empty, t.Total());
    }

    // ---- vector store --------------------------------------------------------------------------------

    [Fact]
    public async Task VectorStore_ranks_by_cosine_and_persists()
    {
        await new SqliteVectorStore(_db.Factory).UpsertAsync("c", "a", [1f, 0f, 0f], "A");
        await new SqliteVectorStore(_db.Factory).UpsertAsync("c", "b", [0f, 1f, 0f], "B");

        var hits = await new SqliteVectorStore(_db.Factory).SearchAsync("c", [0.9f, 0.1f, 0f], k: 2);
        Assert.Equal("A", hits[0].Payload);
        Assert.True(hits[0].Score > hits[1].Score);
    }

    [Fact]
    public async Task VectorStore_upsert_dedups_and_remove_collection_clears()
    {
        var store = new SqliteVectorStore(_db.Factory);
        await store.UpsertAsync("c", "same", [1f, 0f], "first");
        await store.UpsertAsync("c", "same", [1f, 0f], "second"); // same id → overwrite

        var hits = await store.SearchAsync("c", [1f, 0f], k: 5);
        Assert.Single(hits);
        Assert.Equal("second", hits[0].Payload);

        await store.RemoveCollectionAsync("c");
        Assert.Empty(await store.SearchAsync("c", [1f, 0f], k: 5));
    }

    [Fact]
    public async Task Semantic_memory_works_over_the_sqlite_vector_store()
    {
        // the whole point of the seam: SemanticMemory is unchanged, just its vector backend is SQLite
        var mem = new SemanticMemory(new FakeEmbedder(), new SqliteVectorStore(_db.Factory));
        await mem.RememberAsync("t", "s", "cancel my subscription anytime");
        await mem.RememberAsync("t", "s", "our pizza menu today");

        var hits = await mem.RecallAsync("t", "s", "how do I cancel", k: 3, minScore: 0.0001);
        Assert.NotEmpty(hits);
        Assert.Contains("cancel", hits[0].Content);
        Assert.DoesNotContain(hits, h => h.Content.Contains("pizza"));
    }
}
