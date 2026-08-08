using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

/// <summary>Backend-agnostic <see cref="IMemoryGraphStore"/> facts. MEM2b runs these against SQLite and
/// Postgres unchanged; per <c>storage.md</c> the contract IS the deduplication mechanism for the relational
/// pair, not a shared base class.
/// <para><b>Aging is done by WRITING</b>, not by advancing a clock: an entry's age is how far the engine's
/// position has moved since it was last used, and only writes move it. <see cref="Crowd"/> is how these
/// facts make something old.</para>
/// <para>DELIBERATELY OMITTED, because the backends diverge by design exactly as
/// <c>IMemoryStore.RecallAsync</c> already documents: same-match ORDERING (SQLite ranks by bm25, the others
/// by recency) and MULTI-TOKEN matching. The portable guarantee asserted here is the single-token
/// one.</para></summary>
public static class MemoryGraphStoreContract
{
    private const double Stability = 7;

    private static GraphNodeWrite Write(string engine, string key, string content,
        MemoryGrade grade = MemoryGrade.Associative, double advance = 1) =>
        new(engine, key, "s", content, content, grade, Stability, advance, null);

    /// <summary>Advance the engine's position by writing unrelated material in another scope — which is
    /// exactly what makes an entry stale: newer material competing with it.</summary>
    private static async Task Crowd(IMemoryGraphStore store, string engine, string key, int writes)
    {
        for (var i = 0; i < writes; i++)
            await store.UpsertAsync(new GraphNodeWrite(engine, key, "filler", $"filler {i}", $"filler {i}",
                MemoryGrade.Associative, Stability, 1, null));
    }

    public static async Task Upsert_then_seed_by_single_token_substring(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "the deploy pipeline requires manual approval"));
        await store.UpsertAsync(Write("e", key, "rollbacks must page the on-call"));

        var hits = await store.SeedAsync("e", key, "s", "pipeline", null, 10);

        Assert.Single(hits);
        Assert.Contains("manual approval", hits[0].Content, StringComparison.Ordinal);
    }

    public static async Task Upserting_identical_content_refreshes_rather_than_duplicating(
        IMemoryGraphStore store, string key)
    {
        var first = await store.UpsertAsync(Write("e", key, "one fact"));
        var second = await store.UpsertAsync(Write("e", key, "one fact"));

        Assert.Equal(first, second);
        Assert.Single(await store.SeedAsync("e", key, "s", null, null, 10));
    }

    public static async Task Engines_are_isolated_from_one_another(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("engine-a", key, "belongs to a"));
        await store.UpsertAsync(Write("engine-b", key, "belongs to b"));

        var hits = await store.SeedAsync("engine-a", key, "s", null, null, 10);

        Assert.Single(hits);
        Assert.Equal("belongs to a", hits[0].Content);
    }

    /// <summary>A quiet engine must not be aged by a busy one — the reason the position is per engine
    /// rather than global.</summary>
    public static async Task A_busy_engine_does_not_age_a_quiet_ones_memories(
        IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("quiet", key, "a fact nobody disturbs"));
        await Crowd(store, "busy", key, 200);

        var hits = await store.SeedAsync("quiet", key, "s", null, 4.32, 10);

        Assert.Single(hits);
        Assert.Equal(0, hits[0].Age);
    }

    public static async Task The_candidate_cutoff_excludes_stale_associative_nodes(
        IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "stale associative note"));
        // 40 further writes against a stability of 7 is ~5.7 half-lives; a cutoff of 4.32 excludes it
        await Crowd(store, "e", key, 40);

        var hits = await store.SeedAsync("e", key, "s", null, 4.32, 10);

        Assert.Empty(hits);
    }

    /// <summary>The other half of the cutoff, and the one that catches a SILENT failure: if a backend's age
    /// arithmetic yields NULL the predicate excludes every row, and every other cutoff fact still passes
    /// for the wrong reason.</summary>
    public static async Task The_candidate_cutoff_keeps_fresh_associative_nodes(
        IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "a fresh associative note"));
        await Crowd(store, "e", key, 1);

        var hits = await store.SeedAsync("e", key, "s", null, 4.32, 10);

        Assert.Single(hits);
    }

    public static async Task The_candidate_cutoff_never_excludes_authoritative_nodes(
        IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "an exact fact", MemoryGrade.Authoritative));
        await Crowd(store, "e", key, 5000);

        var hits = await store.SeedAsync("e", key, "s", null, 4.32, 10);

        Assert.Single(hits);
    }

    public static async Task A_bigger_write_crowds_harder(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "the entry being crowded"));
        // one write, but the clock said it was worth 40 — the ContentSizeClock shape
        await store.UpsertAsync(Write("e", key, "one very large document", advance: 40));

        var hits = await store.SeedAsync("e", key, "s", "crowded", 4.32, 10);

        Assert.Empty(hits);
    }

    public static async Task Touch_records_reinforcement(IMemoryGraphStore store, string key)
    {
        var id = await store.UpsertAsync(Write("e", key, "reinforced"));
        await Crowd(store, "e", key, 3);

        await store.TouchAsync("e", [new GraphTouch(id, 10.5)]);

        var node = await store.GetAsync("e", id);
        Assert.NotNull(node);
        Assert.Equal(10.5, node!.Stability, precision: 6);
        Assert.Equal(1, node.RecallCount);
        Assert.Equal(0, node.Age); // a touch stamps the CURRENT position, so the entry is fresh again
    }

    /// <summary>Recall must not age anything: a read-only agent never forgets by reading.</summary>
    public static async Task A_touch_does_not_advance_the_position(IMemoryGraphStore store, string key)
    {
        var id = await store.UpsertAsync(Write("e", key, "the touched one"));
        var other = await store.UpsertAsync(Write("e", key, "the untouched one"));

        var before = (await store.GetAsync("e", other))!.Age;
        await store.TouchAsync("e", [new GraphTouch(id, 10)]);
        var after = (await store.GetAsync("e", other))!.Age;

        Assert.Equal(before, after);
    }

    public static async Task Linked_nodes_are_reachable_as_neighbours(IMemoryGraphStore store, string key)
    {
        var a = await store.UpsertAsync(Write("e", key, "alpha"));
        var b = await store.UpsertAsync(Write("e", key, "beta"));
        await store.LinkAsync("e", a, b, null, 1, symmetric: true);

        var neighbours = await store.NeighboursAsync("e", [a], 10);

        Assert.Single(neighbours);
        Assert.Equal(b, neighbours[0].Node.Id);
    }

    public static async Task Linking_the_same_pair_again_strengthens_it(IMemoryGraphStore store, string key)
    {
        var a = await store.UpsertAsync(Write("e", key, "alpha"));
        var b = await store.UpsertAsync(Write("e", key, "beta"));
        await store.LinkAsync("e", a, b, null, 1, symmetric: false);
        await store.LinkAsync("e", a, b, null, 1, symmetric: false);

        var neighbours = await store.NeighboursAsync("e", [a], 10);

        Assert.Single(neighbours); // one edge, not two
        Assert.Equal(2, neighbours[0].EdgeWeight, precision: 6);
    }

    public static async Task An_edge_ages_as_the_memory_moves_on(IMemoryGraphStore store, string key)
    {
        var a = await store.UpsertAsync(Write("e", key, "alpha"));
        var b = await store.UpsertAsync(Write("e", key, "beta"));
        await store.LinkAsync("e", a, b, null, 1, symmetric: true);
        await Crowd(store, "e", key, 12);

        var neighbours = await store.NeighboursAsync("e", [a], 10);

        Assert.Equal(12, neighbours[0].EdgeAge, precision: 6);
    }

    public static async Task Degree_counts_connections(IMemoryGraphStore store, string key)
    {
        var hub = await store.UpsertAsync(Write("e", key, "hub"));
        foreach (var spoke in new[] { "one", "two", "three" })
        {
            var id = await store.UpsertAsync(Write("e", key, spoke));
            await store.LinkAsync("e", hub, id, null, 1, symmetric: true);
        }

        var node = await store.GetAsync("e", hub);

        Assert.Equal(3, node!.Degree);
    }

    public static async Task A_node_reports_its_connection_strength_and_freshness(
        IMemoryGraphStore store, string key)
    {
        var hub = await store.UpsertAsync(Write("e", key, "hub"));
        var one = await store.UpsertAsync(Write("e", key, "one"));
        var two = await store.UpsertAsync(Write("e", key, "two"));
        await store.LinkAsync("e", hub, one, null, 3, symmetric: false);
        await Crowd(store, "e", key, 5);
        await store.LinkAsync("e", hub, two, null, 2, symmetric: false);
        await Crowd(store, "e", key, 2);

        var node = await store.GetAsync("e", hub);

        // raw sum, and staleness measured from the MOST RECENT strengthening — the store applies no curve
        Assert.Equal(5, node!.Strength, precision: 6);
        Assert.Equal(2, node.StrengthAge, precision: 6);
    }

    public static async Task An_unconnected_node_reports_no_strength(IMemoryGraphStore store, string key)
    {
        var id = await store.UpsertAsync(Write("e", key, "alone"));

        var node = await store.GetAsync("e", id);

        Assert.Equal(0, node!.Strength);
        Assert.Equal(0, node.StrengthAge);
    }

    public static async Task Prune_removes_only_what_it_is_told_to(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "faded note"));
        await store.UpsertAsync(Write("e", key, "exact note", MemoryGrade.Authoritative));
        await Crowd(store, "e", key, 40);

        var removed = await store.PruneAsync("e", key, "s", 4.32, null);

        Assert.Equal(1, removed); // the authoritative one is never eligible
        Assert.Single(await store.SeedAsync("e", key, "s", null, null, 10));
    }

    public static async Task Forget_clears_a_scope(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "gone"));

        await store.ForgetAsync("e", key, "s");

        Assert.Empty(await store.SeedAsync("e", key, "s", null, null, 10));
    }

    public static async Task Deleting_a_node_takes_its_edges_with_it(IMemoryGraphStore store, string key)
    {
        var a = await store.UpsertAsync(Write("e", key, "alpha"));
        var b = await store.UpsertAsync(Write("e", key, "beta"));
        await store.LinkAsync("e", a, b, null, 1, symmetric: true);

        await store.ForgetAsync("e", key, "s");
        await store.UpsertAsync(Write("e", key, "alpha")); // same content, new row

        var reborn = await store.SeedAsync("e", key, "s", "alpha", null, 10);
        Assert.Single(reborn);
        Assert.Equal(0, reborn[0].Degree); // no dangling edge survived
    }

    public static async Task Cancellation_propagates(IMemoryGraphStore store, string key)
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.SeedAsync("e", key, "s", null, null, 10, cts.Token));
    }
}
