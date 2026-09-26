using Lyntai.Inference;
using Lyntai.Memory;
using Lyntai.Memory.Annotation;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Memory;

/// <summary>A graph write embeds BEFORE it annotates, so <see cref="GraphMemoryOptions.SkipAnnotationWithoutVector"/>
/// can spare the annotation call of a write that lost its vector: a consumer retrying every write whose
/// <see cref="MemoryWriteResult.Ran"/> lacks <see cref="MemorySources.Similarity"/> (<c>docs/DECISIONS.md</c>
/// D175) then pays for the annotation once, on the retry that keeps the vector.</summary>
public class GraphAnnotationSkipTests
{
    private sealed class Counting(List<string> calls) : IMemoryAnnotationPolicy
    {
        public Task<MemoryAnnotation> AnnotateAsync(MemoryAnnotationRequest request, CancellationToken ct = default)
        {
            calls.Add("annotate");
            return Task.FromResult(new MemoryAnnotation(["deploy key"]));
        }
    }

    private sealed class Recording(List<string> calls) : FakeVectorProviderBase
    {
        private readonly FakeVectorProvider _inner = new();

        public override Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts,
            CancellationToken ct = default)
        {
            calls.Add("embed");
            return _inner.EmbedAsync(texts, ct);
        }
    }

    private sealed class Unavailable : FakeVectorProviderBase, IModelProvider
    {
        private readonly FakeVectorProvider _inner = new();

        public bool IsAvailable => false;

        public override Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts,
            CancellationToken ct = default) => _inner.EmbedAsync(texts, ct);
    }

    private static GraphMemoryEngine Engine(List<string> calls, IModelProvider? embedder, bool skip,
        int similarityK = 5) =>
        new("graph", new InMemoryMemoryGraphStore(),
            new GraphMemoryOptions { SkipAnnotationWithoutVector = skip, SimilarityK = similarityK },
            seams: new GraphMemorySeams
            {
                AgePolicies = [new PerWriteAgePolicy()],
                Annotation = new Counting(calls),
                Providers = embedder is null ? null : [embedder],
                Vectors = new InMemoryVectorStore(),
            });

    private static Task<MemoryWriteResult> Write(GraphMemoryEngine engine) =>
        engine.RememberAsync(new MemoryWrite("t", "s", "the deploy key is in the vault"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_embed_comes_before_the_annotation(bool skip)
    {
        List<string> calls = [];

        var result = await Write(Engine(calls, new Recording(calls), skip));

        Assert.Equal(["embed", "annotate"], calls);
        Assert.Equal(MemorySources.Graph | MemorySources.Similarity | MemorySources.Annotation, result.Ran);
    }

    [Fact]
    public async Task With_the_option_a_write_whose_embed_failed_is_not_annotated()
    {
        List<string> calls = [];

        var result = await Write(Engine(calls, new ThrowingVectorProvider(), skip: true));

        Assert.Empty(calls);
        Assert.Equal(MemorySources.Graph, result.Ran);
    }

    [Fact]
    public async Task With_the_option_a_write_whose_embedder_is_unavailable_is_not_annotated()
    {
        List<string> calls = [];

        var result = await Write(Engine(calls, new Unavailable(), skip: true));

        Assert.Empty(calls);
        Assert.Equal(MemorySources.Graph, result.Ran);
    }

    /// <summary>The POSITIVE control: off, the default, a write that lost its vector is annotated as always.</summary>
    [Fact]
    public async Task Without_the_option_a_write_whose_embed_failed_is_still_annotated()
    {
        List<string> calls = [];

        var result = await Write(Engine(calls, new ThrowingVectorProvider(), skip: false));

        Assert.Equal(["annotate"], calls);
        Assert.Equal(MemorySources.Graph | MemorySources.Annotation, result.Ran);
    }

    /// <summary>A write owes no vector when nothing embeds, so the option must not silently turn annotation
    /// off for an engine that was never going to index one.</summary>
    [Fact]
    public async Task With_the_option_and_no_embedder_every_write_is_annotated()
    {
        List<string> calls = [];

        var result = await Write(Engine(calls, embedder: null, skip: true));

        Assert.Equal(["annotate"], calls);
        Assert.True(result.Ran.HasFlag(MemorySources.Annotation));
    }

    [Fact]
    public async Task With_the_option_and_SimilarityK_zero_every_write_is_annotated()
    {
        List<string> calls = [];

        var result = await Write(Engine(calls, new Recording(calls), skip: true, similarityK: 0));

        Assert.Equal(["annotate"], calls);
        Assert.True(result.Ran.HasFlag(MemorySources.Annotation));
    }
}
