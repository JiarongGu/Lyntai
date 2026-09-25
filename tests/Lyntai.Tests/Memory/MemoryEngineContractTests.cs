using Lyntai.Memory;
using Lyntai.Memory.Engines;

namespace Lyntai.Tests.Memory;

/// <summary>Every <see cref="MemoryEngineContract"/> fact, inherited: a suite derives this, builds its engine
/// and declares what that engine can carry, so every fact runs on every engine BY CONSTRUCTION — the shape
/// <see cref="MemoryAgePolicyContractFacts"/> uses. Each fact is namespaced by its own key, so engines
/// sharing state stay isolated.</summary>
public abstract class MemoryEngineContractFacts
{
    /// <summary>A fresh engine under test.</summary>
    protected abstract IMemoryEngine New();

    /// <summary>Whether this engine's store can carry <see cref="MemoryWrite.Metadata"/> back to a read.</summary>
    protected abstract bool CarriesMetadata { get; }

    /// <summary>Whether expanding an entry this engine wrote returns that entry.</summary>
    protected virtual bool Expands => false;

    [Fact] public Task Remember_then_recall() => MemoryEngineContract.Remember_then_recall_finds_it(New(), "k1");
    [Fact] public Task Carries_name() => MemoryEngineContract.Every_item_carries_this_engines_name(New(), "k2");
    [Fact] public Task Reports_tier() => MemoryEngineContract.Recall_reports_the_tier_that_ran(New(), "k3");
    [Fact] public Task Refuses_grade() => MemoryEngineContract.An_unsupported_grade_throws_rather_than_downgrading(New(), "k4");
    [Fact] public Task Resolves_grade() => MemoryEngineContract.An_inherited_grade_resolves_and_is_never_returned_as_Inherit(New(), "k5");
    [Fact] public Task Authoritative_full() => MemoryEngineContract.Authoritative_items_always_carry_full_content(New(), "k6");
    [Fact] public Task Full_detail() => MemoryEngineContract.Full_detail_returns_content_on_every_item(New(), "k14");
    [Fact] public Task Empty_query() => MemoryEngineContract.An_empty_query_does_not_throw(New(), "k7");
    [Fact] public Task Cancellation() => MemoryEngineContract.Cancellation_propagates(New(), "k8");
    [Fact] public Task Honours_limit() => MemoryEngineContract.A_recall_returns_at_most_the_limit(New(), "k9");

    [Fact]
    public Task Metadata_round_trip() =>
        MemoryEngineContract.Metadata_written_is_returned_or_explicitly_absent(New(), "k10", CarriesMetadata);

    [Fact]
    public Task Metadata_on_expansion() =>
        MemoryEngineContract.Metadata_survives_an_EXPANSION_not_only_a_recall(New(), "k12", CarriesMetadata, Expands);

    [Fact] public Task Walks() => MemoryEngineContract.A_walk_yields_at_least_one_step_and_never_throws(New(), "k13");
}

public class LexicalEngineContractTests : MemoryEngineContractFacts
{
    protected override IMemoryEngine New() => new LexicalMemoryEngine("lex", new FakeMemoryStore());

    // MemoryEntry has no metadata column
    protected override bool CarriesMetadata => false;
}

public class SemanticEngineContractTests : MemoryEngineContractFacts
{
    protected override IMemoryEngine New() => new SemanticMemoryEngine("sem", new FakeSemanticMemory());

    // a vector hit carries content and a score, nothing else
    protected override bool CarriesMetadata => false;
}

public class GraphEngineContractTests : MemoryEngineContractFacts
{
    protected override IMemoryEngine New() =>
        new GraphMemoryEngine("graph", new Lyntai.Storage.InMemory.InMemoryMemoryGraphStore());

    // GraphNode.Metadata is persisted and already returned by the store
    protected override bool CarriesMetadata => true;

    // the ONE engine that genuinely expands — the site a recall-only fact could not see — and therefore
    // the one whose walk takes more than a single step
    protected override bool Expands => true;
}

public class CompositeEngineContractTests : MemoryEngineContractFacts
{
    protected override IMemoryEngine New() => new CompositeMemoryEngine("blend",
    [
        new LexicalMemoryEngine("blend/lex", new FakeMemoryStore()),
        new CuratedMemoryEngine("blend/cur", new FakeCuratedStore(), kind: "glossary"),
    ]);

    // an Inherit write routes to the FIRST member, which is lexical here; a composite always implements
    // IExpandableMemory, and a lexical owner makes it fail OPEN
    protected override bool CarriesMetadata => false;

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

public class CuratedEngineContractTests : MemoryEngineContractFacts
{
    protected override IMemoryEngine New() => new CuratedMemoryEngine("cur", new FakeCuratedStore(), kind: "glossary");

    // CuratedMemory.Metadata is persisted
    protected override bool CarriesMetadata => true;

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
