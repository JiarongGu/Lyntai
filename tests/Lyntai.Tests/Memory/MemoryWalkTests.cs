using Lyntai.Memory;
using Lyntai.Memory.Engines;

namespace Lyntai.Tests.Memory;

/// <summary>The merge rule, which is the part a hand-rolled walk gets wrong. Pure — no engine, no I/O.</summary>
public class MemoryWalkMergeTests
{
    private static MemoryItem Item(string engine, string id, string headline, string? content) =>
        new(new MemoryRef(engine, id), headline, content, MemoryGrade.Associative, 0.5, 0.5, 0);

    [Fact]
    public void Identity_is_the_whole_reference_so_two_engines_may_share_an_id()
    {
        // Both bench harnesses key on Reference.Id alone and get away with it because they run ONE engine.
        // On a composite, two members can each own id "1" — and collapsing them loses a fact silently.
        var state = new MemoryWalkState(10);
        state.BeginStep();

        state.Hold(Item("a", "1", "from engine a", null));
        state.Hold(Item("b", "1", "from engine b", null));

        Assert.Equal(2, state.Items.Count);
    }

    [Fact]
    public void An_arriving_item_with_content_upgrades_a_held_headline()
    {
        var state = new MemoryWalkState(10);
        state.BeginStep();
        state.Hold(Item("a", "1", "the headline", null));

        state.BeginStep();
        state.Hold(Item("a", "1", "the headline", "the full content of the entry"));

        Assert.Equal(1, state.Upgraded);
        Assert.Empty(state.Discovered);
        var only = Assert.Single(state.Items);
        Assert.Equal("the full content of the entry", only.Content);
    }

    [Fact]
    public void The_upgrade_is_semantic_not_by_length_so_a_longer_headline_does_not_win()
    {
        // An AUTHORED headline can be longer than the content it heads. The harnesses use
        // `body.Length > held.Length` as a proxy; the two agree on their corpora and differ here.
        var state = new MemoryWalkState(10);
        state.BeginStep();
        state.Hold(Item("a", "1", "a very long authored headline indeed", "short body"));

        state.BeginStep();
        state.Hold(Item("a", "1", "a very long authored headline indeed", null));

        Assert.Equal(0, state.Upgraded);
        Assert.Equal("short body", Assert.Single(state.Items).Content);
    }

    [Fact]
    public void The_cap_gates_discoveries_and_never_upgrades()
    {
        var state = new MemoryWalkState(2);
        state.BeginStep();
        state.Hold(Item("a", "1", "first", null));
        state.Hold(Item("a", "2", "second", null));

        state.BeginStep();
        state.Hold(Item("a", "3", "a third entry the cap must refuse", null));
        state.Hold(Item("a", "1", "first", "the first entry in full"));

        Assert.Equal(2, state.Items.Count);
        Assert.Empty(state.Discovered);
        Assert.Equal(1, state.Upgraded);
        Assert.Equal("the first entry in full", state.Items[0].Content);
    }

    [Fact]
    public void Arrival_order_is_preserved_and_nothing_is_re_ranked()
    {
        // Nothing may re-rank across steps: an expanded neighbour reaches the caller with a Relevance from a
        // read that never asked a relevance question (GraphNode.Matched is null for a graph walk), and
        // MemoryItem does not carry Matched. Ranking on it is D97 one layer out.
        var state = new MemoryWalkState(10);
        state.BeginStep();
        state.Hold(Item("a", "1", "first", null));
        state.Hold(Item("a", "2", "second", null));
        state.BeginStep();
        state.Hold(Item("a", "3", "third", null));

        Assert.Equal(["1", "2", "3"], state.Items.Select(i => i.Reference.Id));
    }
}

/// <summary>The first step is the recall, and how the walk behaves when it cannot get one.</summary>
public class MemoryWalkFirstStepTests
{
    private static GraphMemoryEngine Graph() =>
        new("graph", new Lyntai.Storage.InMemory.InMemoryMemoryGraphStore());

    [Fact]
    public async Task The_first_step_is_the_recall_and_carries_the_tier_that_ran()
    {
        var engine = Graph();
        await engine.RememberAsync(new MemoryWrite("t", "s", "the deploy pipeline needs approval"));

        var steps = await engine.WalkAsync(new MemoryQuery("t", "s", "pipeline")).ToListAsync();

        Assert.NotEmpty(steps);
        Assert.Equal(1, steps[0].Ordinal);
        Assert.Equal(0, steps[0].Upgraded);
        Assert.Equal(steps[0].Items.Count, steps[0].Discovered.Count);
        Assert.True(steps[0].Ran.HasFlag(MemorySources.Graph));
    }

    [Fact]
    public async Task An_engine_that_cannot_expand_yields_exactly_one_step()
    {
        var engine = new LexicalMemoryEngine("lex", new FakeMemoryStore());
        await engine.RememberAsync(new MemoryWrite("t", "s", "a lexical fact about deployment"));

        var steps = await engine.WalkAsync(new MemoryQuery("t", "s", "deployment")).ToListAsync();

        Assert.Single(steps);
    }

    [Fact]
    public async Task A_faulting_recall_still_yields_step_one_reporting_no_tier_ran()
    {
        // Recall is contractually fail-open, but a BYO engine that ignores that must not sink the walk —
        // the same posture MemoryComposition.ComposeAsync takes. MemorySources.None is what tells a caller
        // "the store did not answer" apart from "nothing matched".
        var steps = await new ThrowingEngine().WalkAsync(new MemoryQuery("t", "s", "anything")).ToListAsync();

        var only = Assert.Single(steps);
        Assert.Empty(only.Items);
        Assert.Equal(MemorySources.None, only.Ran);
    }

    [Fact]
    public async Task Cancellation_propagates_out_of_the_enumeration()
    {
        var engine = Graph();
        await engine.RememberAsync(new MemoryWrite("t", "s", "a fact about deployment"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await engine.WalkAsync(new MemoryQuery("t", "s", "deployment"), ct: cts.Token).ToListAsync());
    }

    [Fact]
    public void A_null_argument_throws_at_the_CALL_not_at_the_first_enumeration()
    {
        // An iterator's body does not run until the first MoveNextAsync, so without the eager-validation
        // split this would surface at the caller's `await foreach` instead of where the mistake was made.
        Assert.Throws<ArgumentNullException>(() => Graph().WalkAsync(null!));
    }

    private sealed class ThrowingEngine : IMemoryEngine
    {
        public string Name => "throws";

        public MemoryGrades Supported => MemoryGrades.Associative;

        public Task<MemoryRef> RememberAsync(MemoryWrite write, CancellationToken ct = default) =>
            throw new InvalidOperationException("the store is down");

        public Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default) =>
            throw new InvalidOperationException("the store is down");
    }
}

/// <summary>Steps 2 and up. Two fixtures, because one cannot discriminate everything: the chain has a single
/// matching entry, so "a later step reached what the recall did not" means something; the star has three,
/// which is the only shape in which a seed COUNT is observable at all.</summary>
public class MemoryWalkExpansionTests
{
    private static GraphMemoryEngine New() =>
        new("graph", new Lyntai.Storage.InMemory.InMemoryMemoryGraphStore());

    private static async Task<GraphMemoryEngine> ChainAsync()
    {
        // Only the FIRST entry matches the query, so reaching the other two is the walk's doing rather than
        // the recall's — which is what makes the step-2 assertions mean anything.
        var engine = New();
        var a = await engine.RememberAsync(new MemoryWrite("t", "s", "the deploy pipeline needs approval"));
        var b = await engine.RememberAsync(new MemoryWrite("t", "s", "approval is granted by the release owner"));
        var c = await engine.RememberAsync(new MemoryWrite("t", "s", "the release owner rotates each quarter"));
        await engine.LinkAsync(a, b, symmetric: true);
        await engine.LinkAsync(b, c, symmetric: true);
        return engine;
    }

    private static async Task<GraphMemoryEngine> StarAsync()
    {
        // Three entries matching "pipeline", each linked to one leaf that does not. So the number of leaves
        // reached at step 2 IS the number of seeds expanded. A recall also co-activates the three hubs with
        // each other, so an expansion returns held hubs beside its leaf — already held, so not discoveries.
        var engine = New();
        for (var i = 1; i <= 3; i++)
        {
            var hub = await engine.RememberAsync(new MemoryWrite("t", "s", $"pipeline stage number {i}"));
            var leaf = await engine.RememberAsync(new MemoryWrite("t", "s", $"an unrelated detail about topic {i}"));
            await engine.LinkAsync(hub, leaf, symmetric: true);
        }
        return engine;
    }

    [Fact]
    public async Task A_later_step_reaches_entries_the_recall_did_not()
    {
        var engine = await ChainAsync();

        var steps = await engine.WalkAsync(new MemoryQuery("t", "s", "pipeline", Limit: 1)).ToListAsync();

        Assert.True(steps.Count >= 2, $"expected the walk to take a second step, took {steps.Count}");
        Assert.True(steps[^1].Items.Count > steps[0].Items.Count);
    }

    [Fact]
    public async Task Expanding_a_held_entry_upgrades_it_from_a_headline_to_full_content()
    {
        // The whole payload of expanding something you already have. A recall returns headlines for
        // associative material, so a step that only counted new entries would report this as buying nothing.
        var engine = await ChainAsync();

        var steps = await engine.WalkAsync(new MemoryQuery("t", "s", "pipeline", Limit: 1)).ToListAsync();

        Assert.Contains(steps, s => s.Upgraded > 0);
        Assert.Contains(steps[^1].Items, i => i.Content is not null);
    }

    [Fact]
    public async Task The_walk_ends_on_its_own_and_never_yields_a_step_that_moved_nothing()
    {
        var engine = await ChainAsync();

        // no break anywhere, and it terminates
        var steps = await engine.WalkAsync(new MemoryQuery("t", "s", "pipeline", Limit: 1)).ToListAsync();

        Assert.NotEmpty(steps);
        // Asserted over ALL steps past the first, not just the last: a no-op step in the middle is the same
        // defect, and a last-step-only check cannot see it.
        Assert.All(steps.Skip(1), s => Assert.True(s.Discovered.Count > 0 || s.Upgraded > 0,
            $"step {s.Ordinal} moved nothing and should not have been yielded"));
    }

    [Fact]
    public async Task The_default_bound_is_DERIVED_from_what_the_first_step_returned()
    {
        var engine = await ChainAsync();

        var steps = await engine.WalkAsync(new MemoryQuery("t", "s", "pipeline", Limit: 1)).ToListAsync();

        // step 1 returned 1 item, so the derived bound is 2 — not a constant, and not the 3 available
        Assert.Single(steps[0].Items);
        Assert.All(steps, s => Assert.True(s.Items.Count <= 2,
            $"step {s.Ordinal} held {s.Items.Count}, above the derived bound of 2"));
        Assert.Equal(2, steps[^1].Items.Count);   // it really did fill it, so the bound is what stopped it
    }

    [Fact]
    public async Task MaxEntries_overrides_the_derived_bound()
    {
        // step 1 returns 3 hubs, so the DERIVED bound would be 6 and all three leaves would fit
        var derived = await (await StarAsync())
            .WalkAsync(new MemoryQuery("t", "s", "pipeline", Limit: 3)).ToListAsync();
        Assert.Equal(6, derived[^1].Items.Count);

        var bounded = await (await StarAsync())
            .WalkAsync(new MemoryQuery("t", "s", "pipeline", Limit: 3),
                new MemoryWalkOptions { MaxEntries = 4 }).ToListAsync();

        Assert.All(bounded, s => Assert.True(s.Items.Count <= 4));
        Assert.Equal(4, bounded[^1].Items.Count);
    }

    [Fact]
    public async Task SeedsPerStep_bounds_how_many_entries_the_default_selector_expands()
    {
        var one = await (await StarAsync()).WalkAsync(new MemoryQuery("t", "s", "pipeline", Limit: 3),
            new MemoryWalkOptions { SeedsPerStep = 1 }).ToListAsync();
        var three = await (await StarAsync()).WalkAsync(new MemoryQuery("t", "s", "pipeline", Limit: 3),
            new MemoryWalkOptions { SeedsPerStep = 3 }).ToListAsync();

        // one seed reaches one leaf; three seeds reach three. Both start from the same three hubs.
        Assert.Equal(3, one[0].Discovered.Count);
        Assert.Equal(3, three[0].Discovered.Count);
        Assert.Single(one[1].Discovered);
        Assert.Equal(3, three[1].Discovered.Count);
    }

    [Fact]
    public async Task A_selector_that_never_returns_empty_still_terminates()
    {
        // The `seeds.Count == 0` guard carries termination for the DEFAULT selector only. A caller-supplied
        // one can hand back seeds forever, so what makes the walk finite is the separate rule that a step
        // moving NOTHING ends it. Without that rule this loops until the process dies.
        var engine = await StarAsync();

        var steps = new List<MemoryWalkStep>();
        await foreach (var step in engine.WalkAsync(new MemoryQuery("t", "s", "pipeline", Limit: 3),
            new MemoryWalkOptions { SelectSeeds = s => s.Items }))
        {
            steps.Add(step);
            if (steps.Count > 20) break;   // bounds the TEST, so a non-terminating walk fails instead of hanging
        }

        Assert.InRange(steps.Count, 1, 20);
    }

    [Fact]
    public async Task SelectSeeds_replaces_the_default_outright_INCLUDING_its_count()
    {
        // SeedsPerStep = 1 would truncate to one seed if it applied. It must not: a supplied selector is
        // authoritative, so this walk expands all three hubs and reaches all three leaves.
        var engine = await StarAsync();

        var steps = await engine.WalkAsync(new MemoryQuery("t", "s", "pipeline", Limit: 3),
            new MemoryWalkOptions { SeedsPerStep = 1, SelectSeeds = step => step.Discovered })
            .ToListAsync();

        Assert.Equal(3, steps[1].Discovered.Count);
    }

    [Fact]
    public async Task An_empty_selection_ends_the_walk_after_the_step_that_produced_it()
    {
        var engine = await StarAsync();
        var consulted = new List<int>();

        var steps = await engine.WalkAsync(new MemoryQuery("t", "s", "pipeline", Limit: 3),
            new MemoryWalkOptions { SelectSeeds = step => { consulted.Add(step.Ordinal); return []; } })
            .ToListAsync();

        Assert.Single(steps);
        Assert.Equal([1], consulted);
    }
}
