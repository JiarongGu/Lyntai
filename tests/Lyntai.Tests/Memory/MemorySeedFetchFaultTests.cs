using Lyntai.Memory;
using Lyntai.Memory.Annotation;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Seeding;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Memory;

/// <summary>A seed source is best-effort end to end: the fetch of the nodes it found is as much its own as the
/// search, so a store that fails there costs that source's candidates and never the other sources'
/// (<c>MemoryFailOpenCancellationTests</c> holds the search half).</summary>
public class MemorySeedFetchFaultTests
{
    private static MemoryQuery Query(string q) => new("t", "s", q, 10);

    private sealed class AlwaysOneSubject : IMemoryAnnotationPolicy
    {
        public Task<MemoryAnnotation> AnnotateAsync(
            MemoryAnnotationRequest request, CancellationToken ct = default) =>
            Task.FromResult(new MemoryAnnotation(["checklist"]));
    }

    [Fact]
    public async Task A_subject_seed_source_returns_empty_when_the_fetch_of_a_found_node_fails()
    {
        var store = new TimingOutGraphStore(nameof(IMemoryGraphStore.GetAsync));
        var engine = new GraphMemoryEngine("graph", store, seams: new GraphMemorySeams
            {
                Annotation = new AlwaysOneSubject(),
            });
        await engine.RememberAsync(new MemoryWrite("t", "s", "the deployment checklist"));
        Assert.Equal(0, store.TimedOut);                    // the write never fetched, so the fault is the seed's alone

        var seeds = await new SubjectSeedSource().SeedAsync(
            new MemorySeedRequest("graph", store, Query("checklist"), 10), default);

        Assert.Empty(seeds);
        Assert.Equal(1, store.TimedOut);                    // the fetch was reached
    }

    [Fact]
    public async Task A_semantic_seed_source_returns_empty_when_the_fetch_of_a_found_node_fails()
    {
        var provider = new FakeVectorProvider();
        var vectors = new InMemoryVectorStore();
        var embedded = (await provider.EmbedAsync(["the deployment checklist"]))[0];
        await vectors.UpsertAsync(MemoryVectorCollection.For("graph", "t", "s"), "1", embedded, "the deployment checklist");
        var store = new TimingOutGraphStore(nameof(IMemoryGraphStore.GetAsync));

        var seeds = await new SemanticSeedSource([provider], vectors).SeedAsync(
            new MemorySeedRequest("graph", store, Query("the deployment checklist"), 10), default);

        Assert.Empty(seeds);
        Assert.Equal(1, store.TimedOut);
    }
}
