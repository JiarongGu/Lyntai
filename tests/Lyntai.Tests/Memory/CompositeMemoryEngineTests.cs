using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

public class CompositeMemoryEngineTests
{
    private static CompositeMemoryEngine Composite(params IMemoryEngine[] members) =>
        new("project", members);

    private static MemoryItem Assoc(string engine, string text) =>
        new(new MemoryRef(engine, text), text, text, MemoryGrade.Associative, 1, 1, 0);

    private static MemoryItem Exact(string engine, string text) =>
        new(new MemoryRef(engine, text), text, text, MemoryGrade.Authoritative, 1, 1, 0);

    [Fact]
    public async Task It_merges_every_members_items_and_unions_the_tiers()
    {
        var composite = Composite(
            new StaticEngine("project/lexical", [Assoc("project/lexical", "recalled thing")],
                MemorySources.Lexical),
            new StaticEngine("project/glossary", [Exact("project/glossary", "exact thing")],
                MemorySources.Curated));

        var recall = await composite.RecallAsync(new MemoryQuery("t", "s", "thing"));

        Assert.Equal(2, recall.Items.Count);
        Assert.Equal(MemorySources.Lexical | MemorySources.Curated, recall.Ran);
    }

    [Fact]
    public async Task Items_keep_the_owning_members_name_not_the_composites()
    {
        var composite = Composite(new StaticEngine("project/lexical", [Assoc("project/lexical", "owned")]));

        var recall = await composite.RecallAsync(new MemoryQuery("t", "s", "owned"));

        Assert.Equal("project/lexical", recall.Items[0].Reference.Engine);
    }

    [Fact]
    public async Task One_faulting_member_does_not_sink_the_others()
    {
        var composite = Composite(
            new FaultingEngine("project/broken"),
            new StaticEngine("project/ok", [Assoc("project/ok", "still here")]));

        var recall = await composite.RecallAsync(new MemoryQuery("t", "s", "still"));

        Assert.Single(recall.Items);
        Assert.Equal("still here", recall.Items[0].Headline);
    }

    [Fact]
    public void Supported_is_the_union_of_its_members()
    {
        var composite = Composite(
            new StaticEngine("project/lexical", [], grades: MemoryGrades.Associative),
            new StaticEngine("project/glossary", [], grades: MemoryGrades.Authoritative));

        Assert.Equal(MemoryGrades.Associative | MemoryGrades.Authoritative, composite.Supported);
    }

    [Fact]
    public async Task An_authoritative_write_is_routed_to_a_member_that_can_hold_it()
    {
        var lexical = new RecordingEngine("project/lexical", MemoryGrades.Associative);
        var glossary = new RecordingEngine("project/glossary", MemoryGrades.Authoritative);
        var composite = Composite(lexical, glossary);

        var reference = await composite.RememberAsync(
            new MemoryWrite("t", "s", "exact", Grade: MemoryGrade.Authoritative));

        Assert.Equal("project/glossary", reference.Engine);
        Assert.Empty(lexical.Writes);
        Assert.Single(glossary.Writes);
    }

    [Fact]
    public async Task An_unroutable_write_throws_and_names_what_was_considered()
    {
        var composite = Composite(new RecordingEngine("project/lexical", MemoryGrades.Associative));

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            composite.RememberAsync(new MemoryWrite("t", "s", "exact", Grade: MemoryGrade.Authoritative)));

        Assert.Contains("project", ex.Message, StringComparison.Ordinal);
        Assert.Contains("project/lexical", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_blend_returns_at_most_the_query_limit()
    {
        // Found 2026-08-14: the composite added every member's items and returned them with no Take, so a
        // 2-member blend answered a Limit of 10 with up to 20. MemoryEngineBuilder ALWAYS wraps members in a
        // composite, so this was the shape every multi-member consumer had. Same class as the
        // AuthoritativeReserve incident pitfalls.md records — a bound configured on one scope (the query) and
        // enforced on another (never).
        var a = new RecordingEngine("project/a", MemoryGrades.Associative)
            .Returning("a1", MemoryGrade.Associative, 0.9).Returning("a2", MemoryGrade.Associative, 0.8);
        var b = new RecordingEngine("project/b", MemoryGrades.Associative)
            .Returning("b1", MemoryGrade.Associative, 0.7).Returning("b2", MemoryGrade.Associative, 0.6);

        var recall = await Composite(a, b).RecallAsync(new MemoryQuery("t", "s", "q", Limit: 3));

        Assert.Equal(3, recall.Items.Count);
    }

    [Fact]
    public async Task A_blend_cut_to_its_limit_keeps_authoritative_facts_over_better_scoring_associative_ones()
    {
        // Design §5.7.0's objective (1) — never lose an authoritative fact — is the ONLY objective with no
        // acceptable failure rate, so it is what decides the blend's cut. The associative items here score
        // HIGHER than the authoritative one, which is the regime where a relevance-only cut would drop it.
        var loud = new RecordingEngine("project/loud", MemoryGrades.Associative)
            .Returning("loud1", MemoryGrade.Associative, 0.99).Returning("loud2", MemoryGrade.Associative, 0.98);
        var glossary = new RecordingEngine("project/glossary", MemoryGrades.Authoritative)
            .Returning("the exact fact", MemoryGrade.Authoritative, 0.10);

        var recall = await Composite(loud, glossary).RecallAsync(new MemoryQuery("t", "s", "q", Limit: 2));

        Assert.Equal(2, recall.Items.Count);
        Assert.Contains(recall.Items, i => i.Headline == "the exact fact");
        Assert.Equal(MemoryGrade.Authoritative, recall.Items[0].Grade);   // and it leads
    }

    [Fact]
    public async Task Expansion_routes_THROUGH_the_composite_to_the_owning_member()
    {
        // THE CAPABILITY-FORWARDING TEST. Decorating a generation provider erased its optional interfaces
        // once, and every video render stopped routing while every image render kept working and every
        // inline test stayed green. Without this test, the same regression ships invisibly here.
        var expandable = new ExpandableEngine("project/graph");
        var composite = Composite(new RecordingEngine("project/lexical", MemoryGrades.Associative), expandable);

        var expanded = await composite.ExpandAsync(new MemoryRef("project/graph", "42"));

        Assert.Single(expanded.Items);
        Assert.Equal("expanded 42", expanded.Items[0].Headline);
    }

    [Fact]
    public async Task Expanding_a_member_that_cannot_expand_fails_open()
    {
        var composite = Composite(
            new StaticEngine("project/lexical", [Assoc("project/lexical", "flat entry")]));

        var expanded = await composite.ExpandAsync(new MemoryRef("project/lexical", "flat entry"));

        Assert.Empty(expanded.Items); // no neighbours, and no throw
    }

    [Fact]
    public async Task Linking_through_a_member_that_cannot_link_throws()
    {
        var composite = Composite(new RecordingEngine("project/lexical", MemoryGrades.Associative));

        await Assert.ThrowsAsync<NotSupportedException>(() => composite.LinkAsync(
            new MemoryRef("project/lexical", "a"), new MemoryRef("project/lexical", "b")));
    }

    [Fact]
    public async Task A_reference_naming_no_member_throws_with_the_members_listed()
    {
        var composite = Composite(new RecordingEngine("project/lexical", MemoryGrades.Associative));

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            composite.ExpandAsync(new MemoryRef("project/nope", "1")));

        Assert.Contains("project/lexical", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reaping_routes_THROUGH_the_composite_to_every_member_that_can_reap()
    {
        // THE CAPABILITY-FORWARDING TEST'S MISSING TWIN, found 2026-08-15. The composite forwarded
        // IExpandableMemory and ILinkableMemory and silently dropped IForgettableMemory — so
        // `engine is IForgettableMemory` was FALSE for every AddMemoryEngine registration (Build is
        // documented "ALWAYS a composite, even for one member"), and a consumer of the shipped memory
        // subsystem had no supported way to reap anything. The engine is typed as IMemoryEngine here on
        // purpose: that is what IMemoryEngineFactory.Get hands back, so this is the consumer's own view.
        var a = new ForgettableEngine("project/a", pruneCount: 2);
        var b = new ForgettableEngine("project/b", pruneCount: 3);
        IMemoryEngine engine = Composite(a, b);

        var forgettable = Assert.IsAssignableFrom<IForgettableMemory>(engine);
        var reaped = await forgettable.PruneAsync("t", "s");

        Assert.Equal(5, reaped);                      // summed across members, not taken from the first
        Assert.Equal([("t", "s")], a.Prunes);
        Assert.Equal([("t", "s")], b.Prunes);
    }

    [Fact]
    public async Task A_blend_with_ONE_member_that_cannot_reap_refuses_and_removes_NOTHING()
    {
        // Round 2 of this review caught the first version fanning out to whichever members could reap and
        // SILENTLY SKIPPING the rest — the exact outcome the fan-out exists to prevent, and the first
        // version of this very test pinned it as correct by putting a non-forgettable member in the blend
        // and asserting success. `UseCurated("glossary").UseGraph()` is a blend from this library's own
        // README: it cleared the graph half, kept the AUTHORITATIVE half, and returned normally. For the
        // call an application makes when a user withdraws consent, that is the worst available answer.
        var graph = new ForgettableEngine("project/graph", pruneCount: 4);
        var glossary = new RecordingEngine("project/glossary", MemoryGrades.Authoritative);
        var forgettable = Assert.IsAssignableFrom<IForgettableMemory>(Composite(graph, glossary));

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => forgettable.ForgetAsync("t", "s"));

        Assert.Contains("project/glossary", ex.Message, StringComparison.Ordinal);
        Assert.Empty(graph.Forgets);   // and the CAPABLE member was not reaped either — checked BEFORE acting
    }

    [Fact]
    public async Task The_refusal_is_checked_before_anything_is_removed_on_the_prune_path_too()
    {
        var graph = new ForgettableEngine("project/graph", pruneCount: 4);
        var glossary = new RecordingEngine("project/glossary", MemoryGrades.Authoritative);
        var forgettable = Assert.IsAssignableFrom<IForgettableMemory>(Composite(graph, glossary));

        await Assert.ThrowsAsync<NotSupportedException>(() => forgettable.PruneAsync("t", "s"));

        Assert.Empty(graph.Prunes);    // a mid-fan-out refusal would be a partial reap AND an exception
    }

    [Fact]
    public async Task Forgetting_routes_THROUGH_the_composite_to_every_member_that_can_reap()
    {
        // ForgetAsync was on NO interface at all before 3.0 — a bare public method on GraphMemoryEngine —
        // so it was unreachable through any abstraction, composite or not.
        var a = new ForgettableEngine("project/a");
        var b = new ForgettableEngine("project/b");
        IMemoryEngine engine = Composite(a, b);

        await Assert.IsAssignableFrom<IForgettableMemory>(engine).ForgetAsync("t", "s");

        Assert.Equal([("t", "s")], a.Forgets);
        Assert.Equal([("t", "s")], b.Forgets);
    }

    [Fact]
    public async Task Reaping_a_blend_where_no_member_can_reap_throws_rather_than_reporting_zero()
    {
        // Fails LOUD, like LinkAsync and unlike ExpandAsync: PruneAsync returns a COUNT, and 0 already means
        // "nothing matched". Reporting 0 for "nothing here can ever reap" would make a delete that cannot
        // happen indistinguishable from one that found nothing to do.
        IMemoryEngine engine = Composite(new RecordingEngine("project/flat", MemoryGrades.Associative));
        var forgettable = Assert.IsAssignableFrom<IForgettableMemory>(engine);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => forgettable.PruneAsync("t", "s"));

        Assert.Contains("project/flat", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_blend_reports_the_abstention_signal_a_member_produced()
    {
        // Found 2026-08-15: the composite returned `new MemoryRecall(items, ran)`, dropping the third
        // positional argument, so MemoryRecall.Answered was NULL on every DI-registered engine even when a
        // judge had answered. docs/memory.md's own "know when the memory has nothing useful" sample tests
        // `recall.Answered == false`, which could never fire.
        var judged = new StaticEngine("project/graph", [Assoc("project/graph", "the answer")],
            MemorySources.Graph, answered: true);
        var plain = new StaticEngine("project/lexical", [Assoc("project/lexical", "unjudged")]);

        var recall = await Composite(plain, judged).RecallAsync(new MemoryQuery("t", "s", "q"));

        Assert.True(recall.Answered);
    }

    [Fact]
    public async Task A_blend_whose_only_judge_found_nothing_reports_false_not_null()
    {
        var judged = new StaticEngine("project/graph", [Assoc("project/graph", "off-topic")],
            MemorySources.Graph, answered: false);
        var plain = new StaticEngine("project/lexical", [Assoc("project/lexical", "unjudged")]);

        var recall = await Composite(plain, judged).RecallAsync(new MemoryQuery("t", "s", "q"));

        Assert.False(recall.Answered);
    }

    [Fact]
    public async Task A_blend_with_no_judge_anywhere_reports_null()
    {
        // The shipped default registers no verifier, so `false` must never be synthesised — a consumer
        // abstaining on `false` would otherwise abstain on everything.
        var recall = await Composite(
                new StaticEngine("project/lexical", [Assoc("project/lexical", "a")]),
                new StaticEngine("project/other", [Assoc("project/other", "b")]))
            .RecallAsync(new MemoryQuery("t", "s", "q"));

        Assert.Null(recall.Answered);
    }

    [Fact]
    public async Task A_blend_honours_the_query_char_budget()
    {
        // MemoryQuery.CharBudget travelled to every member unchanged and was never reconciled at the blend —
        // the same two-scopes defect the Limit cut above was added for, on the field beside it. Each member
        // may spend the whole budget, so an N-member blend returned up to N x CharBudget.
        var a = new RecordingEngine("project/a", MemoryGrades.Associative)
            .Returning(new string('a', 60), MemoryGrade.Associative, 0.9);
        var b = new RecordingEngine("project/b", MemoryGrades.Associative)
            .Returning(new string('b', 60), MemoryGrade.Associative, 0.8);

        var recall = await Composite(a, b).RecallAsync(new MemoryQuery("t", "s", "q", CharBudget: 100));

        Assert.Single(recall.Items);
    }

    [Fact]
    public async Task A_char_budget_never_drops_an_authoritative_fact_and_always_yields_one_item()
    {
        // Mirrors GraphMemoryEngine's own budget rule exactly: objective (1) has no acceptable failure rate,
        // and a caller asking for less than one fact gets one fact rather than nothing.
        var glossary = new RecordingEngine("project/glossary", MemoryGrades.Authoritative)
            .Returning(new string('x', 500), MemoryGrade.Authoritative, 0.1);
        var loud = new RecordingEngine("project/loud", MemoryGrades.Associative)
            .Returning(new string('y', 500), MemoryGrade.Associative, 0.99);

        var recall = await Composite(loud, glossary).RecallAsync(new MemoryQuery("t", "s", "q", CharBudget: 10));

        Assert.Contains(recall.Items, i => i.Grade == MemoryGrade.Authoritative);
        Assert.NotEmpty(recall.Items);
    }

    [Fact]
    public void Two_members_with_the_same_name_fail_at_construction()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Composite(
            new RecordingEngine("project/lexical", MemoryGrades.Associative),
            new RecordingEngine("project/lexical", MemoryGrades.Associative)));

        Assert.Contains("project/lexical", ex.Message, StringComparison.Ordinal);
    }
}
