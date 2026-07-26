using Lyntai;
using Lyntai.Llm;
using Lyntai.Llm.Budgeting;
using Lyntai.Memory;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Fakes;

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
        var got = await new SqliteResponseCache(_db.Factory, options).GetAsync("k");
        Assert.NotNull(got);
        Assert.Equal("cached answer", got!.Text);
        Assert.Equal(LlmVerdict.Ok, got.Verdict);
        Assert.Equal(0.02, got.Usage!.CostUsd);
        Assert.Null(await new SqliteResponseCache(_db.Factory, options).GetAsync("missing"));
    }

    [Fact]
    public async Task ResponseCache_expires_by_ttl()
    {
        var clock = new MutableClock();
        var cache = new SqliteResponseCache(_db.Factory, new LyntaiOptions(), clock.Get);
        await cache.SetAsync("k", new LlmReply("x", LlmVerdict.Ok), TimeSpan.FromMinutes(5));
        clock.Advance(TimeSpan.FromMinutes(4));
        Assert.NotNull(await cache.GetAsync("k")); // still fresh
        clock.Advance(TimeSpan.FromMinutes(2));       // past 5m
        Assert.Null(await cache.GetAsync("k"));
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

        Assert.Null(await cache.GetAsync("a"));
        Assert.NotNull(await cache.GetAsync("b"));
        Assert.NotNull(await cache.GetAsync("c"));
    }

    [Fact]
    public async Task ResponseCache_remove_evicts_one_entry_and_a_missing_key_is_a_no_op()
    {
        var options = new LyntaiOptions();
        var cache = new SqliteResponseCache(_db.Factory, options);
        await cache.SetAsync("keep", new LlmReply("keep", LlmVerdict.Ok));
        await cache.SetAsync("poisoned", new LlmReply("bad", LlmVerdict.Ok));

        await cache.RemoveAsync("poisoned");
        await cache.RemoveAsync("never-set"); // no-op, no throw

        Assert.Null(await new SqliteResponseCache(_db.Factory, options).GetAsync("poisoned"));
        Assert.NotNull(await new SqliteResponseCache(_db.Factory, options).GetAsync("keep"));
    }

    // ---- usage tracker -------------------------------------------------------------------------------

    [Fact]
    public async Task UsageTracker_accumulates_per_consumer_and_globally_persisted()
    {
        await new SqliteUsageTracker(_db.Factory).RecordAsync("a", new LlmUsage(10, 5, CostUsd: 0.10));
        await new SqliteUsageTracker(_db.Factory).RecordAsync("a", new LlmUsage(20, 5, CostUsd: 0.20));
        await new SqliteUsageTracker(_db.Factory).RecordAsync("b", new LlmUsage(1, 1, CostUsd: 0.01));

        var tracker = new SqliteUsageTracker(_db.Factory); // fresh instance reads persisted totals
        var a = (await tracker.TotalAsync("a"));
        Assert.Equal(30, a.InputTokens);
        Assert.Equal(0.30, a.CostUsd, 5);
        Assert.Equal(2, a.Calls);
        Assert.Equal(0.31, (await tracker.TotalAsync()).CostUsd, 5);   // global SUM across rows
        Assert.Equal(UsageTotals.Empty, (await tracker.TotalAsync("never-seen")));
    }

    [Fact] // R6: consumer identity is case-INSENSITIVE everywhere — totals AGGREGATE across casings, so
    // the budget cap (whose PerConsumer map is OrdinalIgnoreCase, like every options map) can't be
    // overspent 2x by tagging "App" in one code path and "app" in another. (Supersedes the earlier
    // case-sensitive pin, which matched the SQL PK but let each casing accrue its own uncapped total.)
    public async Task UsageTracker_consumer_totals_aggregate_across_casings_on_every_backend()
    {
        IUsageTracker[] trackers = [new InMemoryUsageTracker(), new SqliteUsageTracker(_db.Factory)];
        foreach (var t in trackers)
        {
            await t.RecordAsync("App", new LlmUsage(10, 0, CostUsd: 0.10));
            await t.RecordAsync("app", new LlmUsage(20, 0, CostUsd: 0.20));
            Assert.Equal(2, (await t.TotalAsync("App")).Calls);           // ONE consumer identity, either casing
            Assert.Equal(2, (await t.TotalAsync("app")).Calls);
            Assert.Equal(30, (await t.TotalAsync("APP")).InputTokens);
            Assert.Equal(0.30, (await t.TotalAsync("app")).CostUsd, 5);
        }
    }

    [Fact]
    public async Task UsageTracker_reset_clears_a_consumer_or_all()
    {
        var t = new SqliteUsageTracker(_db.Factory);
        await t.RecordAsync("a", new LlmUsage(10, 0, CostUsd: 0.10));
        await t.RecordAsync("b", new LlmUsage(20, 0, CostUsd: 0.20));

        await t.ResetAsync("a");
        Assert.Equal(UsageTotals.Empty, (await t.TotalAsync("a")));
        Assert.Equal(0.20, (await t.TotalAsync()).CostUsd, 5);         // b remains

        await t.ResetAsync();
        Assert.Equal(UsageTotals.Empty, (await t.TotalAsync()));
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
