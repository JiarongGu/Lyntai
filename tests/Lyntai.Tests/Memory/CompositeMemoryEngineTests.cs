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

    /// <summary>The reported defect, pinned as the DEFAULT so the fix below is measurable against it:
    /// <c>UseGraph().UseSemantic()</c> — the graph supports both grades, takes every write, and the semantic
    /// member's store stays permanently empty. Nothing throws, <c>Supported</c> widens, and only a
    /// recall-quality measurement can find it.</summary>
    [Fact]
    public async Task Routing_leaves_a_second_capable_member_with_nothing_written_to_it()
    {
        var graph = new RecordingEngine("project/graph", MemoryGrades.Associative | MemoryGrades.Authoritative);
        var semantic = new RecordingEngine("project/semantic", MemoryGrades.Associative);

        await Composite(graph, semantic).RememberAsync(new MemoryWrite("t", "s", "a fact"));

        Assert.Single(graph.Writes);
        Assert.Empty(semantic.Writes);
    }

    [Fact]
    public async Task Fanning_out_writes_to_every_member_that_can_hold_the_grade()
    {
        var graph = new RecordingEngine("project/graph", MemoryGrades.Associative | MemoryGrades.Authoritative);
        var semantic = new RecordingEngine("project/semantic", MemoryGrades.Associative);
        var composite = new CompositeMemoryEngine("project", [graph, semantic])
        {
            WriteRouting = MemoryWriteRouting.EveryCapable,
        };

        var reference = await composite.RememberAsync(
            new MemoryWrite("t", "s", "a fact", Grade: MemoryGrade.Associative));

        Assert.Single(graph.Writes);
        Assert.Single(semantic.Writes);
        Assert.Equal("project/graph", reference.Engine);   // the FIRST written member's reference
    }

    /// <summary>Fanning out never widens the grade: a member that cannot hold the write is still skipped, so
    /// an authoritative fact is not quietly duplicated into an associative store that would decay it.</summary>
    [Fact]
    public async Task Fanning_out_still_skips_a_member_that_cannot_hold_the_grade()
    {
        var lexical = new RecordingEngine("project/lexical", MemoryGrades.Associative);
        var glossary = new RecordingEngine("project/glossary", MemoryGrades.Authoritative);
        var composite = new CompositeMemoryEngine("project", [lexical, glossary])
        {
            WriteRouting = MemoryWriteRouting.EveryCapable,
        };

        await composite.RememberAsync(new MemoryWrite("t", "s", "exact", Grade: MemoryGrade.Authoritative));

        Assert.Empty(lexical.Writes);
        Assert.Single(glossary.Writes);
    }

    /// <summary>An <see cref="MemoryGrade.Inherit"/> write is every member's business under fan-out — each
    /// resolves it at its own role. That is also why fan-out is opt-in: in a blend mixing roles the same fact
    /// lands at BOTH grades, which is a cost a caller has to choose.</summary>
    [Fact]
    public async Task Fanning_out_sends_an_inherit_write_to_every_member()
    {
        var lexical = new RecordingEngine("project/lexical", MemoryGrades.Associative);
        var glossary = new RecordingEngine("project/glossary", MemoryGrades.Authoritative);
        var composite = new CompositeMemoryEngine("project", [lexical, glossary])
        {
            WriteRouting = MemoryWriteRouting.EveryCapable,
        };

        await composite.RememberAsync(new MemoryWrite("t", "s", "unresolved"));

        Assert.Single(lexical.Writes);
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
    public async Task Removal_routes_THROUGH_the_composite_to_every_member_that_can_remove()
    {
        // THE CAPABILITY-FORWARDING TEST'S MISSING TWIN, found 2026-08-15. The composite forwarded
        // IExpandableMemory and ILinkableMemory and silently dropped IForgettableMemory — so
        // `engine is IForgettableMemory` was FALSE for every AddMemoryEngine registration (Build is
        // documented "ALWAYS a composite, even for one member"), and a consumer of the shipped memory
        // subsystem had no supported way to remove anything. The engine is typed as IMemoryEngine here on
        // purpose: that is what IMemoryEngineFactory.Get hands back, so this is the consumer's own view.
        var a = new ForgettableEngine("project/a", pruneCount: 2);
        var b = new ForgettableEngine("project/b", pruneCount: 3);
        IMemoryEngine engine = Composite(a, b);

        var prunable = Assert.IsAssignableFrom<IPrunableMemory>(engine);
        var removed = await prunable.PruneAsync("t", "s");

        Assert.Equal(5, removed);                      // summed across members, not taken from the first
        Assert.Equal([("t", "s")], a.Prunes);
        Assert.Equal([("t", "s")], b.Prunes);
    }

    [Fact]
    public async Task A_blend_with_ONE_member_that_cannot_remove_refuses_and_removes_NOTHING()
    {
        // Round 2 of this review caught the first version fanning out to whichever members could remove and
        // SILENTLY SKIPPING the rest — the exact outcome the fan-out exists to prevent, and the first
        // version of this very test pinned it as correct by putting a non-forgettable member in the blend
        // and asserting success. `UseCurated("glossary").UseGraph()` is a blend from this library's own
        // README: it cleared the graph half, kept the AUTHORITATIVE half, and returned normally. For the
        // call an application makes when a user withdraws consent, that is the worst available answer.
        var graph = new ForgettableEngine("project/graph", pruneCount: 4);
        var gap = new RecordingEngine("project/gap", MemoryGrades.Associative);   // a GAP, not a catalogue
        var forgettable = Assert.IsAssignableFrom<IForgettableMemory>(Composite(graph, gap));

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => forgettable.ForgetAsync("t", "s"));

        Assert.Contains("project/gap", ex.Message, StringComparison.Ordinal);
        Assert.Empty(graph.Forgets);   // and the CAPABLE member was not removed either — checked BEFORE acting
    }

    [Fact]
    public async Task The_refusal_is_checked_before_anything_is_removed_on_the_prune_path_too()
    {
        var graph = new ForgettableEngine("project/graph", pruneCount: 4);
        var gap = new RecordingEngine("project/gap", MemoryGrades.Associative);   // a GAP, not a catalogue
        var prunable = Assert.IsAssignableFrom<IPrunableMemory>(Composite(graph, gap));

        await Assert.ThrowsAsync<NotSupportedException>(() => prunable.PruneAsync("t", "s"));

        Assert.Empty(graph.Prunes);    // a mid-fan-out refusal would be a partial remove AND an exception
    }

    // ---- operator-authored members are SKIPPED, not blockers (3.0) -----------------------------------
    //
    // The distinction the composite now draws: a member that CANNOT remove is a gap and refuses the whole
    // verb; a member that declares its content OPERATOR-authored is out of scope and is skipped. Collapsing
    // them would turn every gap into a silent partial, which is what D63 was written about — so both
    // directions are pinned here.

    private static RecordingEngine Glossary(string name = "project/glossary") =>
        new(name, MemoryGrades.Authoritative);

    [Fact]
    public async Task A_glossary_member_no_longer_BLOCKS_a_removal_it_is_skipped()
    {
        // THE BLEND FROM THIS LIBRARY'S OWN README: UseCurated("glossary").UseGraph(). Until now it could
        // not remove at all — the curated member cannot forget, so the whole verb refused, and an application
        // withdrawing a user's consent had nothing to call.
        var graph = new ForgettableEngine("project/graph", pruneCount: 4);
        var engine = Composite(Glossary(), graph);

        await Assert.IsAssignableFrom<IForgettableMemory>(engine).ForgetAsync("t", "s");
        var removed = await Assert.IsAssignableFrom<IPrunableMemory>(engine).PruneAsync("t", "s");

        Assert.Equal([("t", "s")], graph.Forgets);   // the USER's data went
        Assert.Equal(4, removed);
    }

    [Fact]
    public async Task A_member_that_holds_user_content_and_cannot_remove_STILL_refuses()
    {
        // The other direction, and the one that must not regress: skipping is earned by DECLARING the
        // content operator-authored, never by failing to implement the capability.
        var graph = new ForgettableEngine("project/graph", pruneCount: 4);
        var gap = new RecordingEngine("project/gap", MemoryGrades.Associative);   // in scope by default
        var engine = Composite(gap, graph);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => Assert.IsAssignableFrom<IForgettableMemory>(engine).ForgetAsync("t", "s"));

        Assert.Contains("project/gap", ex.Message, StringComparison.Ordinal);
        Assert.Empty(graph.Forgets);   // still checked BEFORE anything is removed
    }

    [Fact]
    public async Task A_blend_of_ONLY_operator_authored_members_removes_nothing_and_does_not_throw()
    {
        // Nothing here is the user's, so there is nothing to withdraw — that is a legitimate zero, not the
        // "cannot" the refusal exists to distinguish it from.
        var engine = Composite(Glossary("project/a"), Glossary("project/b"));

        await Assert.IsAssignableFrom<IForgettableMemory>(engine).ForgetAsync("t", "s");
        Assert.Equal(0, await Assert.IsAssignableFrom<IPrunableMemory>(engine).PruneAsync("t", "s"));
    }

    [Fact]
    public async Task The_HOST_decides_eligibility_and_can_differ_PER_KIND()
    {
        // WHY THIS IS A POLICY AND NOT A PROPERTY OF AN ENGINE. Whether a curated section holds material a
        // user may withdraw is a fact about the DEPLOYMENT, not about the type: one application's glossary is
        // operator boilerplate, another's holds preferences the user typed. And the two verbs can legitimately
        // differ — keep the glossary out of an automatic prune, include it in an explicit consent withdrawal —
        // which no single boolean on the engine could have expressed.
        var glossary = new ForgettableEngine("project/glossary") { };
        var graph = new ForgettableEngine("project/graph", pruneCount: 4);
        var engine = new CompositeMemoryEngine("project", [glossary, graph],
            removalPolicy: new ForgetOnlyForAuthoritativePolicy(glossary.Name));

        await Assert.IsAssignableFrom<IForgettableMemory>(engine).ForgetAsync("t", "s");
        await Assert.IsAssignableFrom<IPrunableMemory>(engine).PruneAsync("t", "s");

        Assert.Equal([("t", "s")], glossary.Forgets);   // included in the withdrawal…
        Assert.Empty(glossary.Prunes);                  // …and excluded from the capacity sweep
        Assert.Equal([("t", "s")], graph.Forgets);
        Assert.Equal([("t", "s")], graph.Prunes);
    }

    /// <summary>A host policy: one named member is in scope for a forget and out of scope for a prune.</summary>
    private sealed class ForgetOnlyForAuthoritativePolicy(string member) : IMemoryRemovalPolicy
    {
        public bool Includes(IMemoryEngine candidate, MemoryRemovalKind kind) =>
            !string.Equals(candidate.Name, member, StringComparison.Ordinal) || kind == MemoryRemovalKind.Forget;
    }

    [Fact]
    public async Task A_policy_may_not_configure_a_genuine_GAP_away()
    {
        // The line between eligibility and capability. A policy that INCLUDES a member which cannot serve the
        // verb still gets the loud refusal — otherwise a host could silence the very failure D63 exists for
        // by writing one permissive policy, and a partial remove would report success.
        var graph = new ForgettableEngine("project/graph", pruneCount: 4);
        var gap = new RecordingEngine("project/gap", MemoryGrades.Authoritative);   // cannot remove at all
        var engine = new CompositeMemoryEngine("project", [gap, graph],
            removalPolicy: new IncludeEverythingPolicy());

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => Assert.IsAssignableFrom<IForgettableMemory>(engine).ForgetAsync("t", "s"));

        Assert.Contains("project/gap", ex.Message, StringComparison.Ordinal);
        Assert.Empty(graph.Forgets);
    }

    private sealed class IncludeEverythingPolicy : IMemoryRemovalPolicy
    {
        public bool Includes(IMemoryEngine candidate, MemoryRemovalKind kind) => true;
    }

    [Fact]
    public void The_default_policy_keys_on_the_GRADES_an_engine_declares_not_on_its_type()
    {
        // Keying on the concrete type would be a conditional that must be edited to add a backend, and would
        // miss a BYO catalogue entirely. Authoritative-ONLY is a curated catalogue by construction — that is
        // what the grade split means — so the default reads a property every engine already declares.
        var policy = new DefaultMemoryRemovalPolicy();
        var catalogue = new RecordingEngine("byo/catalogue", MemoryGrades.Authoritative);
        var mixed = new RecordingEngine("byo/graph", MemoryGrades.Associative | MemoryGrades.Authoritative);
        var associative = new RecordingEngine("byo/lexical", MemoryGrades.Associative);

        foreach (var kind in new[] { MemoryRemovalKind.Forget, MemoryRemovalKind.Prune })
        {
            Assert.False(policy.Includes(catalogue, kind));   // a BYO catalogue, never named in any switch
            Assert.True(policy.Includes(mixed, kind));
            Assert.True(policy.Includes(associative, kind));
        }
    }

    [Fact]
    public async Task A_member_may_be_forgettable_WITHOUT_being_prunable()
    {
        // Why the capabilities split. A vector store can forget a scope exactly and cannot prune by age at
        // all; under one combined interface it had to claim both or neither. The pre-flight now asks for the
        // capability the VERB needs, so forgetting works and pruning refuses — rather than pruning
        // half-succeeding and then throwing from a member mid-fan-out.
        var graph = new ForgettableEngine("project/graph", pruneCount: 4);
        var forgetOnly = new ForgetOnlyEngine("project/vectors");
        var engine = Composite(forgetOnly, graph);

        await Assert.IsAssignableFrom<IForgettableMemory>(engine).ForgetAsync("t", "s");
        Assert.Equal([("t", "s")], forgetOnly.Forgets);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => Assert.IsAssignableFrom<IPrunableMemory>(engine).PruneAsync("t", "s"));
        Assert.Contains("project/vectors", ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IPrunableMemory), ex.Message, StringComparison.Ordinal);
        Assert.Empty(graph.Prunes);
    }

    [Fact]
    public async Task Forgetting_routes_THROUGH_the_composite_to_every_member_that_can_remove()
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
    public async Task Removing_a_blend_where_no_member_can_remove_throws_rather_than_reporting_zero()
    {
        // Fails LOUD, like LinkAsync and unlike ExpandAsync: PruneAsync returns a COUNT, and 0 already means
        // "nothing matched". Reporting 0 for "nothing here can ever remove" would make a delete that cannot
        // happen indistinguishable from one that found nothing to do.
        IMemoryEngine engine = Composite(new RecordingEngine("project/flat", MemoryGrades.Associative));
        var prunable = Assert.IsAssignableFrom<IPrunableMemory>(engine);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => prunable.PruneAsync("t", "s"));

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

    /// <summary><b>The capability-forwarding invariant, as a FACT rather than a sentence.</b>
    ///
    /// <para>D63's remedy for the composite silently dropping <see cref="IForgettableMemory"/> was written as
    /// prose — "when a capability interface is added anywhere in this library, the wrapper over it gets a
    /// line in the same change" — which is another sentence in another document, and D63's own diagnosis of
    /// the original defect was that <b>a comment asserting an invariant is not the invariant</b>. The class
    /// docblock had claimed "It never guesses about capabilities" while implementing two of three.</para>
    ///
    /// <para>Derived from the tree rather than hand-listed, so it cannot go stale: every interface
    /// <see cref="Lyntai.Memory.Engines.GraphMemoryEngine"/> implements, the composite must implement too.
    /// The graph engine is the right yardstick because it is the richest member and the one every
    /// <c>UseGraph</c> blend wraps — add a fourth capability to it and this fails until the wrapper forwards
    /// it, which is precisely the change that shipped un-forwarded before.</para></summary>
    [Fact]
    public void The_composite_implements_every_capability_interface_its_richest_member_does()
    {
        var memberCapabilities = typeof(Lyntai.Memory.Engines.GraphMemoryEngine).GetInterfaces();
        var compositeCapabilities = typeof(CompositeMemoryEngine).GetInterfaces();

        var dropped = memberCapabilities.Except(compositeCapabilities).Select(t => t.Name).Order().ToList();

        Assert.True(dropped.Count == 0,
            $"CompositeMemoryEngine does not implement {string.Join(", ", dropped)} — so a consumer holding " +
            "the IMemoryEngine that IMemoryEngineFactory hands back cannot reach it, because " +
            "MemoryEngineBuilder.Build returns a composite for EVERY registration. Forward it, or the " +
            "capability ships unreachable (docs/DECISIONS.md D63).");
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
