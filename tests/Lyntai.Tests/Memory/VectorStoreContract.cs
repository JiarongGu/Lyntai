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

    // ONE vector for every tied entry: each scores bit-identically, so nothing but the tiebreak can order
    // them. Ids are zero-padded, so ordinal string order == numeric order.
    private static readonly float[] Tied = [1f, 0f, 0f];
    private static string TiedId(int i) => $"id-{i:00}";

    private static async Task SeedTiedAsync(IVectorStore store, string c, int n)
    {
        // inserted in DESCENDING id order, so "whatever arrived first" is not the expected answer either
        for (var i = n; i >= 1; i--) await store.UpsertAsync(c, TiedId(i), Tied, $"payload-{i}");
    }

    /// <summary>Equal scores come back in a total, repeatable order — by id, ascending ordinal.
    /// <para><b>Load-bearing, not tidiness</b>, and it belongs to the CONTRACT rather than to one backend:
    /// every implementation ranks out of a container whose natural order it does not control — a
    /// <c>ConcurrentDictionary</c>'s per-process-randomized hash buckets in process, an unordered scan on
    /// SQLite, a plan that may go parallel on Postgres. The same defect <c>storage.md</c> records for an
    /// <c>ORDER BY</c> on a non-unique column.</para>
    /// <para>Ties are not hypothetical: <c>VectorMath.Cosine</c> returns exactly <c>0</c> for a zero vector
    /// or a dimension mismatch, and identical embeddings score exactly equal.</para></summary>
    public static async Task Equal_scores_are_ordered_by_id(IVectorStore store, string c)
    {
        // 16 tied entries: an untiebroken order coincides with id order about once in 16!, so this pins the
        // tiebreak rather than recording today's luck.
        await SeedTiedAsync(store, c, 16);

        var hits = await store.SearchAsync(c, Tied, k: 16);

        Assert.Equal(Enumerable.Range(1, 16).Select(TiedId), hits.Select(h => h.Id));
        Assert.All(hits, h => Assert.Equal(hits[0].Score, h.Score)); // …and they genuinely were all tied
    }

    /// <summary>The sharp half: with more ties than <c>k</c>, an untiebroken top-k drops an ARBITRARY member
    /// of the tie, so the same query over the same data returns different entries run to run — and on the
    /// memory path those top-k hits become similarity EDGES and a novelty score, so the arbitrariness is
    /// written down permanently rather than merely displayed.</summary>
    public static async Task The_k_boundary_keeps_the_same_tied_entries(IVectorStore store, string c)
    {
        await SeedTiedAsync(store, c, 16);

        var hits = await store.SearchAsync(c, Tied, k: 3);

        Assert.Equal(new[] { TiedId(1), TiedId(2), TiedId(3) }, hits.Select(h => h.Id));
    }

    /// <summary>Guard against "fixing" the tie by sorting on id first: a closer match wins even when its id
    /// sorts last. Only EQUAL scores may be reordered.</summary>
    public static async Task The_tiebreak_never_outranks_the_score(IVectorStore store, string c)
    {
        await store.UpsertAsync(c, "aaa-far", [0f, 1f, 0f], "far");
        await store.UpsertAsync(c, "zzz-near", [1f, 0f, 0f], "near");

        var hits = await store.SearchAsync(c, [1f, 0f, 0f], k: 2);

        Assert.Equal("zzz-near", hits[0].Id);
        Assert.True(hits[0].Score > hits[1].Score);
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

    /// <summary>Every SHIPPED store enumerates its collections. The capability is optional for a BYO store —
    /// that is why it is a separate interface — but a store this library ships that could not list would make
    /// <c>ISemanticMemory</c>'s cross-scope recall silently empty on that backend alone, which is exactly the
    /// per-backend divergence a contract exists to stop.</summary>
    public static void Every_shipped_store_can_list_its_collections(IVectorStore store) =>
        Assert.IsAssignableFrom<IListableVectorStore>(store);

    /// <summary>Listing matches a prefix ORDINALLY and returns only the collections that match — the
    /// operation semantic memory partitions a task's scopes with.
    /// <para>The fixtures are chosen so a case-INSENSITIVE match fails: SQLite's <c>LIKE</c> is ASCII
    /// case-insensitive by default, so a prefix implemented through it would reach <c>{c}-OTHER</c> and hand a
    /// caller another task's scopes.</para></summary>
    public static async Task Listing_matches_a_prefix_ordinally(IVectorStore store, string c)
    {
        await store.UpsertAsync($"{c}-mine-a", "1", [1f, 0f, 0f], "A");
        await store.UpsertAsync($"{c}-mine-b", "1", [1f, 0f, 0f], "B");
        await store.UpsertAsync($"{c}-MINE-c", "1", [1f, 0f, 0f], "C");   // differs only in case
        await store.UpsertAsync($"{c}-other", "1", [1f, 0f, 0f], "D");

        var listed = await ((IListableVectorStore)store).ListCollectionsAsync($"{c}-mine-");

        Assert.Equal(new[] { $"{c}-mine-a", $"{c}-mine-b" },
            listed.OrderBy(x => x, StringComparer.Ordinal));
    }

    /// <summary>A prefix is DATA, not a pattern: <c>%</c> and <c>_</c> are LIKE wildcards on both SQL
    /// backends, so a store that reached for LIKE would match collections a caller never asked for.</summary>
    public static async Task A_listing_prefix_is_never_read_as_a_pattern(IVectorStore store, string c)
    {
        await store.UpsertAsync($"{c}-a%b-one", "1", [1f, 0f, 0f], "LITERAL");
        await store.UpsertAsync($"{c}-axxb-two", "1", [1f, 0f, 0f], "WILDCARD-WOULD-MATCH");

        var listed = await ((IListableVectorStore)store).ListCollectionsAsync($"{c}-a%b-");

        Assert.Equal(new[] { $"{c}-a%b-one" }, listed);
    }

    /// <summary>A collection whose last entry was deleted is not listed, and a prefix nothing matches yields
    /// an empty list rather than a throw — so "this task has no scopes yet" is an ordinary answer.
    /// <para>Deleting the last ENTRY rather than the collection is the discriminating case: a row-based
    /// backend has nothing left to find, while the in-process store keeps the emptied bucket, and listing it
    /// would tell a caller a scope exists that holds nothing.</para></summary>
    public static async Task Listing_omits_emptied_collections_and_never_throws(IVectorStore store, string c)
    {
        var lister = (IListableVectorStore)store;
        await store.UpsertAsync($"{c}-gone", "1", [1f, 0f, 0f], "X");
        Assert.Single(await lister.ListCollectionsAsync($"{c}-gone"));

        await store.DeleteAsync($"{c}-gone", "1");

        Assert.Empty(await lister.ListCollectionsAsync($"{c}-gone"));
        Assert.Empty(await lister.ListCollectionsAsync($"{c}-never-written"));
    }
}
