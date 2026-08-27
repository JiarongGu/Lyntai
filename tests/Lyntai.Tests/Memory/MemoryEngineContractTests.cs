using Lyntai.Memory;
using Lyntai.Memory.Engines;

namespace Lyntai.Tests.Memory;

public class LexicalEngineContractTests
{
    private static IMemoryEngine New() => new LexicalMemoryEngine("lex", new FakeMemoryStore());

    [Fact] public Task Remember_then_recall() => MemoryEngineContract.Remember_then_recall_finds_it(New(), "k1");
    [Fact] public Task Carries_name() => MemoryEngineContract.Every_item_carries_this_engines_name(New(), "k2");
    [Fact] public Task Reports_tier() => MemoryEngineContract.Recall_reports_the_tier_that_ran(New(), "k3");
    [Fact] public Task Refuses_grade() => MemoryEngineContract.An_unsupported_grade_throws_rather_than_downgrading(New(), "k4");
    [Fact] public Task Resolves_grade() => MemoryEngineContract.An_inherited_grade_resolves_and_is_never_returned_as_Inherit(New(), "k5");
    [Fact] public Task Authoritative_full() => MemoryEngineContract.Authoritative_items_always_carry_full_content(New(), "k6");
    [Fact] public Task Empty_query() => MemoryEngineContract.An_empty_query_does_not_throw(New(), "k7");
    [Fact] public Task Cancellation() => MemoryEngineContract.Cancellation_propagates(New(), "k8");
    [Fact] public Task Honours_limit() => MemoryEngineContract.A_recall_returns_at_most_the_limit(New(), "k9");
    // MemoryEntry has no metadata column
    [Fact] public Task Metadata_round_trip() =>
        MemoryEngineContract.Metadata_written_is_returned_or_explicitly_absent(New(), "k10", carries: false);
}

public class SemanticEngineContractTests
{
    private static IMemoryEngine New() => new SemanticMemoryEngine("sem", new FakeSemanticMemory());

    [Fact] public Task Remember_then_recall() => MemoryEngineContract.Remember_then_recall_finds_it(New(), "k1");
    [Fact] public Task Carries_name() => MemoryEngineContract.Every_item_carries_this_engines_name(New(), "k2");
    [Fact] public Task Reports_tier() => MemoryEngineContract.Recall_reports_the_tier_that_ran(New(), "k3");
    [Fact] public Task Refuses_grade() => MemoryEngineContract.An_unsupported_grade_throws_rather_than_downgrading(New(), "k4");
    [Fact] public Task Resolves_grade() => MemoryEngineContract.An_inherited_grade_resolves_and_is_never_returned_as_Inherit(New(), "k5");
    [Fact] public Task Authoritative_full() => MemoryEngineContract.Authoritative_items_always_carry_full_content(New(), "k6");
    [Fact] public Task Empty_query() => MemoryEngineContract.An_empty_query_does_not_throw(New(), "k7");
    [Fact] public Task Cancellation() => MemoryEngineContract.Cancellation_propagates(New(), "k8");
    [Fact] public Task Honours_limit() => MemoryEngineContract.A_recall_returns_at_most_the_limit(New(), "k9");
    // a vector hit carries content and a score, nothing else
    [Fact] public Task Metadata_round_trip() =>
        MemoryEngineContract.Metadata_written_is_returned_or_explicitly_absent(New(), "k10", carries: false);
}

public class GraphEngineContractTests
{
    private static IMemoryEngine New() =>
        new GraphMemoryEngine("graph", new Lyntai.Storage.InMemory.InMemoryMemoryGraphStore());

    [Fact] public Task Remember_then_recall() => MemoryEngineContract.Remember_then_recall_finds_it(New(), "k1");
    [Fact] public Task Carries_name() => MemoryEngineContract.Every_item_carries_this_engines_name(New(), "k2");
    [Fact] public Task Reports_tier() => MemoryEngineContract.Recall_reports_the_tier_that_ran(New(), "k3");
    [Fact] public Task Refuses_grade() => MemoryEngineContract.An_unsupported_grade_throws_rather_than_downgrading(New(), "k4");
    [Fact] public Task Resolves_grade() => MemoryEngineContract.An_inherited_grade_resolves_and_is_never_returned_as_Inherit(New(), "k5");
    [Fact] public Task Authoritative_full() => MemoryEngineContract.Authoritative_items_always_carry_full_content(New(), "k6");
    [Fact] public Task Empty_query() => MemoryEngineContract.An_empty_query_does_not_throw(New(), "k7");
    [Fact] public Task Cancellation() => MemoryEngineContract.Cancellation_propagates(New(), "k8");
    [Fact] public Task Honours_limit() => MemoryEngineContract.A_recall_returns_at_most_the_limit(New(), "k9");
    // GraphNode.Metadata is persisted and already returned by the store
    [Fact] public Task Metadata_round_trip() =>
        MemoryEngineContract.Metadata_written_is_returned_or_explicitly_absent(New(), "k10", carries: true);
}

public class CompositeEngineContractTests
{
    private static IMemoryEngine New() => new CompositeMemoryEngine("blend",
    [
        new LexicalMemoryEngine("blend/lex", new FakeMemoryStore()),
        new CuratedMemoryEngine("blend/cur", new FakeCuratedStore(), kind: "glossary"),
    ]);

    [Fact] public Task Remember_then_recall() => MemoryEngineContract.Remember_then_recall_finds_it(New(), "k1");
    [Fact] public Task Carries_name() => MemoryEngineContract.Every_item_carries_this_engines_name(New(), "k2");
    [Fact] public Task Reports_tier() => MemoryEngineContract.Recall_reports_the_tier_that_ran(New(), "k3");
    [Fact] public Task Refuses_grade() => MemoryEngineContract.An_unsupported_grade_throws_rather_than_downgrading(New(), "k4");
    [Fact] public Task Resolves_grade() => MemoryEngineContract.An_inherited_grade_resolves_and_is_never_returned_as_Inherit(New(), "k5");
    [Fact] public Task Authoritative_full() => MemoryEngineContract.Authoritative_items_always_carry_full_content(New(), "k6");
    [Fact] public Task Empty_query() => MemoryEngineContract.An_empty_query_does_not_throw(New(), "k7");
    [Fact] public Task Cancellation() => MemoryEngineContract.Cancellation_propagates(New(), "k8");
    [Fact] public Task Honours_limit() => MemoryEngineContract.A_recall_returns_at_most_the_limit(New(), "k9");
    // an Inherit write routes to the FIRST member, which is lexical here
    [Fact] public Task Metadata_round_trip() =>
        MemoryEngineContract.Metadata_written_is_returned_or_explicitly_absent(New(), "k10", carries: false);

    [Fact]
    public async Task A_blend_does_not_STRIP_metadata_from_a_member_that_carries_it()
    {
        // The contract fact above asserts null for this blend, which is right and is NOT this property: it
        // holds because the first member is lexical, so it would pass just as well if the composite discarded
        // metadata outright. A composite today re-uses the member's MemoryItem instance rather than rebuilding
        // it, so this survives by construction — and "by construction" is precisely what a later refactor
        // that maps items (to renormalize Relevance, say) would quietly undo, with every other fact green.
        var blend = new CompositeMemoryEngine("blend",
        [
            new CuratedMemoryEngine("blend/cur", new FakeCuratedStore(), kind: "glossary"),
            new LexicalMemoryEngine("blend/lex", new FakeMemoryStore()),
        ]);

        await MemoryEngineContract.Metadata_written_is_returned_or_explicitly_absent(blend, "k11", carries: true);
    }
}

public class CuratedEngineContractTests
{
    private static IMemoryEngine New() => new CuratedMemoryEngine("cur", new FakeCuratedStore(), kind: "glossary");

    [Fact] public Task Remember_then_recall() => MemoryEngineContract.Remember_then_recall_finds_it(New(), "k1");
    [Fact] public Task Carries_name() => MemoryEngineContract.Every_item_carries_this_engines_name(New(), "k2");
    [Fact] public Task Reports_tier() => MemoryEngineContract.Recall_reports_the_tier_that_ran(New(), "k3");
    [Fact] public Task Refuses_grade() => MemoryEngineContract.An_unsupported_grade_throws_rather_than_downgrading(New(), "k4");
    [Fact] public Task Resolves_grade() => MemoryEngineContract.An_inherited_grade_resolves_and_is_never_returned_as_Inherit(New(), "k5");
    [Fact] public Task Authoritative_full() => MemoryEngineContract.Authoritative_items_always_carry_full_content(New(), "k6");
    [Fact] public Task Empty_query() => MemoryEngineContract.An_empty_query_does_not_throw(New(), "k7");
    [Fact] public Task Cancellation() => MemoryEngineContract.Cancellation_propagates(New(), "k8");
    [Fact] public Task Honours_limit() => MemoryEngineContract.A_recall_returns_at_most_the_limit(New(), "k9");
    // CuratedMemory.Metadata is persisted
    [Fact] public Task Metadata_round_trip() =>
        MemoryEngineContract.Metadata_written_is_returned_or_explicitly_absent(New(), "k10", carries: true);

    [Fact]
    public async Task A_query_less_recall_returns_only_this_engines_kind_and_honours_the_limit()
    {
        // Found 2026-08-14. The query-less branch calls ForCompositionAsync, which takes NEITHER kind NOR
        // limit — while the SearchAsync branch one line below passes both. So a blend of two curated engines
        // over one catalog had each member return the WHOLE catalog, every section, unbounded, and every item
        // graded Authoritative: each fact came back once per member, and the duplicates consumed the
        // authoritative reserve that objective (1) exists to protect.
        var store = new FakeCuratedStore();
        var glossary = new CuratedMemoryEngine("cur/glossary", store, kind: "glossary");
        var style = new CuratedMemoryEngine("cur/style", store, kind: "style");

        await glossary.RememberAsync(new MemoryWrite("t", "s", "a glossary fact", Grade: MemoryGrade.Authoritative));
        await glossary.RememberAsync(new MemoryWrite("t", "s", "another glossary fact", Grade: MemoryGrade.Authoritative));
        await style.RememberAsync(new MemoryWrite("t", "s", "a style rule", Grade: MemoryGrade.Authoritative));

        var all = await glossary.RecallAsync(new MemoryQuery("t", "s", null));
        Assert.NotEmpty(all.Items);
        Assert.All(all.Items, i => Assert.DoesNotContain("style rule", i.Content ?? "", StringComparison.Ordinal));

        var capped = await glossary.RecallAsync(new MemoryQuery("t", "s", null, Limit: 1));
        Assert.Single(capped.Items);
    }
}
