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
/// survival across a fresh store instance, size eviction — plus the vector contract's wiring.</summary>
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

    // The cross-backend VectorStoreContract, wired here because this class owns the database lifetime. Its
    // fixtures are deliberately NOT unit basis vectors, under which cosine and a raw dot product agree.
    [Fact] public Task Contract_cosine_not_dot() => VectorStoreContract.Ranking_is_by_cosine_so_magnitude_does_not_win(new SqliteVectorStore(_db.Factory), "vc1");
    [Fact] public Task Contract_score_in_range() => VectorStoreContract.A_score_is_a_cosine_in_the_documented_range(new SqliteVectorStore(_db.Factory), "vc2");
    [Fact] public Task Contract_upsert_replaces() => VectorStoreContract.Upserting_the_same_id_replaces_rather_than_duplicating(new SqliteVectorStore(_db.Factory), "vc3");
    [Fact] public Task Contract_bounded_by_k() => VectorStoreContract.Search_returns_at_most_k(new SqliteVectorStore(_db.Factory), "vc4");
    [Fact] public Task Contract_delete_one() => VectorStoreContract.Delete_removes_one_entry_and_leaves_the_others(new SqliteVectorStore(_db.Factory), "vc5");
    [Fact] public Task Contract_remove_collection() => VectorStoreContract.Removing_a_collection_clears_it_and_absent_deletes_are_no_ops(new SqliteVectorStore(_db.Factory), "vc6");
    [Fact] public Task Contract_isolated() => VectorStoreContract.Collections_are_isolated(new SqliteVectorStore(_db.Factory), "vc7");
    [Fact] public Task Contract_tie_by_id() => VectorStoreContract.Equal_scores_are_ordered_by_id(new SqliteVectorStore(_db.Factory), "vc8");
    [Fact] public Task Contract_tie_at_k() => VectorStoreContract.The_k_boundary_keeps_the_same_tied_entries(new SqliteVectorStore(_db.Factory), "vc9");
    [Fact] public Task Contract_tie_loses_to_score() => VectorStoreContract.The_tiebreak_never_outranks_the_score(new SqliteVectorStore(_db.Factory), "vc10");
    [Fact] public Task Contract_other_dimension() => VectorStoreContract.A_vector_of_another_dimension_scores_zero_and_ranks_last(new SqliteVectorStore(_db.Factory), "vc14");
    [Fact] public Task Contract_zero_vector() => VectorStoreContract.A_zero_vector_scores_zero_and_ranks_last(new SqliteVectorStore(_db.Factory), "vc15");
    [Fact] public void Contract_can_list() => VectorStoreContract.Every_shipped_store_can_list_its_collections(new SqliteVectorStore(_db.Factory));
    [Fact] public Task Contract_list_prefix() => VectorStoreContract.Listing_matches_a_prefix_ordinally(new SqliteVectorStore(_db.Factory), "vc11");
    [Fact] public Task Contract_list_literal() => VectorStoreContract.A_listing_prefix_is_never_read_as_a_pattern(new SqliteVectorStore(_db.Factory), "vc12");
    [Fact] public Task Contract_list_empty() => VectorStoreContract.Listing_omits_emptied_collections_and_never_throws(new SqliteVectorStore(_db.Factory), "vc13");

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
