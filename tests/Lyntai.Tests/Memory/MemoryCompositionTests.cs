using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

public class MemoryCompositionTests
{
    private static IMemoryEngine EngineWith(params MemoryItem[] items) => new StaticEngine("e", items);

    private static MemoryItem Item(string text, MemoryGrade grade) =>
        new(new MemoryRef("e", text), text, text, grade, 1, 1, 0);

    [Fact]
    public async Task It_renders_the_two_grades_as_separate_labelled_sections()
    {
        var engine = EngineWith(
            Item("the build gate is dev.mjs verify", MemoryGrade.Authoritative),
            Item("user prefers terse commit messages", MemoryGrade.Associative));

        var composed = await engine.ComposeAsync("BASE", new MemoryQuery("t", "s", "q"));

        Assert.Contains("## Known facts (authoritative)", composed, StringComparison.Ordinal);
        Assert.Contains("## Recalled context (associative", composed, StringComparison.Ordinal);
        Assert.True(
            composed.IndexOf("## Known facts", StringComparison.Ordinal)
            < composed.IndexOf("## Recalled context", StringComparison.Ordinal),
            "authoritative material must render first");
    }

    [Fact]
    public async Task Associative_noise_cannot_crowd_out_an_authoritative_fact()
    {
        // THE ACCURACY TEST. 200 high-relevance associative items against a tiny budget: the one exact
        // fact must survive, verbatim, or this design is worse than the flat dump it replaced.
        var items = new List<MemoryItem> { Item("the build gate is dev.mjs verify", MemoryGrade.Authoritative) };
        for (var i = 0; i < 200; i++)
            items.Add(Item($"noise item number {i} which is quite wordy indeed", MemoryGrade.Associative));

        var composed = await EngineWith([.. items]).ComposeAsync("BASE", new MemoryQuery("t", "s", "q"),
            new MemoryCompositionOptions { Budget = 300, AuthoritativeReserve = 100 });

        Assert.Contains("the build gate is dev.mjs verify", composed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authoritative_material_that_does_not_fit_is_reported_not_dropped_silently()
    {
        var items = Enumerable.Range(0, 20)
            .Select(i => Item($"exact fact number {i} stated at some length", MemoryGrade.Authoritative))
            .ToArray();

        var composed = await EngineWith(items).ComposeAsync("BASE", new MemoryQuery("t", "s", "q"),
            new MemoryCompositionOptions { Budget = 200, AuthoritativeReserve = 200 });

        Assert.Matches(@"… \d+ further authoritative facts omitted \(budget\)", composed);
    }

    [Fact]
    public async Task The_authoritative_section_is_byte_identical_across_repeated_recalls()
    {
        var engine = EngineWith(
            Item("alpha is exact", MemoryGrade.Authoritative),
            Item("beta is exact", MemoryGrade.Authoritative),
            Item("gamma is recalled", MemoryGrade.Associative));

        var first = await engine.ComposeAsync("BASE", new MemoryQuery("t", "s", "q"));
        var second = await engine.ComposeAsync("BASE", new MemoryQuery("t", "s", "q"));

        Assert.Equal(Section(first), Section(second));

        static string Section(string composed)
        {
            var start = composed.IndexOf("## Known facts", StringComparison.Ordinal);
            var end = composed.IndexOf("## Recalled context", StringComparison.Ordinal);
            return end < 0 ? composed[start..] : composed[start..end];
        }
    }

    [Fact]
    public async Task An_oversized_item_does_not_hide_the_shorter_ones_behind_it()
    {
        var engine = EngineWith(
            Item(new string('x', 500), MemoryGrade.Associative),
            Item("short and useful", MemoryGrade.Associative));

        var composed = await engine.ComposeAsync("BASE", new MemoryQuery("t", "s", "q"),
            new MemoryCompositionOptions { Budget = 100, AuthoritativeReserve = 0 });

        Assert.Contains("short and useful", composed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_recall_returns_the_base_prompt_unchanged()
    {
        var composed = await EngineWith().ComposeAsync("BASE", new MemoryQuery("t", "s", "q"));

        Assert.Equal("BASE", composed);
    }

    [Fact]
    public async Task A_faulting_engine_returns_the_base_prompt_rather_than_throwing()
    {
        var composed = await new FaultingEngine("e").ComposeAsync("BASE", new MemoryQuery("t", "s", "q"));

        Assert.Equal("BASE", composed);
    }

    [Fact]
    public async Task Cancellation_still_propagates_through_composition()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            EngineWith(Item("anything", MemoryGrade.Associative))
                .ComposeAsync("BASE", new MemoryQuery("t", "s", "q"), ct: cts.Token));
    }
}
