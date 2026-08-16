using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

/// <summary>Backend-agnostic facts every <see cref="IVectorStore"/> satisfies, held to by all three
/// implementations the way <c>MemoryGraphStoreContract</c> holds the three graph stores to one contract.
///
/// <para><b>Why this file exists.</b> `IVectorStore` had THREE implementations (in-process, SQLite, Postgres)
/// and NO cross-backend contract — each was exercised only by a couple of per-backend tests. Worse, every
/// vector fixture in the repository was a UNIT BASIS VECTOR (<c>[1,0,0]</c>, <c>[0,1,0]</c>), and with unit
/// vectors the query norm is a positive constant common to every candidate, so cosine similarity and a raw,
/// unnormalised dot product induce the SAME ordering and the same sign. A test named
/// <c>VectorStore_ranks_by_cosine</c> therefore could not tell the two apart: deleting the normalisation from
/// the in-process store, or swapping pgvector's <c>&lt;=&gt;</c> for <c>&lt;#&gt;</c>, passed. Found
/// 2026-08-14 by the whole-codebase review.</para>
///
/// <para>That matters beyond tidiness. <c>IVectorStore</c> takes whatever an <c>IEmbedder</c> produces and
/// not every embedding model returns normalised vectors, while <c>ISemanticMemory</c>'s <c>minScore</c>
/// treats the result as a cosine in <c>[-1, 1]</c>. An unbounded dot product makes that threshold mean
/// nothing — and mean something DIFFERENT on each backend.</para>
///
/// <para>Every method is namespaced by a caller-supplied collection so backends sharing a container stay
/// isolated.</para></summary>
public static class VectorStoreContract
{
    /// <summary><b>The discriminating fact: cosine, not dot product.</b> A long vector that is merely
    /// well-aligned must NOT outrank a short vector that is perfectly aligned.
    /// <para>Query <c>[1,0,0]</c>. Candidate "near" is <c>[1,0,0]</c> — cosine 1.0, dot 1. Candidate "long"
    /// is <c>[10,1,0]</c> — cosine ≈0.995, dot 10. Under cosine "near" wins; under an unnormalised dot
    /// product "long" wins by 10×. The magnitudes are chosen so the two rules disagree about the WINNER,
    /// not merely about the scores, because an ordering assertion is what a caller actually depends on.</para>
    /// </summary>
    public static async Task Ranking_is_by_cosine_so_magnitude_does_not_win(IVectorStore store, string c)
    {
        await store.UpsertAsync(c, "near", [1f, 0f, 0f], "NEAR");
        await store.UpsertAsync(c, "long", [10f, 1f, 0f], "LONG");

        var hits = await store.SearchAsync(c, [1f, 0f, 0f], k: 2);

        Assert.Equal(2, hits.Count);
        Assert.Equal("NEAR", hits[0].Payload);
        Assert.True(hits[0].Score > hits[1].Score,
            $"cosine must rank the aligned unit vector first (near={hits[0].Score}, long={hits[1].Score})");
    }

    /// <summary>A cosine score is bounded by <c>[-1, 1]</c>, which is what <c>ISemanticMemory</c>'s
    /// <c>minScore</c> is expressed against. An unnormalised dot product over the vectors below exceeds 1,
    /// so this catches the same defect from the SCORE side rather than the ordering side — worth having
    /// separately, because a backend could normalise the ordering and still report a raw score.</summary>
    public static async Task A_score_is_a_cosine_in_the_documented_range(IVectorStore store, string c)
    {
        await store.UpsertAsync(c, "big", [10f, 10f, 10f], "BIG");
        await store.UpsertAsync(c, "opposed", [-1f, 0f, 0f], "OPPOSED");

        var hits = await store.SearchAsync(c, [5f, 5f, 5f], k: 2);

        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.InRange(h.Score, -1.0001, 1.0001));
        // a parallel vector of a very different LENGTH is still cosine-identical
        Assert.Equal(1.0, hits[0].Score, precision: 3);
    }

    /// <summary>Re-upserting an id overwrites — the documented dedup mechanism — rather than minting a
    /// second row that would let one entry occupy two of k slots.</summary>
    public static async Task Upserting_the_same_id_replaces_rather_than_duplicating(IVectorStore store, string c)
    {
        await store.UpsertAsync(c, "x", [1f, 0f, 0f], "FIRST");
        await store.UpsertAsync(c, "x", [1f, 0f, 0f], "SECOND");

        var hits = await store.SearchAsync(c, [1f, 0f, 0f], k: 10);

        Assert.Single(hits);
        Assert.Equal("SECOND", hits[0].Payload);
    }

    /// <summary><c>k</c> bounds the result set on every backend.</summary>
    public static async Task Search_returns_at_most_k(IVectorStore store, string c)
    {
        for (var i = 0; i < 5; i++)
            await store.UpsertAsync(c, $"id{i}", [1f, i * 0.01f, 0f], $"P{i}");

        Assert.True((await store.SearchAsync(c, [1f, 0f, 0f], k: 2)).Count <= 2);
    }

    /// <summary>Deleting one id removes exactly that entry and leaves the rest of the collection.</summary>
    public static async Task Delete_removes_one_entry_and_leaves_the_others(IVectorStore store, string c)
    {
        await store.UpsertAsync(c, "keep", [1f, 0f, 0f], "KEEP");
        await store.UpsertAsync(c, "drop", [0f, 1f, 0f], "DROP");

        await store.DeleteAsync(c, "drop");

        var hits = await store.SearchAsync(c, [1f, 1f, 0f], k: 10);
        Assert.Single(hits);
        Assert.Equal("KEEP", hits[0].Payload);
    }

    /// <summary>Dropping a collection clears it, and a delete of an absent id is a documented no-op rather
    /// than a throw — the shape <c>ISemanticMemory.ForgetAsync</c> is built on.</summary>
    public static async Task Removing_a_collection_clears_it_and_absent_deletes_are_no_ops(
        IVectorStore store, string c)
    {
        await store.UpsertAsync(c, "a", [1f, 0f, 0f], "A");
        await store.DeleteAsync(c, "never-existed");   // must not throw

        await store.RemoveCollectionAsync(c);

        Assert.Empty(await store.SearchAsync(c, [1f, 0f, 0f], k: 10));
    }

    /// <summary>Collections are isolated: a search never reaches another collection's vectors. Semantic
    /// memory uses one collection per task+scope, so a leak here is a cross-task memory leak.</summary>
    public static async Task Collections_are_isolated(IVectorStore store, string c)
    {
        await store.UpsertAsync(c, "mine", [1f, 0f, 0f], "MINE");
        await store.UpsertAsync($"{c}-other", "theirs", [1f, 0f, 0f], "THEIRS");

        var hits = await store.SearchAsync(c, [1f, 0f, 0f], k: 10);

        Assert.Single(hits);
        Assert.Equal("MINE", hits[0].Payload);
    }
}
