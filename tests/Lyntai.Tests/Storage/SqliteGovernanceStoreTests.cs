using Lyntai.Inference;
using Lyntai;
using Lyntai.Inference.Budgeting;
using Lyntai.Memory;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Memory;

namespace Lyntai.Tests.Storage;

/// <summary>The persistent SQLite backends for the front-door governance + semantic-memory seams (response
/// cache, usage tracker, vector store) against a real migrated temp db: what the contracts do not pin —
/// survival across a fresh store instance, size eviction. The vector contract is
/// <see cref="SqliteVectorStoreContractTests"/>.</summary>
public class SqliteGovernanceStoreTests : IDisposable
{
    private readonly TempDb _db = new();
    public void Dispose() => _db.Dispose();

    // ---- response cache ------------------------------------------------------------------------------

    [Fact]
    public async Task ResponseCache_persists_a_reply_across_store_instances()
    {
        var options = new LyntaiOptions();
        var reply = new TextResponse("cached answer", ProviderVerdict.Ok, new TextUsage(10, 5, CostUsd: 0.02));
        await new SqliteResponseCache(_db.Factory, options).SetAsync("k", reply);

        // a FRESH store over the same db reads it back — proves it's on disk, not in the store instance
        var got = await new SqliteResponseCache(_db.Factory, options).GetAsync("k");
        Assert.NotNull(got);
        Assert.Equal("cached answer", got!.Text);
        Assert.Equal(ProviderVerdict.Ok, got.Verdict);
        Assert.Equal(0.02, got.Usage!.CostUsd);
        Assert.Null(await new SqliteResponseCache(_db.Factory, options).GetAsync("missing"));
    }

    [Fact]
    public async Task ResponseCache_evicts_the_oldest_beyond_max_entries()
    {
        var options = new LyntaiOptions();
        options.Cache.MaxEntries = 2;
        var clock = new MutableClock();
        var cache = new SqliteResponseCache(_db.Factory, options, clock.Get);
        await cache.SetAsync("a", new TextResponse("a", ProviderVerdict.Ok)); clock.Advance(TimeSpan.FromSeconds(1));
        await cache.SetAsync("b", new TextResponse("b", ProviderVerdict.Ok)); clock.Advance(TimeSpan.FromSeconds(1));
        await cache.SetAsync("c", new TextResponse("c", ProviderVerdict.Ok)); // over cap → oldest ("a") trimmed

        Assert.Null(await cache.GetAsync("a"));
        Assert.NotNull(await cache.GetAsync("b"));
        Assert.NotNull(await cache.GetAsync("c"));
    }

    // ---- usage tracker -------------------------------------------------------------------------------

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
    public async Task Semantic_memory_works_over_the_sqlite_vector_store()
    {
        // the whole point of the seam: SemanticMemory is unchanged, just its vector backend is SQLite
        var mem = new SemanticMemory([new FakeVectorProvider()], new SqliteVectorStore(_db.Factory));
        await mem.RememberAsync("t", "s", "cancel my subscription anytime");
        await mem.RememberAsync("t", "s", "our pizza menu today");

        var hits = await mem.RecallAsync("t", "s", "how do I cancel", k: 3, minScore: 0.0001);
        Assert.NotEmpty(hits);
        Assert.Contains("cancel", hits[0].Content);
        Assert.DoesNotContain(hits, h => h.Content.Contains("pizza"));
    }
}

/// <summary>The <see cref="VectorStoreContract"/> against SQLite over a per-test temp db. Its fixtures are
/// deliberately NOT unit basis vectors, under which cosine and a raw dot product agree.</summary>
public class SqliteVectorStoreContractTests : VectorStoreContractFacts, IDisposable
{
    private readonly TempDb _db = new();
    protected override IVectorStore New() => new SqliteVectorStore(_db.Factory);
    public void Dispose() => _db.Dispose();
}
