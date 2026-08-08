using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

/// <summary>Backend-agnostic <see cref="IMemoryGraphStore"/> facts. MEM2b runs these against SQLite and
/// Postgres unchanged; per <c>storage.md</c> the contract IS the deduplication mechanism for the relational
/// pair, not a shared base class.
/// <para>DELIBERATELY OMITTED, because the backends diverge by design exactly as
/// <c>IMemoryStore.RecallAsync</c> already documents: same-match ORDERING (SQLite ranks by bm25, the others
/// by recency) and MULTI-TOKEN matching. The portable guarantee asserted here is the single-token
/// one.</para></summary>
public static class MemoryGraphStoreContract
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static GraphNodeWrite Write(string engine, string key, string content,
        MemoryGrade grade = MemoryGrade.Associative) =>
        new(engine, key, "s", content, content, grade, 7, null);

    public static async Task Upsert_then_seed_by_single_token_substring(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "the deploy pipeline requires manual approval"), T0);
        await store.UpsertAsync(Write("e", key, "rollbacks must page the on-call"), T0);

        var hits = await store.SeedAsync("e", key, "s", "pipeline", null, 10, T0);

        Assert.Single(hits);
        Assert.Contains("manual approval", hits[0].Content, StringComparison.Ordinal);
    }

    public static async Task Upserting_identical_content_refreshes_rather_than_duplicating(
        IMemoryGraphStore store, string key)
    {
        var first = await store.UpsertAsync(Write("e", key, "one fact"), T0);
        var second = await store.UpsertAsync(Write("e", key, "one fact"), T0);

        Assert.Equal(first, second);
        Assert.Single(await store.SeedAsync("e", key, "s", null, null, 10, T0));
    }

    public static async Task Engines_are_isolated_from_one_another(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("engine-a", key, "belongs to a"), T0);
        await store.UpsertAsync(Write("engine-b", key, "belongs to b"), T0);

        var hits = await store.SeedAsync("engine-a", key, "s", null, null, 10, T0);

        Assert.Single(hits);
        Assert.Equal("belongs to a", hits[0].Content);
    }

    public static async Task The_candidate_cutoff_excludes_stale_associative_nodes(
        IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "stale associative note"), T0);

        // 100 days at stability 7 is ~14 half-lives; a cutoff of 4.32 (minR .05) must exclude it
        var hits = await store.SeedAsync("e", key, "s", null, 4.32, 10, T0.AddDays(100));

        Assert.Empty(hits);
    }

    public static async Task The_candidate_cutoff_never_excludes_authoritative_nodes(
        IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "an exact fact", MemoryGrade.Authoritative), T0);

        var hits = await store.SeedAsync("e", key, "s", null, 4.32, 10, T0.AddDays(10_000));

        Assert.Single(hits);
    }

    public static async Task Touch_records_reinforcement(IMemoryGraphStore store, string key)
    {
        var id = await store.UpsertAsync(Write("e", key, "reinforced"), T0);
        var at = T0.AddDays(3);

        await store.TouchAsync([new GraphTouch(id, at, 10.5)]);

        var node = await store.GetAsync("e", id);
        Assert.NotNull(node);
        Assert.Equal(at, node!.LastRecalledAt);
        Assert.Equal(10.5, node.Stability, precision: 6);
        Assert.Equal(1, node.RecallCount);
    }

    public static async Task Linked_nodes_are_reachable_as_neighbours(IMemoryGraphStore store, string key)
    {
        var a = await store.UpsertAsync(Write("e", key, "alpha"), T0);
        var b = await store.UpsertAsync(Write("e", key, "beta"), T0);
        await store.LinkAsync(a, b, null, 1, symmetric: true, T0);

        var neighbours = await store.NeighboursAsync("e", [a], 10, T0);

        Assert.Single(neighbours);
        Assert.Equal(b, neighbours[0].Id);
    }

    public static async Task Linking_the_same_pair_again_strengthens_it(IMemoryGraphStore store, string key)
    {
        var a = await store.UpsertAsync(Write("e", key, "alpha"), T0);
        var b = await store.UpsertAsync(Write("e", key, "beta"), T0);
        await store.LinkAsync(a, b, null, 1, symmetric: false, T0);
        await store.LinkAsync(a, b, null, 1, symmetric: false, T0);

        var neighbours = await store.NeighboursAsync("e", [a], 10, T0);

        Assert.Single(neighbours); // one edge, not two
    }

    public static async Task Degree_counts_connections(IMemoryGraphStore store, string key)
    {
        var hub = await store.UpsertAsync(Write("e", key, "hub"), T0);
        foreach (var spoke in new[] { "one", "two", "three" })
        {
            var id = await store.UpsertAsync(Write("e", key, spoke), T0);
            await store.LinkAsync(hub, id, null, 1, symmetric: true, T0);
        }

        var node = await store.GetAsync("e", hub);

        Assert.Equal(3, node!.Degree);
    }

    public static async Task Prune_removes_only_what_it_is_told_to(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "faded note"), T0);
        await store.UpsertAsync(Write("e", key, "exact note", MemoryGrade.Authoritative), T0);

        var removed = await store.PruneAsync("e", key, "s", 4.32, null, T0.AddDays(100));

        Assert.Equal(1, removed); // the authoritative one is never eligible
        Assert.Single(await store.SeedAsync("e", key, "s", null, null, 10, T0.AddDays(100)));
    }

    public static async Task Forget_clears_a_scope(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "gone"), T0);

        await store.ForgetAsync("e", key, "s");

        Assert.Empty(await store.SeedAsync("e", key, "s", null, null, 10, T0));
    }

    public static async Task Deleting_a_node_takes_its_edges_with_it(IMemoryGraphStore store, string key)
    {
        var a = await store.UpsertAsync(Write("e", key, "alpha"), T0);
        var b = await store.UpsertAsync(Write("e", key, "beta"), T0);
        await store.LinkAsync(a, b, null, 1, symmetric: true, T0);

        await store.ForgetAsync("e", key, "s");
        await store.UpsertAsync(Write("e", key, "alpha"), T0); // same content, new row

        var reborn = await store.SeedAsync("e", key, "s", "alpha", null, 10, T0);
        Assert.Single(reborn);
        Assert.Equal(0, reborn[0].Degree); // no dangling edge survived
    }

    public static async Task Cancellation_propagates(IMemoryGraphStore store, string key)
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.SeedAsync("e", key, "s", null, null, 10, T0, cts.Token));
    }
}
