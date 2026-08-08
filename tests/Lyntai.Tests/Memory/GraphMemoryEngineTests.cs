using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

public class GraphMemoryEngineTests
{
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private GraphMemoryEngine Engine(GraphMemoryOptions? options = null) =>
        new("project/graph", new InMemoryMemoryGraphStore(), options, clock: () => _now);

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

        var before = (await engine.RecallAsync(new MemoryQuery("t", "s", "reinforced"))).Items[0];
        _now = _now.AddDays(14);
        var after = (await engine.RecallAsync(new MemoryQuery("t", "s", "reinforced"))).Items[0];

        // 14 days on the original 7-day half-life would be r=0.25; the first recall pushed it out
        Assert.Equal(1.0, before.Retrievability, precision: 6);
        Assert.True(after.Retrievability > 0.25,
            $"reinforcement did not extend the half-life (r={after.Retrievability})");
    }

    [Fact]
    public async Task A_stale_associative_memory_falls_below_the_floor()
    {
        var engine = Engine();
        await engine.RememberAsync(new MemoryWrite("t", "s", "one-off noise"));

        _now = _now.AddDays(365);
        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "noise"));

        Assert.Empty(recall.Items);
    }

    [Fact]
    public async Task A_stale_authoritative_memory_does_not()
    {
        var engine = Engine();
        await engine.RememberAsync(new MemoryWrite("t", "s", "an exact fact",
            Grade: MemoryGrade.Authoritative));

        _now = _now.AddDays(10_000);
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
    public async Task A_failing_touch_still_returns_the_hits()
    {
        // a read-only database must degrade to "no learning", never to "no memory"
        var engine = new GraphMemoryEngine("project/graph", new TouchHostileGraphStore(), clock: () => _now);
        await engine.RememberAsync(new MemoryWrite("t", "s", "still recalled"));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "still"));

        Assert.Single(recall.Items);
    }

    [Fact]
    public async Task A_connected_memory_outlives_an_isolated_one()
    {
        // the point of feeding connectedness into decay: being embedded in the graph is itself a reason to
        // survive, not merely a way to be reached
        var engine = Engine();
        var hub = await engine.RememberAsync(new MemoryWrite("t", "s", "hub fact about widgets"));
        for (var i = 0; i < 8; i++)
        {
            var spoke = await engine.RememberAsync(new MemoryWrite("t", "s", $"widget detail {i}"));
            await engine.LinkAsync(hub, spoke, weight: 3, symmetric: true);
        }
        await engine.RememberAsync(new MemoryWrite("t", "s", "isolated fact about widgets"));

        // 45 days: past the isolated node's 7-day half-life several times over, still inside the window
        // the connection boost buys. That window is FINITE on purpose — by 60 days the edges have decayed
        // enough that the hub goes too, which is the model working, not a gap in it.
        _now = _now.AddDays(45);
        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "widgets"));

        Assert.Contains(recall.Items, i => i.Headline.Contains("hub fact", StringComparison.Ordinal));
        Assert.DoesNotContain(recall.Items, i => i.Headline.Contains("isolated", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_neighbourhood_that_went_quiet_stops_propping_a_memory_up()
    {
        var engine = Engine();
        var hub = await engine.RememberAsync(new MemoryWrite("t", "s", "hub fact about widgets"));
        for (var i = 0; i < 8; i++)
        {
            var spoke = await engine.RememberAsync(new MemoryWrite("t", "s", $"widget detail {i}"));
            await engine.LinkAsync(hub, spoke, weight: 3, symmetric: true);
        }

        // far enough out that the edges themselves have decayed away
        _now = _now.AddDays(2000);
        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "widgets"));

        Assert.DoesNotContain(recall.Items, i => i.Headline.Contains("hub fact", StringComparison.Ordinal));
    }

    [Fact]
    public void It_stores_both_grades()
    {
        Assert.Equal(MemoryGrades.Associative | MemoryGrades.Authoritative, Engine().Supported);
    }

    [Fact]
    public async Task Pruning_reaps_only_the_forgotten()
    {
        var engine = Engine();
        await engine.RememberAsync(new MemoryWrite("t", "s", "faded"));
        await engine.RememberAsync(new MemoryWrite("t", "s", "exact", Grade: MemoryGrade.Authoritative));

        _now = _now.AddDays(365);
        var removed = await engine.PruneAsync("t", "s", minRetrievability: 0.05);

        Assert.Equal(1, removed);
    }
}
