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
            new MemoryCompositionOptions { Budget = 300, AuthoritativeCharacters = 100 });

        Assert.Contains("the build gate is dev.mjs verify", composed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authoritative_material_that_does_not_fit_is_reported_not_dropped_silently()
    {
        var items = Enumerable.Range(0, 20)
            .Select(i => Item($"exact fact number {i} stated at some length", MemoryGrade.Authoritative))
            .ToArray();

        var composed = await EngineWith(items).ComposeAsync("BASE", new MemoryQuery("t", "s", "q"),
            new MemoryCompositionOptions { Budget = 200, AuthoritativeCharacters = 200 });

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
            new MemoryCompositionOptions { Budget = 100, AuthoritativeCharacters = 0 });

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

    // ---- Render: the formatting half, reachable without an engine -------------------------------------

    /// <summary>The door an adopter with its OWN retrieval needs. Reported on 3.0.0: reaching this rendering
    /// meant implementing <see cref="IMemoryEngine"/> whose recall returned material the caller had already
    /// chosen — an engine written only to reach a formatter.</summary>
    [Fact]
    public void Render_composes_material_a_caller_selected_itself_with_no_engine_involved()
    {
        var composed = MemoryComposition.Render("BASE",
        [
            Item("the build gate is dev.mjs verify", MemoryGrade.Authoritative),
            Item("user prefers terse commit messages", MemoryGrade.Associative),
        ]);

        Assert.Contains("## Known facts (authoritative)", composed, StringComparison.Ordinal);
        Assert.Contains("the build gate is dev.mjs verify", composed, StringComparison.Ordinal);
        Assert.Contains("## Recalled context (associative", composed, StringComparison.Ordinal);
    }

    /// <summary>The two doors must not diverge: whatever <c>ComposeAsync</c> produces for a recall is what
    /// <c>Render</c> produces for the same items. Diverging would make the split a second implementation
    /// rather than a seam.</summary>
    [Fact]
    public async Task Render_and_ComposeAsync_agree_on_the_same_items()
    {
        MemoryItem[] items =
        [
            Item("exact fact", MemoryGrade.Authoritative),
            Item("recalled note", MemoryGrade.Associative),
        ];
        var options = new MemoryCompositionOptions { Budget = 500, AuthoritativeCharacters = 120 };

        var viaEngine = await EngineWith(items).ComposeAsync("BASE", new MemoryQuery("t", "s", "q"), options);

        Assert.Equal(viaEngine, MemoryComposition.Render("BASE", items, options));
    }

    [Fact]
    public void Render_returns_the_base_prompt_for_nothing_to_render()
    {
        Assert.Equal("BASE", MemoryComposition.Render("BASE", []));
        Assert.Throws<ArgumentNullException>(() => MemoryComposition.Render("BASE", null!));
    }

    /// <summary>"Append to a prompt" and "produce a block I will place myself" are both natural uses of a
    /// formatting-only entry point, and <c>Render</c> exists FOR callers doing their own retrieval — so an
    /// empty base prompt must not lead with the separator. Reported against 3.0.1: both heading sites
    /// appended <c>"\n\n"</c> unconditionally and the return only <c>TrimEnd</c>s, so a standalone block
    /// arrived with two leading newlines the caller had to strip.</summary>
    [Fact]
    public void Render_with_no_base_prompt_yields_a_standalone_block_with_no_leading_separator()
    {
        var composed = MemoryComposition.Render("", [Item("exact fact", MemoryGrade.Authoritative)]);

        Assert.StartsWith("## Known facts", composed, StringComparison.Ordinal);
    }

    /// <summary>The SECOND heading site, which is a separate <c>Append("\n\n")</c> — a fix applied to only
    /// one of them leaves the associative-only recall (the common case) still leading with the separator.</summary>
    [Fact]
    public void The_associative_heading_is_the_second_separator_site_and_needs_the_same_fix()
    {
        var composed = MemoryComposition.Render("", [Item("recalled note", MemoryGrade.Associative)]);

        Assert.StartsWith("## Recalled context", composed, StringComparison.Ordinal);
    }

    /// <summary>The regression half: a non-empty base prompt keeps its blank-line separator exactly as
    /// before. The fix is "nothing to separate FROM", never "drop the separator".</summary>
    [Fact]
    public void A_non_empty_base_prompt_keeps_its_separator()
    {
        var composed = MemoryComposition.Render("BASE", [Item("exact fact", MemoryGrade.Authoritative)]);

        Assert.StartsWith("BASE\n\n## Known facts", composed, StringComparison.Ordinal);
    }

    /// <summary>A base prompt is passed through VERBATIM, whitespace included — every other path in this
    /// method returns it unchanged (the no-items early return does), so trimming it here would make the two
    /// disagree about the same input.</summary>
    [Fact]
    public void A_whitespace_base_prompt_is_preserved_rather_than_trimmed_away()
    {
        Assert.StartsWith("  \n\n## Known facts",
            MemoryComposition.Render("  ", [Item("exact fact", MemoryGrade.Authoritative)]),
            StringComparison.Ordinal);
        Assert.Equal("  ", MemoryComposition.Render("  ", []));   // the no-items path, for the same input
    }

    /// <summary>The reserve still bounds exact material when a caller supplies its own — and it is the
    /// BUDGET it protects against, never the caller's selection. That distinction is what the adopter had to
    /// find by test.</summary>
    [Fact]
    public void Render_reserves_for_exact_material_out_of_the_callers_own_selection()
    {
        List<MemoryItem> items = [Item("the build gate is dev.mjs verify", MemoryGrade.Authoritative)];
        for (var i = 0; i < 50; i++)
            items.Add(Item($"noise item number {i} which is quite wordy indeed", MemoryGrade.Associative));

        var composed = MemoryComposition.Render("BASE", items,
            new MemoryCompositionOptions { Budget = 300, AuthoritativeCharacters = 100 });

        Assert.Contains("the build gate is dev.mjs verify", composed, StringComparison.Ordinal);
    }
}
