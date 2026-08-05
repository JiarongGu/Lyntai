using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

/// <summary>
/// Part 43 VECTOR-TIEBREAK. <see cref="InMemoryVectorStore"/> ranks out of a <c>ConcurrentDictionary</c>,
/// whose enumeration order is BUCKET order over per-process-randomized string hashes — and LINQ's
/// <c>OrderByDescending</c> is a STABLE sort, so before the id tiebreak it faithfully preserved an order
/// that differs between runs. Equal scores swapped places, and at the <c>k</c> boundary an arbitrary member
/// of the tie silently dropped out. Same defect <c>storage.md</c> records for an <c>ORDER BY</c> on a
/// non-unique column, in the in-memory backend.
/// </summary>
public class InMemoryVectorStoreTiebreakTests
{
    // ONE vector for every entry: each scores bit-identically (same inputs to VectorMath.Cosine), so nothing
    // but the tiebreak can order them.
    private static readonly float[] Tied = [1f, 0f, 0f];

    private static string Id(int i) => $"id-{i:00}"; // zero-padded, so ordinal string order == numeric order

    private static async Task<InMemoryVectorStore> TiedStoreAsync(int n)
    {
        var store = new InMemoryVectorStore();
        // added in DESCENDING id order, so "whatever was inserted first" is not the expected answer either
        for (var i = n; i >= 1; i--)
            await store.UpsertAsync("c", Id(i), Tied, $"payload-{i}");
        return store;
    }

    [Fact]
    public async Task Equal_scores_come_back_ordered_by_id()
    {
        // 16 tied entries: an untiebroken hash-bucket order coincides with id order about once in 16!, so
        // this pins the tiebreak rather than recording today's luck.
        var store = await TiedStoreAsync(16);

        var hits = await store.SearchAsync("c", Tied, k: 16);

        Assert.Equal(Enumerable.Range(1, 16).Select(i => Id(i)), hits.Select(h => h.Id));
        Assert.All(hits, h => Assert.Equal(hits[0].Score, h.Score)); // …and they genuinely were all tied
    }

    [Fact]
    public async Task The_k_boundary_keeps_the_same_tied_entries_every_call()
    {
        // The sharp half: with more ties than k, an untiebroken top-k drops an ARBITRARY member of the tie,
        // so the same query over the same data returns different payloads run to run.
        var store = await TiedStoreAsync(16);

        var hits = await store.SearchAsync("c", Tied, k: 3);

        Assert.Equal(new[] { Id(1), Id(2), Id(3) }, hits.Select(h => h.Id));
    }

    [Fact]
    public async Task The_tiebreak_never_outranks_the_score()
    {
        // Guard against "fixing" the tie by sorting on id first: a closer match wins even when its id sorts
        // last. Only EQUAL scores may be reordered.
        var store = new InMemoryVectorStore();
        await store.UpsertAsync("c", "aaa-far", [0f, 1f, 0f], "far");
        await store.UpsertAsync("c", "zzz-near", [1f, 0f, 0f], "near");

        var hits = await store.SearchAsync("c", [1f, 0f, 0f], k: 2);

        Assert.Equal("zzz-near", hits[0].Id);
        Assert.True(hits[0].Score > hits[1].Score);
    }
}
