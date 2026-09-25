using Lyntai.Memory;
using Lyntai.Memory.Annotation;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Memory;

/// <summary>A write's similarity links and its subject links each reach the store as ONE batched call — the
/// same round-trip claim a recall's co-activation already makes — never one call per edge.</summary>
public class WritePathLinkBatchingTests
{
    private sealed class OneSubject : IMemoryAnnotationPolicy
    {
        public Task<MemoryAnnotation> AnnotateAsync(MemoryAnnotationRequest request, CancellationToken ct = default) =>
            Task.FromResult(new MemoryAnnotation(["subscription"]));
    }

    [Fact]
    public async Task A_write_links_its_neighbours_in_one_call_per_kind_of_link()
    {
        var store = new LinkCountingGraphStore();
        var engine = new GraphMemoryEngine("g", store, new GraphMemoryOptions { MinSimilarity = 0.1 }, seams: new GraphMemorySeams
            {
                AgePolicies = [new PerWriteAgePolicy()],
                Providers = [new FakeVectorProvider()],
                Vectors = new InMemoryVectorStore(),
                Annotation = new OneSubject(),
            });
        for (var i = 0; i < 3; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s", $"cancel your subscription option {i}"));
        var before = (store.SingleLinks, store.BatchedLinks, store.EdgesWritten);

        await engine.RememberAsync(new MemoryWrite("t", "s", "cancel your subscription from settings"));

        Assert.Equal(before.SingleLinks, store.SingleLinks);
        Assert.Equal(before.BatchedLinks + 2, store.BatchedLinks);    // one similar batch, one subject batch
        Assert.True(store.EdgesWritten - before.EdgesWritten >= 2, "both kinds of link must have been written");
    }
}
