using System.Globalization;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Ranking;
using Lyntai.Memory.Salience;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

public class GraphMemoryEngineTests
{
    /// <summary>An undamped per-write age policy, so these facts age deterministically by counting: every write
    /// crowds by exactly one, and nothing depends on how fast the test happens to run.</summary>
    private static GraphMemoryEngine Engine(GraphMemoryOptions? options = null) =>
        new("project/graph", new InMemoryMemoryGraphStore(), options, agePolicies: [new PerWriteAgePolicy()]);

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
        // the property interference-measured age exists for: a read-only agent never forgets by reading
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
        //
        // Crowd count and threshold retuned for DsrRetrievability, the bare-constructor default as of 3.0
        // (docs/DECISIONS.md — HalfLifeRetrievability, whose fast exponential decay reached r<0.01 at a
        // crowd of 200, is deleted). DSR's heavier power-law tail decays far more slowly by design — the
        // property FSRS was adopted for — so reaching an absolute r<0.01 needs many tens of thousands of
        // writes; 2000 keeps this fast while still being unmistakably faint relative to a fresh recall's
        // r=1. MEASURED (fix round 1): 0.057639, comfortably under the loosened 0.1 bar below.
        var engine = Engine();
        await engine.RememberAsync(new MemoryWrite("t", "s", "one-off noise"));

        await Crowd(engine, 2000);
        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "noise"));

        Assert.Single(recall.Items);
        Assert.True(recall.Items[0].Retrievability < 0.1, "it should be faint, just not gone");
    }

    [Fact]
    public async Task A_faint_memory_is_buried_once_something_stronger_exists()
    {
        // Explicit RelativeFloor (2026-08-10, fsrs-properly plan Task 1) rather than a much larger crowd
        // count: burial is decided by MultiplicativeRankingOptions.RelativeFloor, RELATIVE to the best score
        // in the result set, and DSR's heavy tail keeps a merely-200-writes-old entry well above the
        // shipped 0.02 default (MEASURED, fix round 1: old note alone reads 0.179213 at this age — 18% of a
        // fresh one's r=1), so it would take tens of thousands of writes to push it under that specific bar
        // by decay alone. Raising the floor here to 0.5 (comfortably above the measured 0.179) proves the
        // same "something stronger buries something faint" claim this fact has always made, without
        // resurrecting the write count HalfLifeRetrievability's much faster decay used to need.
        var ranking = new MultiplicativeRankingPolicy(new MultiplicativeRankingOptions { RelativeFloor = 0.5 });
        var engine = new GraphMemoryEngine("project/graph", new InMemoryMemoryGraphStore(),
            agePolicies: [new PerWriteAgePolicy()], ranking: ranking);
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

    /// <summary>The fixture both tests below share: a hub linked to a BURIED entry and a live one, where the
    /// buried entry deliberately holds the heavier edge — so edge weight alone keeps it and only
    /// retrievability can drop it.</summary>
    private static async Task<MemoryRef> Superseded(GraphMemoryEngine engine)
    {
        var stale = await engine.RememberAsync(new MemoryWrite("t", "s", "the deploy target is the alpha host"));
        await Crowd(engine, 200);
        var hub = await engine.RememberAsync(new MemoryWrite("t", "s", "deploy notes for the service"));
        var current = await engine.RememberAsync(new MemoryWrite("t", "s", "the deploy target is the beta host"));
        await engine.LinkAsync(hub, stale, weight: 1.0);
        await engine.LinkAsync(hub, current, weight: 0.5);
        return hub;
    }

    [Fact]
    public async Task A_recall_writes_its_co_activation_set_as_ONE_store_call_not_one_per_pair()
    {
        // At CoActivationCap = 5 a recall links C(5,2) = 10 pairs, and each LinkAsync on a relational store
        // opens its own connection AND re-reads the position totals. This pins the round-trip COUNT rather
        // than a latency, because `memory-scale` cannot resolve the change above its own noise: its 10k p50
        // spans 8.9-11.2ms across runs of identical code. What is checkable here is that ten calls became one.
        var store = new LinkCountingGraphStore();
        var engine = new GraphMemoryEngine("project/graph", store, agePolicies: [new PerWriteAgePolicy()]);
        for (var i = 0; i < 6; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s", $"the deploy gate runs check number {i}"));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "deploy gate check"));

        Assert.True(recall.Items.Count >= 5, $"needs 5+ hits to link a full set, got {recall.Items.Count}");
        Assert.Equal(0, store.SingleLinks);
        Assert.Equal(1, store.BatchedLinks);
        Assert.Equal(10, store.EdgesWritten);   // C(5,2), the cap's own arithmetic
    }

    [Fact]
    public async Task A_recalls_whole_write_back_reaches_the_store_as_ONE_call_not_three()
    {
        // The touch, the co-activation edges and the review-log rows were three calls, and on a relational
        // store each opened its own connection. Same countable-claim reasoning as the fact above, one level
        // up: `memory-scale` cannot resolve what this buys above its own run-to-run spread, so what is
        // checkable is that three calls became one.
        var store = new WriteBackCountingGraphStore();
        var engine = new GraphMemoryEngine("project/graph", store, agePolicies: [new PerWriteAgePolicy()]);
        for (var i = 0; i < 6; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s", $"the deploy gate runs check number {i}"));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "deploy gate check"));

        Assert.True(recall.Items.Count >= 2, $"needs hits to write anything back, got {recall.Items.Count}");
        Assert.Equal(1, store.WriteBacks);
        Assert.Equal(0, store.DirectTouches);
        Assert.Equal(0, store.DirectBatchedLinks);
        Assert.Equal(0, store.DirectReviewWrites);

        // and the review log LAST, which is what keeps a broken one from costing the other two — the
        // isolation ReinforceAsync used to buy with a second catch around the log write
        Assert.Equal(["touch", "edges", "reviews"], store.Order);
    }

    [Fact]
    public async Task Expansion_walks_to_a_BURIED_neighbour_by_default_so_forgetting_has_no_vote_in_traversal()
    {
        // The control, and the defect it describes is measured: EdgeHalfLife decays the EDGE and nothing
        // consulted the ENTRY, so a recall buries a superseded fact and an expansion hands it straight back.
        // On LongMemEval's knowledge-update class that cost 4 points of clean context per extra shot.
        var engine = Engine();

        var walked = (await engine.ExpandAsync(await Superseded(engine))).Items.Skip(1).ToList();

        Assert.Contains(walked, i => i.Headline.Contains("alpha", StringComparison.Ordinal));
        Assert.Contains(walked, i => i.Headline.Contains("beta", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_retrievability_floor_stops_expansion_resurrecting_what_recall_buried()
    {
        // Same fixture, one option. The buried entry holds the HEAVIER edge, so this cannot pass on edge
        // weight or on the id tiebreak — only the floor drops it.
        var engine = Engine(new GraphMemoryOptions { ExpansionRetrievabilityFloor = 0.8 });

        var walked = (await engine.ExpandAsync(await Superseded(engine))).Items.Skip(1).ToList();

        Assert.DoesNotContain(walked, i => i.Headline.Contains("alpha", StringComparison.Ordinal));
        Assert.Contains(walked, i => i.Headline.Contains("beta", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_floor_never_hides_the_entry_the_caller_NAMED_however_buried_it_is()
    {
        // Asking to expand something buried must still return it: the floor filters the walk OUT from a seed,
        // never the seed. Without this an entry could become unreachable by the one call that names it.
        var engine = Engine(new GraphMemoryOptions { ExpansionRetrievabilityFloor = 0.99 });
        var buried = await engine.RememberAsync(new MemoryWrite("t", "s", "an old note about widgets"));
        await Crowd(engine, 300);

        var expanded = await engine.ExpandAsync(buried);

        Assert.Contains("an old note about widgets", expanded.Items[0].Content!, StringComparison.Ordinal);
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
    public async Task A_recall_honours_the_query_char_budget_without_dropping_an_exact_fact()
    {
        // MemoryQuery.CharBudget shipped in 2.5.0 documented as "maximum characters the caller intends to
        // spend" and was read by NOTHING for a whole released version — the same class as ExpandAsync's own
        // charBudget and GraphMemoryOptions.MinRetrievability. Found 2026-08-14.
        var engine = Engine();
        await engine.RememberAsync(new MemoryWrite("t", "s",
            "the deployment gate is the verify command", Grade: MemoryGrade.Authoritative));
        for (var i = 0; i < 6; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s",
                $"deployment note number {i} with a reasonable amount of surrounding text"));

        var unbounded = await engine.RecallAsync(new MemoryQuery("t", "s", "deployment"));
        var bounded = await engine.RecallAsync(new MemoryQuery("t", "s", "deployment", CharBudget: 60));

        Assert.True(bounded.Items.Count < unbounded.Items.Count,
            $"the budget must cut: unbounded {unbounded.Items.Count}, bounded {bounded.Items.Count}");
        // objective (1): the exact fact survives a budget that cuts everything around it
        Assert.Contains(bounded.Items, i => i.Grade == MemoryGrade.Authoritative);
    }

    [Fact]
    public async Task A_char_budget_too_small_for_anything_still_returns_one_item()
    {
        // A caller asking for less than one fact gets one fact, not nothing — a recall that answers a
        // too-small budget with silence is indistinguishable from "nothing matched", which is the
        // degradation shape this subsystem keeps getting wrong.
        var engine = Engine();
        await engine.RememberAsync(new MemoryWrite("t", "s", "a deployment note of some length"));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "deployment", CharBudget: 1));

        Assert.NotEmpty(recall.Items);
    }

    [Fact]
    public async Task Prune_uses_the_configured_MinRetrievability_when_the_caller_names_no_floor()
    {
        // Found 2026-08-14: GraphMemoryOptions.MinRetrievability occurred ONLY in its own declaration and its
        // own validation guard. PruneAsync took an independent parameter and never consulted the option, so
        // with both criteria null `doomed` was empty and `engine.PruneAsync("t")` — the documented shape —
        // deleted NOTHING while design §5.7 and the option's own summary both said it governs pruning.
        // The entry must actually BE faint — a fresh one is fully retrievable, so no floor would reach it and
        // the test would pass for the wrong reason. Crowd() is how this subsystem ages something: newer
        // material competing with it.
        var engine = Engine(new GraphMemoryOptions { MinRetrievability = 0.9 });
        await engine.RememberAsync(new MemoryWrite("t", "s", "a faint associative entry"));
        await Crowd(engine, 40);

        var removed = await engine.PruneAsync("t", "s");

        Assert.True(removed >= 1, $"the configured floor must remove the faded entry; removed {removed}");
        Assert.Empty((await engine.RecallAsync(new MemoryQuery("t", "s", "faint"))).Items);
    }

    [Fact]
    public async Task A_zero_MinRetrievability_removes_nothing_which_is_the_opt_out()
    {
        // The escape hatch, asserted rather than assumed: retrievability is never below zero, so a floor of 0
        // means "never remove on this criterion" and restores the pre-fix behaviour for a deployment that wants
        // deletion to happen only when a caller names a floor explicitly.
        var engine = Engine(new GraphMemoryOptions { MinRetrievability = 0 });
        await engine.RememberAsync(new MemoryWrite("t", "s", "a faint associative entry"));
        await Crowd(engine, 40);

        Assert.Equal(0, await engine.PruneAsync("t", "s"));
        Assert.NotEmpty((await engine.RecallAsync(new MemoryQuery("t", "s", "faint"))).Items);
    }

    [Fact]
    public async Task Prune_never_removes_an_authoritative_fact_however_low_the_floor()
    {
        // Objective (1) again: an exact fact is not eligible for removing at any floor. Guarded here because
        // wiring the option turned PruneAsync from a no-op into something that actually deletes.
        var engine = Engine(new GraphMemoryOptions { MinRetrievability = 0.99 });
        await engine.RememberAsync(new MemoryWrite("t", "s", "the exact fact", Grade: MemoryGrade.Authoritative));

        Assert.Equal(0, await engine.PruneAsync("t", "s"));
    }

    [Fact]
    public async Task Expansion_walks_the_requested_number_of_hops()
    {
        // Found 2026-08-14: `hops` appeared ONLY in ExpandAsync's signature — never in its body, which
        // hard-coded a single hop. MemoryTools forwards a model-supplied value AND advertises it in the tool
        // JSON schema, so an agent asking for hops:2 silently got one hop with no error and no signal.
        var engine = Engine(new GraphMemoryOptions { Hops = 3 });
        var a = await engine.RememberAsync(new MemoryWrite("t", "s", "alpha fact"));
        var b = await engine.RememberAsync(new MemoryWrite("t", "s", "beta fact"));
        var c = await engine.RememberAsync(new MemoryWrite("t", "s", "gamma fact"));
        await engine.LinkAsync(a, b, symmetric: true);
        await engine.LinkAsync(b, c, symmetric: true);   // c is TWO hops from a

        var one = await engine.ExpandAsync(a, hops: 1);
        Assert.DoesNotContain(one.Items, i => i.Headline.Contains("gamma", StringComparison.Ordinal));

        var two = await engine.ExpandAsync(a, hops: 2);
        Assert.Contains(two.Items, i => i.Headline.Contains("beta", StringComparison.Ordinal));
        Assert.Contains(two.Items, i => i.Headline.Contains("gamma", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Expansion_with_zero_hops_returns_only_the_entry_itself()
    {
        // GraphMemoryWiringTests documents `hops: 0` as "nothing but the entry itself returns" and reads
        // Items[0] — so it passed while neighbours were returned anyway. Now the claim is the behaviour.
        var engine = Engine();
        var a = await engine.RememberAsync(new MemoryWrite("t", "s", "alpha fact"));
        var b = await engine.RememberAsync(new MemoryWrite("t", "s", "beta fact"));
        await engine.LinkAsync(a, b, symmetric: true);

        var expanded = await engine.ExpandAsync(a, hops: 0);

        Assert.Single(expanded.Items);
        Assert.Contains("alpha", expanded.Items[0].Headline, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expansion_stops_adding_neighbours_once_the_char_budget_is_spent()
    {
        // charBudget had the same defect as hops — accepted, documented, never read. The expanded entry is
        // ALWAYS returned whatever the budget: returning its full content is what expansion IS, so a budget
        // smaller than that entry bounds the NEIGHBOURS rather than refusing the request.
        var engine = Engine();
        var a = await engine.RememberAsync(new MemoryWrite("t", "s", "alpha fact"));
        for (var i = 0; i < 5; i++)
            await engine.LinkAsync(a, await engine.RememberAsync(
                new MemoryWrite("t", "s", $"neighbour number {i} with a reasonable amount of text")), symmetric: true);

        var unbounded = await engine.ExpandAsync(a, hops: 1);
        var bounded = await engine.ExpandAsync(a, hops: 1, charBudget: 40);

        Assert.True(unbounded.Items.Count > bounded.Items.Count,
            $"a budget must cut neighbours: unbounded {unbounded.Items.Count}, bounded {bounded.Items.Count}");
        Assert.NotEmpty(bounded.Items);
        Assert.Contains("alpha", bounded.Items[0].Headline, StringComparison.Ordinal);
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
        //
        // Threshold lowered for DsrRetrievability (2026-08-10, fsrs-properly plan Task 1): under the deleted
        // exponential curve, a stability boost compounds EXPONENTIALLY with age (r = 2^(-age/(S*boost))), so
        // the connected/isolated ratio grows without bound as age increases. Under DSR's power law the same
        // boost only rescales the curve's ARGUMENT, so the ratio APPROACHES a ceiling of
        // sqrt(MaxConnectionBoost) (= 2 at the shipped default of 4) as age grows, and edge-weight decay
        // (GraphMemoryOptions.EdgeHalfLife) further shrinks the boost actually in force by the time this
        // recall happens — measured, ~1.4x at this crowd. A ratio bound this file's own predecessor could
        // ask for unconditionally is now mathematically unreachable; this still proves the same claim
        // (connectedness measurably helps) with a bound DSR can actually clear.
        Assert.True(connected.Retrievability > alone.Retrievability * 1.2,
            $"connectedness barely mattered: {connected.Retrievability:F4} vs {alone.Retrievability:F4}");
    }

    [Fact]
    public async Task A_failing_touch_still_returns_the_hits()
    {
        // a read-only database must degrade to "no learning", never to "no memory"
        var engine = new GraphMemoryEngine("project/graph", new TouchHostileGraphStore(),
            agePolicies: [new PerWriteAgePolicy()]);
        await engine.RememberAsync(new MemoryWrite("t", "s", "still recalled"));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "still"));

        Assert.Single(recall.Items);
    }

    [Fact]
    public void It_stores_both_grades() =>
        Assert.Equal(MemoryGrades.Associative | MemoryGrades.Authoritative, Engine().Supported);

    [Fact]
    public async Task Pruning_removes_only_the_forgotten()
    {
        var engine = Engine();
        await engine.RememberAsync(new MemoryWrite("t", "s", "faded"));
        await engine.RememberAsync(new MemoryWrite("t", "s", "exact", Grade: MemoryGrade.Authoritative));

        // Pruning removes by the policy's CANDIDATE CUTOFF, which is a conservative SUPERSET — widened by the
        // connection-boost ceiling. So prune UNDER-removes rather than over-removes: an entry can be below the
        // recall floor and still not be deleted. That is the right direction for a destructive operation,
        // and it is why this needs far more crowding than the recall floor does.
        //
        // Crowd raised for DsrRetrievability (2026-08-10, fsrs-properly plan Task 1): 400 writes cleared the
        // deleted exponential curve's 0.05 floor at InitialStability 20 in a few half-lives; DSR's heavier
        // tail needs far more age to fall under the same floor. MEASURED (fix round 1, via a direct
        // store read of DsrRetrievability.Retrievability — bypassing RecallAsync so the probe itself does
        // not reinforce the entry it is measuring): r=0.128037 at a crowd of 400 (still above the floor,
        // confirming 400 no longer prunes), r=0.047088 at 3000 (technically under the floor but only ~6%
        // below it — tighter than every other retuned margin in this sweep), r=0.033318 at 6000 (~33% below
        // the floor, matching the margin quality used elsewhere). 6000 is what ships.
        await Crowd(engine, 6000);
        var removed = await engine.PruneAsync("t", "s", minRetrievability: 0.05);

        Assert.Equal(1, removed);
    }

    /// <summary>Fix round 1, I-1's own failure scenario, reproduced directly: a corpus built under
    /// <see cref="ContentSizeAgePolicy"/> (the store's raw position accumulator advances in CHARACTERS,
    /// growing large fast), then a SECOND engine instance over the SAME store, reconfigured to
    /// <see cref="PerWriteAgePolicy"/> alone (resolved age = ordinal WRITE count, small). Before this fix,
    /// <see cref="GraphMemoryEngine.PruneAsync"/> always delegated to the store's cheap, accumulator-based
    /// cutoff — which still held the STALE, chars-based residue from before the swap — and would remove an
    /// entry the swapped engine's own <c>RecallAsync</c> correctly rates well within its retention window.
    /// No recall runs on either engine before the assertion, so nothing is reinforced/touched first — the
    /// accumulator is exactly what the ContentSize-governed writes above left it at.
    /// <para><b>Fix round 2, I-1: extended to a CONNECTED entry, which the original scenario above
    /// structurally cannot exercise.</b> "the seed fact" alone has <c>Strength == 0</c> (no edge, no recall,
    /// no embedder before this point), so it was never routed through
    /// <c>GraphMemoryEngine.HasUnknownStrengthUnit</c>'s guard at all — round 1's own fix (re-deriving
    /// <c>Age</c>) is a complete story for an UNCONNECTED entry, and this method's first half still proves
    /// exactly that, unmodified. "the linked fact" below is EXPLICITLY linked to a neighbour while
    /// <see cref="ContentSizeAgePolicy"/> still governs the store (so <c>strengthened_position</c> is stamped
    /// in the CHARS unit), then the same 50-filler crowd leaves its <c>StrengthAge</c> stale by the same
    /// ~10,000 the round-1 scenario already measures for <c>Age</c> — except <c>StrengthAge</c> is never
    /// re-derived by anything, round 1 or round 2, so after the swap it is STILL that stale number. A floor
    /// the entry would clear with its rightful connection boost intact, but would NOT clear if that boost
    /// collapsed to 1x under the bogus, enormous <c>StrengthAge</c> (exactly as if it had no edge at all), is
    /// what actually discriminates the fix: see the mutation-check note below.</para></summary>
    [Fact]
    public async Task Prune_agrees_with_recall_after_a_policy_swap_rather_than_removing_the_stale_accumulator()
    {
        var store = new InMemoryMemoryGraphStore();
        var underContentSize = new GraphMemoryEngine("e", store, agePolicies: [new ContentSizeAgePolicy(perUnit: 1)]);

        await underContentSize.RememberAsync(new MemoryWrite("t", "s", "the seed fact"));

        // fix round 2, I-1: an EXPLICITLY connected entry, linked EARLY (position still small) so its
        // `strengthened_position` is stamped in the CHARS unit about to become stale — mirroring exactly how
        // "the seed fact" above is aged: written first, then left behind by 50 chars-heavy filler writes.
        var linked = await underContentSize.RememberAsync(new MemoryWrite("t", "s", "the linked fact"));
        var neighbour = await underContentSize.RememberAsync(new MemoryWrite("t", "s", "a linked neighbour"));
        await underContentSize.LinkAsync(linked, neighbour, weight: 20, symmetric: true);

        var filler = new string('x', 200);
        for (var i = 0; i < 50; i++)
            await underContentSize.RememberAsync(new MemoryWrite("t", "s", $"{filler} {i}"));

        // SWAP: a fresh engine instance over the SAME store, now governed by PerWriteAgePolicy alone.
        var underPerWrite = new GraphMemoryEngine("e", store, agePolicies: [new PerWriteAgePolicy()]);

        // 50 ordinal writes at InitialStability 20 clears a modest floor easily (2^(-50/20) ~ 0.177). The
        // SAME fact under the STALE chars-based accumulator (~50*200=10000 over the same stability) reads
        // 2^(-500) — indistinguishable from zero — which is exactly the divergence round 1's fix closes: the
        // pre-round-1 code would have removed it.
        var removed = await underPerWrite.PruneAsync("t", "s", minRetrievability: 0.05);
        Assert.Equal(0, removed);

        var recalled = await underPerWrite.RecallAsync(new MemoryQuery("t", "s", "seed"));
        Assert.Single(recalled.Items);

        // fix round 2, I-1's own assertion: a stricter floor. Plenty of OLD, UNCONNECTED filler genuinely
        // fails 0.3 and is correctly removed (unrelated to this fix — nothing protects an unconnected entry
        // beyond round 1's own age re-derivation), so this does NOT assert `removed == 0` overall. What it
        // asserts is narrower and load-bearing: "the linked fact" specifically survives.
        //
        // 3.0 pre-freeze: it now survives ON ITS MERITS rather than behind the blanket guard this originally
        // pinned. StrengthAge is re-derived in the CURRENT policy's unit (50 writes, not ~10,150 chars), so
        // the entry keeps its rightful connection boost and reads r ~ 0.486 — clear of 0.3. Reading the raw
        // chars-unit residue instead collapses the boost to 1x and gives ~0.340, which also clears 0.3, so
        // this assertion no longer discriminates between the two on its own; that is exactly what
        // Prune_removes_a_connected_entry_on_its_re_derived_strength_age_instead_of_refusing_outright's
        // two-sided 0.40/0.60 pair exists to do.
        await underPerWrite.PruneAsync("t", "s", minRetrievability: 0.3);
        var stillLinked = await underPerWrite.RecallAsync(new MemoryQuery("t", "s", "linked"));
        Assert.Contains(stillLinked.Items, i => i.Headline.Contains("the linked fact", StringComparison.Ordinal));
    }

    /// <summary><b>A connected entry's <c>StrengthAge</c> is re-derived in the CURRENT age policy's own unit,
    /// so pruning is EXACT for it rather than merely conservative</b> (3.0 pre-freeze; closes the
    /// "future work" the design doc §5.7 and <c>GraphMemoryEngine.PruneAsync</c>'s own remarks recorded).
    /// <para>Before this, <c>Strength</c>/<c>StrengthAge</c> were the store's raw
    /// <c>position - strengthened_position</c> subtraction in whatever unit was in force when the edge was
    /// last strengthened, while <c>Age</c> re-derived from the swap-safe primitives — so the derivable prune
    /// path could not trust the connection boost and refused to delete ANY connected entry on the
    /// retrievability criterion. That is safe but wrong: a genuinely unretrievable connected entry was
    /// unremovable forever.</para>
    /// <para><b>Both halves are load-bearing, and they fail in OPPOSITE directions</b> — which is what makes
    /// this discriminate the real fix from either mistake. The scenario is the swap
    /// <see cref="Prune_agrees_with_recall_after_a_policy_swap_rather_than_removing_the_stale_accumulator"/>
    /// already establishes: writes governed by <see cref="ContentSizeAgePolicy"/> (position counts CHARS,
    /// reaching ~10,150 by the end), then a second engine over the same store governed by
    /// <see cref="PerWriteAgePolicy"/> alone (resolved age counts WRITES, 51).
    /// <list type="bullet">
    /// <item><b>Floor 0.40 — the entry must SURVIVE.</b> With its strength age correctly re-derived as 50
    /// WRITES, <c>EffectiveStrength = 20·2^(-50/100) = 14.14</c>, so the boost is
    /// <c>1 + 0.5·ln(15.14) = 2.36</c> and the effective stability <c>20·2.36 = 47.2</c> —
    /// <c>r = (1 + 3·51/47.2)^-0.5 = 0.486</c>, comfortably clear. An implementation that kept reading the
    /// RAW, chars-unit <c>StrengthAge</c> (~10,150) collapses the boost to <c>1×</c>, giving
    /// <c>r = (1 + 3·51/20)^-0.5 = 0.340</c> — below the floor, so the entry is wrongly removed and this half
    /// reddens.</item>
    /// <item><b>Floor 0.60 — the same entry must be REMOVED.</b> Its true <c>r</c> is 0.486, genuinely below
    /// 0.60. The old blanket guard (<c>never delete an entry with Strength &gt; 0 &amp;&amp; StrengthAge &gt;
    /// 0</c>) retains it unconditionally, so this half is what reddens against the pre-fix code.</item>
    /// </list>
    /// Ordered survive-then-remove on ONE store deliberately: the 0.40 prune leaves both endpoints (and so the
    /// edge) intact, which is what makes the 0.60 prune a test of the same connected state rather than of an
    /// entry that quietly lost its link.</para></summary>
    [Fact]
    public async Task Prune_removes_a_connected_entry_on_its_re_derived_strength_age_instead_of_refusing_outright()
    {
        var store = new InMemoryMemoryGraphStore();
        var underContentSize = new GraphMemoryEngine("e", store, agePolicies: [new ContentSizeAgePolicy(perUnit: 1)]);

        var linked = await underContentSize.RememberAsync(new MemoryWrite("t", "s", "the linked fact"));
        var neighbour = await underContentSize.RememberAsync(new MemoryWrite("t", "s", "a linked neighbour"));
        await underContentSize.LinkAsync(linked, neighbour, weight: 20, symmetric: true);

        var filler = new string('x', 200);
        for (var i = 0; i < 50; i++)
            await underContentSize.RememberAsync(new MemoryWrite("t", "s", $"{filler} {i}"));

        var underPerWrite = new GraphMemoryEngine("e", store, agePolicies: [new PerWriteAgePolicy()]);
        var id = long.Parse(linked.Id, CultureInfo.InvariantCulture);

        // the rightful connection boost clears 0.40 — reading the raw chars-unit StrengthAge does not
        await underPerWrite.PruneAsync("t", "s", minRetrievability: 0.40);
        Assert.NotNull(await store.GetAsync("e", id));

        // ...and it is genuinely below 0.60, so it is now removable rather than guarded forever
        await underPerWrite.PruneAsync("t", "s", minRetrievability: 0.60);
        Assert.Null(await store.GetAsync("e", id));
    }

    /// <summary><b>The THIRD age axis — an edge's own traversal age — is projected through the installed age
    /// policies too, so all three axes finally speak one unit.</b> `Age` and `StrengthAge` were made
    /// swap-safe first; `GraphNeighbour.EdgeAge` was the last one still read as the raw
    /// <c>position - strengthened_position</c> accumulator, which after a policy swap mixes pre- and
    /// post-swap units WITHIN ITSELF.
    /// <para>Unlike the other two this one never deleted anything — it only orders a traversal — which is why
    /// it is a coherence fix rather than a measured data-loss bug. It is taken inside the 3.0 window for the
    /// same reason D50 and D52's own item were: adding a member to the <c>GraphNeighbour</c> record is
    /// binary-breaking, so it is free today and a whole major afterwards.</para>
    /// <para><b>The scenario makes the two units disagree by construction.</b> Under
    /// <see cref="ContentSizeAgePolicy"/> the position counts CHARS, so the far edge — linked early, then
    /// buried under ~10,150 characters of filler — reads an edge age of ~10,168 positions but only 51 WRITES.
    /// At the shipped <c>EdgeHalfLife</c> of 100:
    /// <list type="bullet">
    /// <item>raw (chars): <c>100 × 2^(-10168/100)</c> is indistinguishable from zero, so the weak-but-fresh
    /// "near" edge (weight 10, age 0) wins and this fact reddens;</item>
    /// <item>projected (writes): <c>100 × 2^(-51/100) = 70.1</c>, comfortably above near's 10, so the heavy
    /// edge correctly still leads.</item>
    /// </list>
    /// Weight 100 vs 10 is deliberately lopsided: the fact must turn on the AGE UNIT, not on the weights, so
    /// the heavier edge has to be the one the raw unit wrongly buries.</para></summary>
    [Fact]
    public async Task Expansion_ranks_an_edge_by_its_re_derived_age_not_the_raw_position_accumulator()
    {
        var store = new InMemoryMemoryGraphStore();
        var underContentSize = new GraphMemoryEngine("e", store, agePolicies: [new ContentSizeAgePolicy(perUnit: 1)]);

        var hub = await underContentSize.RememberAsync(new MemoryWrite("t", "s", "the hub fact"));
        var far = await underContentSize.RememberAsync(new MemoryWrite("t", "s", "the far neighbour"));
        await underContentSize.LinkAsync(hub, far, weight: 100);

        var filler = new string('x', 200);
        for (var i = 0; i < 50; i++)
            await underContentSize.RememberAsync(new MemoryWrite("t", "s", $"{filler} {i}"));

        // linked LAST, so its edge is fresh on every scale — the control the far edge must still beat
        var near = await underContentSize.RememberAsync(new MemoryWrite("t", "s", "the near neighbour"));
        await underContentSize.LinkAsync(hub, near, weight: 10);

        var underPerWrite = new GraphMemoryEngine("e", store, agePolicies: [new PerWriteAgePolicy()]);
        var expanded = await underPerWrite.ExpandAsync(hub);

        // items[0] is the expanded node itself; the neighbours follow, ordered by DECAYED edge weight
        var neighbours = expanded.Items.Skip(1).Select(i => i.Headline).ToList();
        Assert.Equal(2, neighbours.Count);
        Assert.Contains("far", neighbours[0], StringComparison.Ordinal);
    }

    /// <summary><b>A recall does TWO separable things to every entry it returns — it RESETS the entry's age
    /// and (when the curve is configured to) GROWS the entry's stability — and they are welded into one
    /// call.</b> Pinned because the decomposition is the load-bearing finding of `TASKS.md` Part 64, and
    /// nothing else in the suite states it.
    /// <para><b>The growth half is switched on explicitly here, because as of 3.0 it is OFF by default</b>
    /// (<c>DsrOptions.ReinforceGain = 0</c>, <c>docs/DECISIONS.md</c> D54 — retrieval-driven growth measured
    /// as harmful, and capped and non-compounding variants both lost to not growing). The WELD is what this
    /// fact is about and the weld is still there: one call, two effects, no way to ask for the reset without
    /// the growth. That is precisely why the option gating them TOGETHER was reverted rather than
    /// shipped.</para>
    /// <para><b>Why it matters that these are separable in EFFECT but not in API.</b> Measured across four
    /// studies on 2026-08-12, the two pull in OPPOSITE directions: the age reset is what keeps a
    /// rarely-queried fact alive (removing it collapses recall quality on every shape), while the stability
    /// growth is what entrenches whatever the ranking policy already favoured (it is what wrecks the
    /// `topical` class). A `ReinforceOn` option that gated both together was written and REVERTED the same
    /// day for exactly this reason — it could not express "reset age, do not grow", which is the
    /// best-measured configuration and is reachable today only through
    /// <c>DsrOptions.ReinforceGain = 0</c>.</para>
    /// <para>Both assertions are POSITIVE. A test asserting only that something did not change would pass
    /// just as well if the recall returned nothing at all (<c>pitfalls.md</c>), so the query here is a
    /// literal contiguous substring of the content — belt and braces since 3.0, when
    /// <see cref="Lyntai.Storage.SearchTerms"/> gave this store term-wise matching and a shared term would
    /// have sufficed — and the recall is asserted non-empty before either effect is checked.</para>
    /// <para>Crowding first is load-bearing rather than scene-setting: a just-written entry recalls at
    /// <c>r = 1</c>, where law 3's term is <c>e^0 - 1 = 0</c>, so an immediate re-recall grows NOTHING by
    /// design. Without ageing it, the stability half of this fact would pass vacuously.</para></summary>
    [Fact]
    public async Task A_recall_both_resets_age_and_grows_stability_in_one_inseparable_step()
    {
        const string content = "the deploy pipeline requires manual approval";
        const string query = "deploy pipeline";   // contiguous in the content, so the match needs nothing clever

        var store = new InMemoryMemoryGraphStore();
        var engine = new GraphMemoryEngine("e", store,
            retrievability: new DsrRetrievability(new DsrOptions { ReinforceGain = 2.0 }),
            agePolicies: [new PerWriteAgePolicy()]);
        var reference = await engine.RememberAsync(new MemoryWrite("t", "s", content));
        var id = long.Parse(reference.Id, CultureInfo.InvariantCulture);

        await Crowd(engine, 10);
        var before = (await store.GetAsync("e", id))!;
        Assert.True(before.OrdinalAge > 0, "the entry must have aged, or the reset below proves nothing");

        Assert.NotEmpty((await engine.RecallAsync(new MemoryQuery("t", "s", query))).Items);

        var after = (await store.GetAsync("e", id))!;
        Assert.Equal(0, after.OrdinalAge, precision: 9);                 // effect 1: the age reset
        Assert.True(after.Stability > before.Stability,                   // effect 2: the stability growth
            $"stability must grow on recall; was {before.Stability}, now {after.Stability}");
    }

    /// <summary><b>The two effects are separable in the API now, not only in principle — "reset the age, do
    /// not grow the stability" is expressible without the curve's cooperation.</b> This is the configuration
    /// four studies converged on (`TASKS.md` Part 64): the age reset is what keeps a rarely-queried critical
    /// fact alive, the stability growth is what entrenches whatever the ranker already favoured.
    /// <para><b>Why an ENGINE option and not the curve's own knob.</b> It is reachable today only through
    /// <c>DsrOptions.ReinforceGain = 0</c> — one shipped curve's private constant. A consumer who writes
    /// their own <see cref="IMemoryRetrievabilityPolicy"/>, or registers a future one, has no such knob and
    /// no way to ask for this at all. Which effects a recall applies is the ENGINE's decision about
    /// learning, not a property of the forgetting curve, so it belongs here.</para>
    /// <para><b>The growth is switched ON at the policy explicitly</b> (<c>ReinforceGain = 2.0</c>), so the
    /// assertion cannot pass by accident on 3.0's growth-free default — the curve is trying to grow and the
    /// engine is what stops it. That also makes this the exact inverse of the weld fact above, which runs
    /// the same policy with the same gain and gets both effects.</para></summary>
    [Fact]
    public async Task Reinforcement_effects_are_separable_the_age_resets_while_stability_is_left_alone()
    {
        const string content = "the deploy pipeline requires manual approval";
        const string query = "deploy pipeline";

        var store = new InMemoryMemoryGraphStore();
        var engine = new GraphMemoryEngine("e", store,
            retrievability: new DsrRetrievability(new DsrOptions { ReinforceGain = 2.0 }),
            agePolicies: [new PerWriteAgePolicy()],
            options: new GraphMemoryOptions
            {
                Reinforcement = MemoryReinforcementEffects.AgeReset,
            });
        var reference = await engine.RememberAsync(new MemoryWrite("t", "s", content));
        var id = long.Parse(reference.Id, CultureInfo.InvariantCulture);

        await Crowd(engine, 10);
        var before = (await store.GetAsync("e", id))!;
        Assert.True(before.OrdinalAge > 0, "the entry must have aged, or the reset below proves nothing");

        Assert.NotEmpty((await engine.RecallAsync(new MemoryQuery("t", "s", query))).Items);

        var after = (await store.GetAsync("e", id))!;
        Assert.Equal(0, after.OrdinalAge, precision: 9);                  // effect 1: still applied
        Assert.Equal(before.Stability, after.Stability, precision: 9);    // effect 2: suppressed
    }

    /// <summary><b>The inverse control: with the SAME curve and the same gain, the default option set still
    /// grows.</b> Without this, the fact above would pass just as well if the engine had stopped reinforcing
    /// entirely, or if <c>ReinforceGain = 2.0</c> silently did nothing — both of which would make it a test
    /// of the wrong thing. It is the same shape as the authoritative-survival control (D56): a promise about
    /// a switch needs the switch's OTHER position measured too.</summary>
    [Fact]
    public async Task The_default_effect_set_still_grows_stability_so_the_suppression_above_is_the_option()
    {
        const string content = "the deploy pipeline requires manual approval";
        const string query = "deploy pipeline";

        var store = new InMemoryMemoryGraphStore();
        var engine = new GraphMemoryEngine("e", store,
            retrievability: new DsrRetrievability(new DsrOptions { ReinforceGain = 2.0 }),
            agePolicies: [new PerWriteAgePolicy()]);
        var reference = await engine.RememberAsync(new MemoryWrite("t", "s", content));
        var id = long.Parse(reference.Id, CultureInfo.InvariantCulture);

        await Crowd(engine, 10);
        var before = (await store.GetAsync("e", id))!;

        Assert.NotEmpty((await engine.RecallAsync(new MemoryQuery("t", "s", query))).Items);

        var after = (await store.GetAsync("e", id))!;
        Assert.True(after.Stability > before.Stability,
            $"the default must still grow; was {before.Stability}, now {after.Stability}");
    }

    /// <summary><b><see cref="MemoryReinforcementEffects.None"/> skips the store call outright — it is not
    /// "touch with everything suppressed".</b> The measured "neither" arm collapsed combined recall quality
    /// on every shape, so this position exists to be MEASURABLE and configurable, not because it is
    /// recommended. Asserting the age too is what distinguishes it from
    /// <see cref="MemoryReinforcementEffects.AgeReset"/>.</summary>
    [Fact]
    public async Task None_applies_neither_effect_so_a_recall_leaves_the_entry_exactly_as_it_found_it()
    {
        const string content = "the deploy pipeline requires manual approval";
        const string query = "deploy pipeline";

        var store = new InMemoryMemoryGraphStore();
        var engine = new GraphMemoryEngine("e", store,
            retrievability: new DsrRetrievability(new DsrOptions { ReinforceGain = 2.0 }),
            agePolicies: [new PerWriteAgePolicy()],
            options: new GraphMemoryOptions { Reinforcement = MemoryReinforcementEffects.None });
        var reference = await engine.RememberAsync(new MemoryWrite("t", "s", content));
        var id = long.Parse(reference.Id, CultureInfo.InvariantCulture);

        await Crowd(engine, 10);
        var before = (await store.GetAsync("e", id))!;
        Assert.True(before.OrdinalAge > 0, "the entry must have aged, or the age assertion proves nothing");

        Assert.NotEmpty((await engine.RecallAsync(new MemoryQuery("t", "s", query))).Items);

        var after = (await store.GetAsync("e", id))!;
        Assert.Equal(before.OrdinalAge, after.OrdinalAge, precision: 9);
        Assert.Equal(before.Stability, after.Stability, precision: 9);
    }

    /// <summary><b>Growth without the age reset is REFUSED at construction, not silently ignored.</b> The
    /// store resets the age as an inseparable part of the same write, so the engine can only honour that
    /// combination by skipping the write entirely — which would apply neither effect while the configuration
    /// claimed one. A deployment whose reinforcement quietly does nothing is the failure this guard exists to
    /// prevent, and it is the same reasoning `MultiplicativeRankingOptions` validates on: throw at the line
    /// that configured it rather than return wrong answers for the rest of the process's life.</summary>
    [Fact]
    public void Growth_without_the_age_reset_is_refused_because_it_would_silently_apply_neither()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GraphMemoryOptions { Reinforcement = MemoryReinforcementEffects.StabilityGrowth });

        Assert.Contains("would apply NEITHER effect", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>An undefined bit is refused too — widening a flags enum is how a typo silently becomes a
    /// configuration.</summary>
    [Fact]
    public void An_undefined_reinforcement_flag_is_refused()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GraphMemoryOptions { Reinforcement = (MemoryReinforcementEffects)8 });

    /// <summary><b>A non-finite value is refused at the line that configured it, because this engine has
    /// already been bitten by exactly this once.</b>
    ///
    /// <para><c>DsrOptions.MaxStability</c> accepted <c>NaN</c> until 3.0; it propagated through
    /// <c>Math.Min</c>, was written back to the store, and a <c>NaN</c> stability compares false against
    /// every threshold — so the entry neither ranked, nor pruned, nor reported as broken. The fix there was
    /// a throw at construction, and `GraphMemoryOptions` kept plain accessors that take anything a
    /// <c>double</c> can hold.</para>
    ///
    /// <para><b>The live one is <see cref="GraphMemoryOptions.EdgeHalfLife"/>.</b> <c>EffectiveEdgeWeight</c>
    /// guards it with <c>halfLife &lt;= 0</c>, which is FALSE for <c>NaN</c> — so a NaN falls straight
    /// through into <c>Math.Pow(2, -age / NaN)</c> and every edge weight in the graph becomes NaN. Traversal
    /// then orders by a value that compares false against everything, so spreading silently stops
    /// discriminating and nothing anywhere reports a problem.</para></summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_non_finite_edge_half_life_is_refused_rather_than_poisoning_every_edge_weight(double value)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new GraphMemoryOptions { EdgeHalfLife = value });

    /// <summary>The other two doubles on this record, for the same reason: both feed comparisons that a
    /// <c>NaN</c> makes meaningless rather than loud.</summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_non_finite_similarity_or_retrievability_floor_is_refused(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphMemoryOptions { MinSimilarity = value });
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphMemoryOptions { MinRetrievability = value });
    }

    /// <summary>A finite value in range still constructs — the false-positive direction, so the guard cannot
    /// be "throw on everything".</summary>
    [Fact]
    public void Ordinary_finite_values_still_construct()
    {
        var o = new GraphMemoryOptions { EdgeHalfLife = 60, MinSimilarity = 0.7, MinRetrievability = 0.1 };

        Assert.Equal(60, o.EdgeHalfLife, precision: 9);
        Assert.Equal(0.7, o.MinSimilarity, precision: 9);
        Assert.Equal(0.1, o.MinRetrievability, precision: 9);
    }

    // ---- provenance validated at construction time (fix round 2, cheap minor) ----

    private sealed class FixedProvenanceSaliencePolicy(MemorySalienceProvenance provenance) : IMemorySaliencePolicy
    {
        public MemorySalienceProvenance Provenance => provenance;
        public MemorySignals Signals(MemoryWrite write, in SalienceContext context) => MemorySignals.Empty;
    }

    private sealed class FixedProvenanceRetrievability(MemoryRetrievabilityProvenance provenance)
        : IMemoryRetrievabilityPolicy
    {
        public double InitialStability => 20;
        public MemoryRetrievabilityProvenance Provenance => provenance;
        public double Retrievability(in MemoryDecayState state) => 1;
        public MemoryDecayState Reinforce(in MemoryDecayState state) => state;
        public double CandidateCutoff(double minRetrievability) => double.PositiveInfinity;
    }

    /// <summary>A second, DIFFERENTLY-NAMED salience policy type — the genuine cross-type collision the production
    /// check exists to catch (see
    /// <c>MemoryProvenanceTests.A_consumer_policy_colliding_with_a_shipped_bit_is_rejected_by_the_production_check</c>).</summary>
    private sealed class AnotherFixedProvenanceSaliencePolicy(MemorySalienceProvenance provenance) : IMemorySaliencePolicy
    {
        public MemorySalienceProvenance Provenance => provenance;
        public MemorySignals Signals(MemoryWrite write, in SalienceContext context) => MemorySignals.Empty;
    }

    [Fact]
    public void Two_DIFFERENTLY_TYPED_salience_policies_declaring_the_same_bit_are_rejected_at_construction()
    {
        var bit = (MemorySalienceProvenance)(1L << 40);

        var ex = Assert.Throws<ArgumentException>(() => new GraphMemoryEngine("e", new InMemoryMemoryGraphStore(),
            saliencePolicies: [new FixedProvenanceSaliencePolicy(bit), new AnotherFixedProvenanceSaliencePolicy(bit)]));

        Assert.Contains("Provenance", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_salience_policy_declaring_None_is_rejected_at_construction()
    {
        var ex = Assert.Throws<ArgumentException>(() => new GraphMemoryEngine("e", new InMemoryMemoryGraphStore(),
            saliencePolicies: [new FixedProvenanceSaliencePolicy(MemorySalienceProvenance.None)]));

        Assert.Contains("None", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_retrievability_policy_declaring_None_is_rejected_at_construction()
    {
        var ex = Assert.Throws<ArgumentException>(() => new GraphMemoryEngine("e", new InMemoryMemoryGraphStore(),
            retrievability: new FixedProvenanceRetrievability(MemoryRetrievabilityProvenance.None)));

        Assert.Contains("None", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_salience_policy_INSTANCES_of_the_SAME_type_sharing_a_bit_construct_fine()
    {
        // the distinction MemoryProvenance.ValidateProvenanceBits exists to make: same TYPE,
        // same bit, no collision — only a DIFFERENT type sharing a bit (above) is rejected. Constructing
        // without throwing is the whole assertion.
        var bit = (MemorySalienceProvenance)(1L << 41);
        _ = new GraphMemoryEngine("e", new InMemoryMemoryGraphStore(),
            saliencePolicies: [new FixedProvenanceSaliencePolicy(bit), new FixedProvenanceSaliencePolicy(bit)]);
    }

    // ---- the engine's own injectable clock (fix round 2, cheap minor) ----

    [Fact]
    public async Task PruneAsync_olderThan_reads_the_engines_own_injected_clock_on_the_derivable_path()
    {
        // before this fix, the derivable branch always read DateTimeOffset.UtcNow directly — no seam on the
        // engine at all, disagreeing with a test that fakes the STORE's own clock (every IMemoryGraphStore
        // already takes one). The entry below is written under the REAL clock (an ordinary `CreatedAt`,
        // "now"); only the ENGINE's clock is faked, to far in the future — `olderThan` becomes trivially
        // satisfied for anything already written ONLY if the engine actually consulted the injected clock
        // rather than the real one (which cannot be in the year 2999 in a fast-running test, so a fallback
        // to the real clock would leave `removed == 0`).
        var fixedNow = new DateTimeOffset(2999, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var store = new InMemoryMemoryGraphStore();
        var engine = new GraphMemoryEngine("e", store, agePolicies: [new PerWriteAgePolicy()],
            clock: () => fixedNow);

        await engine.RememberAsync(new MemoryWrite("t", "s", "long since written"));

        var removed = await engine.PruneAsync("t", "s", olderThan: TimeSpan.FromDays(1));

        Assert.Equal(1, removed);
    }
}
