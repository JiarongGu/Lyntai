using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Storage;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Memory;

/// <summary>The two seams a real curated catalog needed, both reported by an adopter on 3.0.0: a catalog
/// MIXES provenance (the owner types facts, the assistant saves what it inferred) and had no way to say so,
/// and a catalog of several sections had no single-engine read.</summary>
public class CuratedMemoryEngineTests
{
    private static async Task<FakeCuratedStore> SeededAsync()
    {
        var store = new FakeCuratedStore();
        await store.AddAsync("glossary", "the release gate is dev.mjs verify", taskKey: "t",
            metadata: new Dictionary<string, string> { ["source"] = "owner" });
        await store.AddAsync("glossary", "the owner probably prefers short commit messages", taskKey: "t",
            metadata: new Dictionary<string, string> { ["source"] = "assistant" });
        await store.AddAsync("style", "wrap prose at 110 columns", taskKey: "t");
        return store;
    }

    // the deployment's own convention, which is the reason this is a delegate and not a reserved key
    private static MemoryGrade BySource(CuratedMemory entry) =>
        entry.Metadata?.GetValueOrDefault("source") == "assistant"
            ? MemoryGrade.Associative
            : MemoryGrade.Authoritative;

    [Fact]
    public async Task Without_a_delegate_every_entry_is_authoritative_exactly_as_before()
    {
        var engine = new CuratedMemoryEngine("e", await SeededAsync(), "glossary");

        var recall = await engine.RecallAsync(new MemoryQuery("t"));

        Assert.Equal(2, recall.Items.Count);
        Assert.All(recall.Items, i => Assert.Equal(MemoryGrade.Authoritative, i.Grade));
    }

    /// <summary>The ask: an assistant's inference must not be presented as an exact fact, and the two live in
    /// ONE catalog under one kind — so the `kind` axis, already spent on the catalog's own sections, cannot
    /// carry the distinction and a composite of two curated engines cannot either (both would be
    /// authoritative).</summary>
    [Fact]
    public async Task A_delegate_grades_each_entry_from_the_entry_itself()
    {
        var engine = new CuratedMemoryEngine("e", await SeededAsync(), "glossary") { Grade = BySource };

        var recall = await engine.RecallAsync(new MemoryQuery("t"));

        Assert.Equal(MemoryGrade.Authoritative,
            recall.Items.Single(i => i.Headline.Contains("release gate")).Grade);
        Assert.Equal(MemoryGrade.Associative,
            recall.Items.Single(i => i.Headline.Contains("short commit")).Grade);
    }

    [Fact]
    public async Task The_delegate_grades_the_search_branch_too_not_only_the_composition_read()
    {
        var engine = new CuratedMemoryEngine("e", await SeededAsync(), "glossary") { Grade = BySource };

        var recall = await engine.RecallAsync(new MemoryQuery("t", Query: "commit"));

        Assert.Equal(MemoryGrade.Associative, Assert.Single(recall.Items).Grade);
    }

    /// <summary><c>Supported</c> deliberately does NOT widen. If it did, a composite would route associative
    /// writes to the catalog instead of to the engine that can decay them — and the store has no grade column
    /// to mark them with, so they would come back exact on the next read with no delegate installed.</summary>
    [Fact]
    public async Task A_delegate_does_not_widen_what_the_engine_accepts()
    {
        var engine = new CuratedMemoryEngine("e", await SeededAsync(), "glossary") { Grade = BySource };

        Assert.Equal(MemoryGrades.Authoritative, engine.Supported);
        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => engine.RememberAsync(
            new MemoryWrite("t", "s", "inferred", Grade: MemoryGrade.Associative)));
        Assert.Contains("Grade.Inherit", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>The supported route for an application that wants graded material in ONE catalog: write with
    /// Inherit carrying provenance, read it back through the delegate.</summary>
    [Fact]
    public async Task An_inherit_write_carrying_provenance_round_trips_through_the_delegate()
    {
        var store = new FakeCuratedStore();
        var engine = new CuratedMemoryEngine("e", store, "glossary") { Grade = BySource };

        await engine.RememberAsync(new MemoryWrite("t", "s", "inferred from the diff",
            Metadata: new Dictionary<string, string> { ["source"] = "assistant" }));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s"));
        Assert.Equal(MemoryGrade.Associative, Assert.Single(recall.Items).Grade);
    }

    /// <summary>A throwing delegate fails the recall the way a faulting store does. Guessing has no safe
    /// direction: an inferred fact shown as exact is the confusion the split exists to prevent, and an exact
    /// fact shown as inferred loses the protection objective (1) gives it.</summary>
    [Fact]
    public async Task A_throwing_delegate_yields_nothing_rather_than_a_guessed_grade()
    {
        var engine = new CuratedMemoryEngine("e", await SeededAsync(), "glossary")
        {
            Grade = _ => throw new InvalidOperationException("classifier down"),
        };

        Assert.Empty((await engine.RecallAsync(new MemoryQuery("t"))).Items);
    }

    // ---- kind: null ----------------------------------------------------------------------------------

    /// <summary>A catalog with several sections had no single-engine read: one engine binds one kind, so N
    /// sections meant a composite of N members. Null reads them all, still bounded by the query's limit —
    /// which is the bound the whole-catalog read was narrowed to fix in the first place.</summary>
    [Fact]
    public async Task A_null_kind_reads_every_section_of_the_catalog()
    {
        var engine = new CuratedMemoryEngine("e", await SeededAsync(), kind: null);

        var recall = await engine.RecallAsync(new MemoryQuery("t"));

        Assert.Equal(3, recall.Items.Count);
        Assert.Contains(recall.Items, i => i.Headline.Contains("110 columns"));
    }

    [Fact]
    public async Task A_null_kind_is_still_bounded_by_the_querys_limit()
    {
        var engine = new CuratedMemoryEngine("e", await SeededAsync(), kind: null);

        Assert.Equal(2, (await engine.RecallAsync(new MemoryQuery("t", Limit: 2))).Items.Count);
    }

    [Fact]
    public async Task A_null_kind_searches_every_section_too()
    {
        var engine = new CuratedMemoryEngine("e", await SeededAsync(), kind: null);

        var recall = await engine.RecallAsync(new MemoryQuery("t", Query: "columns"));

        Assert.Contains(recall.Items, i => i.Headline.Contains("110 columns"));
    }

    /// <summary>Read-only, and LOUD about it: there is no section to write into, and picking one would be the
    /// engine inventing the catalog's shape. Losing a write silently is the failure this refuses.</summary>
    [Fact]
    public async Task A_null_kind_refuses_a_write_instead_of_choosing_a_section()
    {
        var engine = new CuratedMemoryEngine("e", new FakeCuratedStore(), kind: null);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            engine.RememberAsync(new MemoryWrite("t", "s", "anything")));
        Assert.Contains("kind: null", ex.Message, StringComparison.Ordinal);
    }

    // ---- through the builder -------------------------------------------------------------------------

    /// <summary>Both reach the engine through <c>AddMemoryEngine</c>, which is the only route a consumer
    /// takes — and the grading overload is a SEPARATE method, so this also pins that the two-argument calls
    /// every existing consumer wrote still bind the original.</summary>
    [Fact]
    public async Task The_builder_wires_a_grade_delegate_and_a_null_kind()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICuratedMemoryStore>(await SeededAsync());
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddMemoryEngine("graded", e => e.UseCurated("glossary", BySource))
            .AddMemoryEngine("plain", e => e.UseCurated("glossary"))
            .AddMemoryEngine("whole", e => e.UseCurated(kind: null, label: "catalog")));
        using var sp = services.BuildServiceProvider();
        var engines = sp.GetRequiredService<IMemoryEngineFactory>();

        var graded = await engines.Get("graded").RecallAsync(new MemoryQuery("t"));
        Assert.Contains(graded.Items, i => i.Grade == MemoryGrade.Associative);

        var plain = await engines.Get("plain").RecallAsync(new MemoryQuery("t"));
        Assert.All(plain.Items, i => Assert.Equal(MemoryGrade.Authoritative, i.Grade));

        Assert.Equal(3, (await engines.Get("whole").RecallAsync(new MemoryQuery("t"))).Items.Count);
    }
}
