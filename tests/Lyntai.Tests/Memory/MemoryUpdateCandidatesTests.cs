using System.Globalization;
using Lyntai.Inference;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>A write is shown the live entries it may update, and applies none</b> (<c>docs/DECISIONS.md</c> D239). The
/// list is a suggestion: what a recall's counters, links and validity would show of the entries is exactly what they
/// were before it was asked.
/// </summary>
public class MemoryUpdateCandidatesTests
{
    private static readonly DateTimeOffset Jan = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mar = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    private static GraphMemoryEngine Build(IMemoryGraphStore? store = null, IModelProvider? provider = null,
        IVectorStore? vectors = null) =>
        new("project/graph", store ?? new InMemoryMemoryGraphStore(), new GraphMemoryOptions { MinSimilarity = 0.1 },
            new GraphMemorySeams
            {
                AgePolicies = [new PerWriteAgePolicy()], Providers = provider is null ? null : [provider], Vectors = vectors,
                Clock = () => new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            });

    private static MemoryWrite Fact(string content, string task = "t", string scope = "s") => new(task, scope, content);

    [Fact]
    public async Task A_revised_fact_finds_the_entry_it_may_replace_with_its_revision()
    {
        var engine = Build();
        var paris = (await engine.RememberAsync(Fact("the client lives in Paris"))).Reference;

        var found = await engine.FindUpdatesAsync(Fact("the client lives in Berlin"));

        var candidate = Assert.Single(found);
        Assert.Equal(paris, candidate.Item.Reference);
        Assert.Equal(MemoryUpdateBasis.Lexical, candidate.Basis);
        Assert.NotNull(candidate.Item.Revision);
        Assert.Equal("the client lives in Paris", candidate.Item.Content);
    }

    [Fact]
    public async Task What_is_not_live_in_this_task_and_scope_never_appears()
    {
        var engine = Build();
        await engine.RememberAsync(Fact("the client lives in Paris"));
        var ended = (await engine.RememberAsync(Fact("the client lives in Rome") with { ValidFrom = Jan })).Reference;
        await engine.EndAsync("t", ended, new MemoryEnding("moved") { At = Mar });
        await engine.RememberAsync(Fact("the client lives in Oslo", scope: "other-scope"));
        await engine.RememberAsync(Fact("the client lives in Lima", task: "other-task"));
        await engine.RememberAsync(Fact("invoices are paid quarterly"));

        var found = await engine.FindUpdatesAsync(Fact("the client lives in Berlin"));

        Assert.Equal(["the client lives in Paris"], found.Select(c => c.Item.Content!));
    }

    [Fact]
    public async Task The_write_itself_is_left_out_whether_it_is_asked_before_or_after_it_is_stored()
    {
        var engine = Build();
        await engine.RememberAsync(Fact("the client lives in Paris"));
        var write = Fact("the client lives in Berlin");

        Assert.Single(await engine.FindUpdatesAsync(write));
        await engine.RememberAsync(write);

        var after = await engine.FindUpdatesAsync(write);
        Assert.Equal(["the client lives in Paris"], after.Select(c => c.Item.Content!));
    }

    [Fact]
    public async Task Asking_moves_nothing_a_recall_would_have()
    {
        var store = new InMemoryMemoryGraphStore();
        var engine = Build(store);
        var paris = (await engine.RememberAsync(Fact("the client lives in Paris"))).Reference;
        var id = long.Parse(paris.Id, CultureInfo.InvariantCulture);
        var before = (await store.GetAsync("project/graph", id))!;

        await engine.FindUpdatesAsync(Fact("the client lives in Berlin"));
        await engine.FindUpdatesAsync(Fact("the client lives in Berlin"));

        var after = (await store.GetAsync("project/graph", id))!;
        Assert.Equal((before.RecallCount, before.Stability, before.Age, before.Degree, before.Revision),
            (after.RecallCount, after.Stability, after.Age, after.Degree, after.Revision));
    }

    [Fact]
    public async Task The_limit_bounds_the_list_and_zero_returns_none()
    {
        var engine = Build();
        for (var i = 0; i < 6; i++) await engine.RememberAsync(Fact($"the client lives in town number {i}"));

        Assert.Equal(3, (await engine.FindUpdatesAsync(Fact("the client lives in Berlin"), 3)).Count);
        Assert.Empty(await engine.FindUpdatesAsync(Fact("the client lives in Berlin"), 0));
    }

    [Fact]
    public async Task A_wired_embedder_adds_the_similarity_basis_and_a_failed_one_leaves_the_lexical_list()
    {
        var engine = Build(provider: new FakeVectorProvider(), vectors: new InMemoryVectorStore());
        await engine.RememberAsync(Fact("you can cancel your subscription anytime"));

        var found = await engine.FindUpdatesAsync(Fact("cancel your subscription from settings"));

        Assert.True(Assert.Single(found).Basis.HasFlag(MemoryUpdateBasis.Similarity));

        var broken = Build(provider: new ThrowingVectorProvider(), vectors: new InMemoryVectorStore());
        await broken.RememberAsync(Fact("you can cancel your subscription anytime"));
        var lexical = await broken.FindUpdatesAsync(Fact("cancel your subscription from settings"));
        Assert.Equal(MemoryUpdateBasis.Lexical, Assert.Single(lexical).Basis);
    }

    [Fact]
    public async Task The_blend_asks_the_member_that_can_and_none_offers_none()
    {
        var graph = Build();
        await graph.RememberAsync(Fact("the client lives in Paris"));
        var blend = new CompositeMemoryEngine("blend", [new LexicalMemoryEngine("blend/lex", new FakeMemoryStore()), graph]);
        var lexicalOnly = new CompositeMemoryEngine("lex", [new LexicalMemoryEngine("lex/lex", new FakeMemoryStore())]);

        Assert.Single(await blend.FindUpdatesAsync(Fact("the client lives in Berlin")));
        Assert.Empty(await lexicalOnly.FindUpdatesAsync(Fact("the client lives in Berlin")));
    }
}
