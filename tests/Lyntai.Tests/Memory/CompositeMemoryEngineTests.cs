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
    public void Two_members_with_the_same_name_fail_at_construction()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Composite(
            new RecordingEngine("project/lexical", MemoryGrades.Associative),
            new RecordingEngine("project/lexical", MemoryGrades.Associative)));

        Assert.Contains("project/lexical", ex.Message, StringComparison.Ordinal);
    }
}
