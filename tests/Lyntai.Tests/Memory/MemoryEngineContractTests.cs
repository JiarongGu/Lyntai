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
}
