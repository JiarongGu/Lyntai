using Lyntai.Memory;
using Lyntai.Memory.Annotation;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Salience;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Memory;

/// <summary>Boundaries a bare graph engine has to hold on its own: whose reference it was handed, how many
/// edges one subject buys, what "turn salience off" can be combined with, and what a forget erases.</summary>
public class GraphMemoryBoundaryTests
{
    // ---- a reference names its OWNING engine ------------------------------------------------------------

    [Fact]
    public async Task Expanding_another_engines_reference_returns_nothing()
    {
        var engine = new GraphMemoryEngine("g", new InMemoryMemoryGraphStore());
        var mine = await engine.RememberAsync(new MemoryWrite("t", "s", "the ferry leaves at nine"));

        var foreign = await engine.ExpandAsync(new MemoryRef("other/graph", mine.Reference.Id));

        Assert.Empty(foreign.Items);
    }

    [Fact]
    public async Task Linking_to_another_engines_reference_is_refused()
    {
        var engine = new GraphMemoryEngine("g", new InMemoryMemoryGraphStore());
        var a = await engine.RememberAsync(new MemoryWrite("t", "s", "the ferry leaves at nine"));
        var b = await engine.RememberAsync(new MemoryWrite("t", "s", "the pier is north"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            engine.LinkAsync(a.Reference, new MemoryRef("other/graph", b.Reference.Id)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            engine.LinkAsync(new MemoryRef("other/graph", a.Reference.Id), b.Reference));
    }

    // ---- one subject is one edge, however an annotator spells it ----------------------------------------

    private sealed class SpellingAnnotator : IMemoryAnnotationPolicy
    {
        public Task<MemoryAnnotation> AnnotateAsync(MemoryAnnotationRequest request, CancellationToken ct = default) =>
            Task.FromResult(new MemoryAnnotation(["Alice", " alice ", "ALICE"]));
    }

    [Fact]
    public async Task Case_and_padding_variants_of_one_subject_link_once()
    {
        var store = new InMemoryMemoryGraphStore();
        var engine = new GraphMemoryEngine("g", store, seams: new GraphMemorySeams
            {
                Annotation = new SpellingAnnotator(),
            });
        var first = await engine.RememberAsync(new MemoryWrite("t", "s", "my spouse is Alice"));
        var second = await engine.RememberAsync(new MemoryWrite("t", "s", "she is an anaesthetist"));

        var neighbours = await store.NeighboursAsync("g", "t", [long.Parse(second.Reference.Id)], 10);

        var edge = Assert.Single(neighbours, n => n.Node.Id == long.Parse(first.Reference.Id));
        Assert.Equal(1, edge.EdgeWeight);
    }

    // ---- the off switch is not combinable ---------------------------------------------------------------

    [Fact]
    public void Neutral_salience_beside_another_policy_is_refused_by_name()
    {
        var ex = Assert.Throws<ArgumentException>(() => new GraphMemoryEngine("g", new InMemoryMemoryGraphStore(), seams: new GraphMemorySeams
            {
                SaliencePolicies = [new StructuralSaliencePolicy(), new NeutralSaliencePolicy()],
            }));

        Assert.Contains(nameof(NeutralSaliencePolicy), ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Provenance", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Neutral_salience_registered_after_AddLyntai_is_reported_as_the_mistake_it_is()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddProvider(_ => new FakeTextProvider("p")).UseInMemoryStorage().AddMemory());
        services.AddSingleton<IMemorySaliencePolicy, NeutralSaliencePolicy>();
        await using var sp = services.BuildServiceProvider();

        var ex = Assert.ThrowsAny<Exception>(() => sp.GetRequiredService<IMemoryEngineFactory>());

        Assert.Contains(nameof(NeutralSaliencePolicy), ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Provenance", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Neutral_salience_alone_still_builds()
    {
        _ = new GraphMemoryEngine("g", new InMemoryMemoryGraphStore(), seams: new GraphMemorySeams
            {
                SaliencePolicies = [new NeutralSaliencePolicy()],
            });
    }

    // ---- an unscoped forget erases an ORPHANED collection too ---------------------------------------------

    [Fact]
    public async Task An_unscoped_forget_drops_a_vector_collection_whose_nodes_are_already_gone()
    {
        var vectors = new InMemoryVectorStore();
        var engine = new GraphMemoryEngine("g", new InMemoryMemoryGraphStore(), seams: new GraphMemorySeams
            {
                Vectors = vectors,
            });
        var orphan = MemoryVectorCollection.For("g", "t", "ghost");
        var neighbour = MemoryVectorCollection.For("g", "tx", "ghost");
        await vectors.UpsertAsync(orphan, "1", [1f, 0f], "a withdrawn user's words");
        await vectors.UpsertAsync(neighbour, "1", [1f, 0f], "another task's words");

        await engine.ForgetAsync("t");

        Assert.DoesNotContain(orphan, await vectors.ListCollectionsAsync(""));
        Assert.Contains(neighbour, await vectors.ListCollectionsAsync(""));
    }
}
