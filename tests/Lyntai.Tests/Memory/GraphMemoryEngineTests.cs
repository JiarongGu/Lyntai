using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

public class GraphMemoryEngineTests
{
    /// <summary>An undamped per-write clock, so these facts age deterministically by counting: every write
    /// crowds by exactly one, and nothing depends on how fast the test happens to run.</summary>
    private static GraphMemoryEngine Engine(GraphMemoryOptions? options = null) =>
        new("project/graph", new InMemoryMemoryGraphStore(), options, memoryClock: new PerWriteClock());

    /// <summary>Make everything already stored older by writing unrelated material — which is what ages a
    /// memory now: newer material competing with it.</summary>
    private static async Task Crowd(GraphMemoryEngine engine, int writes)
    {
        for (var i = 0; i < writes; i++)
            await engine.RememberAsync(new MemoryWrite("t", "filler", $"unrelated filler number {i}"));
    }

    [Fact]
    public async Task Recall_returns_headlines_and_withholds_content_until_expansion()
    {
        var engine = Engine(new GraphMemoryOptions { HeadlineChars = 40 });
        var reference = await engine.RememberAsync(new MemoryWrite("t", "s",
            "The build gate runs seven checks and stops at the first failure."));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "build"));

        Assert.Single(recall.Items);
        Assert.Null(recall.Items[0].Content); // the whole point of a cheap first load
        Assert.True(recall.Items[0].Headline.Length <= 41, recall.Items[0].Headline);
        Assert.Equal(MemorySources.Graph, recall.Ran);

        var expanded = await engine.ExpandAsync(reference);
        Assert.Contains("seven checks", expanded.Items[0].Content!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_derived_headline_cuts_on_a_word_boundary_not_a_sentence()
    {
        // "The build gate is dev.mjs verify" split at the first period reads "The build gate is dev." —
        // confidently wrong, and worse than no memory at all.
        var engine = Engine(new GraphMemoryOptions { HeadlineChars = 24 });
        await engine.RememberAsync(new MemoryWrite("t", "s",
            "The build gate is dev.mjs verify and it runs seven checks"));

        var headline = (await engine.RecallAsync(new MemoryQuery("t", "s", "build"))).Items[0].Headline;

        Assert.EndsWith("…", headline, StringComparison.Ordinal);
        Assert.DoesNotContain("dev.…", headline, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_authored_headline_is_used_as_given()
    {
        var engine = Engine();
        await engine.RememberAsync(new MemoryWrite("t", "s", "a long body of explanatory text",
            Headline: "short form"));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "explanatory"));

        Assert.Equal("short form", recall.Items[0].Headline);
    }

    [Fact]
    public async Task Authoritative_material_is_never_shortened()
    {
        var engine = Engine(new GraphMemoryOptions { HeadlineChars = 10 });
        const string exact = "The build gate is node devtools/dev.mjs verify";
        await engine.RememberAsync(new MemoryWrite("t", "s", exact, Grade: MemoryGrade.Authoritative));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "gate"));

        Assert.Equal(exact, recall.Items[0].Headline);
        Assert.Equal(exact, recall.Items[0].Content);
        Assert.Equal(1.0, recall.Items[0].Retrievability, precision: 9);
    }

    [Fact]
    public async Task Recall_reinforces_what_it_returned()
    {
        var engine = Engine();
        await engine.RememberAsync(new MemoryWrite("t", "s", "reinforced fact"));

        await engine.RecallAsync(new MemoryQuery("t", "s", "reinforced")); // reinforces, does not age
        await Crowd(engine, 30);
        var after = (await engine.RecallAsync(new MemoryQuery("t", "s", "reinforced"))).Items[0];

        // 30 events against the original 20-event half-life would be r≈0.35; the first recall pushed the
        // half-life out to 30, so it stands higher than that
        Assert.True(after.Retrievability > 0.4,
            $"reinforcement did not extend the half-life (r={after.Retrievability})");
    }

    [Fact]
    public async Task Reading_does_not_age_anything()
    {
        // the property the interference clock exists for: a read-only agent never forgets by reading
        var engine = Engine();
        await engine.RememberAsync(new MemoryWrite("t", "s", "a stable fact"));

        for (var i = 0; i < 50; i++) await engine.RecallAsync(new MemoryQuery("t", "s", "stable"));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "stable"));
        Assert.Single(recall.Items);
        Assert.Equal(1.0, recall.Items[0].Retrievability, precision: 6);
    }

    [Fact]
    public async Task A_memory_nobody_writes_to_keeps_everything()
    {
        // a rarely-used engine must not decay like a busy one — the reason the position is per engine and
        // advances only on writes
        var quiet = Engine();
        await quiet.RememberAsync(new MemoryWrite("t", "s", "an undisturbed fact"));

        var busy = Engine();
        await Crowd(busy, 500);

        var recall = await quiet.RecallAsync(new MemoryQuery("t", "s", "undisturbed"));

        Assert.Single(recall.Items);
        Assert.Equal(1.0, recall.Items[0].Retrievability, precision: 6);
    }

    [Fact]
    public async Task A_faint_memory_alone_still_surfaces()
    {
        // BURIED, NOT CUT: nothing outranks it, so it is still the best thing there. It comes back faint —
        // the caller can see how faint from Retrievability — but it comes back.
        var engine = Engine();
        await engine.RememberAsync(new MemoryWrite("t", "s", "one-off noise"));

        await Crowd(engine, 200);
        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "noise"));

        Assert.Single(recall.Items);
        Assert.True(recall.Items[0].Retrievability < 0.01, "it should be faint, just not gone");
    }

    [Fact]
    public async Task A_faint_memory_is_buried_once_something_stronger_exists()
    {
        var engine = Engine();
        await engine.RememberAsync(new MemoryWrite("t", "s", "an old note about widgets"));
        await Crowd(engine, 200);
        await engine.RememberAsync(new MemoryWrite("t", "s", "a fresh note about widgets"));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "widgets"));

        Assert.Contains(recall.Items, i => i.Headline.Contains("fresh", StringComparison.Ordinal));
        Assert.DoesNotContain(recall.Items, i => i.Headline.Contains("old note", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_buried_memory_is_still_reachable_by_reference()
    {
        // the difference between buried and cut: it is out of the way, not destroyed
        var engine = Engine();
        var buried = await engine.RememberAsync(new MemoryWrite("t", "s", "an old note about widgets"));
        await Crowd(engine, 200);
        await engine.RememberAsync(new MemoryWrite("t", "s", "a fresh note about widgets"));

        var expanded = await engine.ExpandAsync(buried);

        Assert.Contains("an old note about widgets", expanded.Items[0].Content!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_crowded_out_authoritative_memory_does_not()
    {
        var engine = Engine();
        await engine.RememberAsync(new MemoryWrite("t", "s", "an exact fact",
            Grade: MemoryGrade.Authoritative));

        await Crowd(engine, 5000);
        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "exact"));

        Assert.Single(recall.Items);
    }

    [Fact]
    public async Task Items_recalled_together_become_connected()
    {
        var engine = Engine();
        await engine.RememberAsync(new MemoryWrite("t", "s", "alpha relates to the gate"));
        await engine.RememberAsync(new MemoryWrite("t", "s", "beta relates to the gate"));

        await engine.RecallAsync(new MemoryQuery("t", "s", "gate")); // co-activation happens here
        var again = await engine.RecallAsync(new MemoryQuery("t", "s", "gate"));

        Assert.All(again.Items, i => Assert.True(i.Degree >= 1,
            "co-activation did not link the items returned together"));
    }

    [Fact]
    public async Task Expansion_returns_the_neighbours_of_what_it_expanded()
    {
        var engine = Engine();
        var a = await engine.RememberAsync(new MemoryWrite("t", "s", "alpha fact"));
        var b = await engine.RememberAsync(new MemoryWrite("t", "s", "beta fact"));
        await engine.LinkAsync(a, b, symmetric: true);

        var expanded = await engine.ExpandAsync(a);

        Assert.Equal(2, expanded.Items.Count); // the node plus one neighbour
        Assert.Contains(expanded.Items, i => i.Headline.Contains("beta", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Spreading_reaches_a_neighbour_the_query_never_matched()
    {
        var engine = Engine(new GraphMemoryOptions { Hops = 1 });
        var a = await engine.RememberAsync(new MemoryWrite("t", "s", "the deploy pipeline"));
        var b = await engine.RememberAsync(new MemoryWrite("t", "s", "rollbacks page the on-call"));
        await engine.LinkAsync(a, b, symmetric: true);

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "pipeline"));

        Assert.Contains(recall.Items, i => i.Headline.Contains("rollbacks", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_hop_away_ranks_below_a_direct_match()
    {
        var engine = Engine(new GraphMemoryOptions { Hops = 1 });
        var a = await engine.RememberAsync(new MemoryWrite("t", "s", "the deploy pipeline"));
        var b = await engine.RememberAsync(new MemoryWrite("t", "s", "rollbacks page the on-call"));
        await engine.LinkAsync(a, b, symmetric: true);

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "pipeline"));

        Assert.Contains("pipeline", recall.Items[0].Headline, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_connected_memory_outranks_an_isolated_one()
    {
        // being embedded in the graph is itself a reason to stay retrievable, not merely a way to be
        // reached — and it shows up as RANK, since neither is cut
        var engine = Engine();
        var hub = await engine.RememberAsync(new MemoryWrite("t", "s", "hub fact about widgets"));
        for (var i = 0; i < 8; i++)
        {
            var spoke = await engine.RememberAsync(new MemoryWrite("t", "s", $"widget detail {i}"));
            await engine.LinkAsync(hub, spoke, weight: 3, symmetric: true);
        }
        await engine.RememberAsync(new MemoryWrite("t", "s", "isolated fact about widgets"));

        await Crowd(engine, 120);
        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "widgets"));

        var connected = recall.Items.Single(i => i.Headline.Contains("hub fact", StringComparison.Ordinal));
        var alone = recall.Items.Single(i => i.Headline.Contains("isolated", StringComparison.Ordinal));

        // both are still there — connectedness stretches the hub's half-life, it does not save it from
        // deletion, because nothing here deletes
        Assert.True(connected.Retrievability > alone.Retrievability * 3,
            $"connectedness barely mattered: {connected.Retrievability:F4} vs {alone.Retrievability:F4}");
    }

    [Fact]
    public async Task A_failing_touch_still_returns_the_hits()
    {
        // a read-only database must degrade to "no learning", never to "no memory"
        var engine = new GraphMemoryEngine("project/graph", new TouchHostileGraphStore(),
            memoryClock: new PerWriteClock());
        await engine.RememberAsync(new MemoryWrite("t", "s", "still recalled"));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "still"));

        Assert.Single(recall.Items);
    }

    [Fact]
    public void It_stores_both_grades() =>
        Assert.Equal(MemoryGrades.Associative | MemoryGrades.Authoritative, Engine().Supported);

    [Fact]
    public async Task Pruning_reaps_only_the_forgotten()
    {
        var engine = Engine();
        await engine.RememberAsync(new MemoryWrite("t", "s", "faded"));
        await engine.RememberAsync(new MemoryWrite("t", "s", "exact", Grade: MemoryGrade.Authoritative));

        // Pruning reaps by the policy's CANDIDATE CUTOFF, which is a conservative SUPERSET — widened by the
        // connection-boost ceiling. So prune UNDER-reaps rather than over-reaps: an entry can be below the
        // recall floor and still not be deleted. That is the right direction for a destructive operation,
        // and it is why this needs far more crowding than the recall floor does.
        await Crowd(engine, 400);
        var removed = await engine.PruneAsync("t", "s", minRetrievability: 0.05);

        Assert.Equal(1, removed);
    }
}
