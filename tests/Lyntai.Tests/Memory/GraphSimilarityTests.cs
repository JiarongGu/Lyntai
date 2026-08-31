using Lyntai.Embeddings;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Seeding;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Memory;

/// <summary>Similarity enrichment: pure enrichment on top of a model-free floor. It links a new entry to
/// its nearest existing neighbours when an embedder and a vector store are wired, and its absence — or its
/// failure — costs connections, never the entry.</summary>
public class GraphSimilarityTests
{
    private static GraphMemoryEngine Engine(IEmbedder? embedder, IVectorStore? vectors,
        GraphMemoryOptions? options = null) =>
        new("project/graph", new InMemoryMemoryGraphStore(), options,
            agePolicies: [new PerWriteAgePolicy()], embedder: embedder, vectors: vectors);

    [Fact]
    public async Task A_new_entry_is_linked_to_a_similar_existing_one()
    {
        var engine = Engine(new FakeEmbedder(), new InMemoryVectorStore(),
            new GraphMemoryOptions { MinSimilarity = 0.1 });

        await engine.RememberAsync(new MemoryWrite("t", "s", "you can cancel your subscription anytime"));
        await engine.RememberAsync(new MemoryWrite("t", "s", "cancel your subscription from settings"));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "cancel"));

        Assert.Contains(recall.Items, i => i.Degree >= 1);
    }

    [Fact]
    public async Task Recall_reports_that_similarity_is_wired()
    {
        var engine = Engine(new FakeEmbedder(), new InMemoryVectorStore());
        await engine.RememberAsync(new MemoryWrite("t", "s", "a remembered thing"));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "remembered"));

        Assert.True(recall.Ran.HasFlag(MemorySources.Similarity));
    }

    [Fact]
    public async Task Without_an_embedder_the_graph_still_forms_and_says_so()
    {
        // the model-free floor: co-activation and explicit links do not need an embedder at all
        var engine = Engine(embedder: null, vectors: null);
        await engine.RememberAsync(new MemoryWrite("t", "s", "alpha about widgets"));
        await engine.RememberAsync(new MemoryWrite("t", "s", "beta about widgets"));

        await engine.RecallAsync(new MemoryQuery("t", "s", "widgets")); // co-activation links them
        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "widgets"));

        Assert.All(recall.Items, i => Assert.True(i.Degree >= 1));
        Assert.False(recall.Ran.HasFlag(MemorySources.Similarity)); // absent, not merely empty
    }

    [Fact]
    public async Task A_failing_embedder_costs_links_not_the_entry()
    {
        // enrichment sits ON TOP of the floor, so a broken embedding endpoint must not fail a write
        var engine = Engine(new ThrowingEmbedder(), new InMemoryVectorStore());

        var reference = await engine.RememberAsync(new MemoryWrite("t", "s", "still stored"));

        Assert.NotNull(reference.Id);
        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "still"));
        Assert.Single(recall.Items);
    }

    [Fact]
    public async Task A_failing_embedder_at_RECALL_degrades_to_the_lexical_hits_rather_than_to_nothing()
    {
        // The twin of the write-path fact above, and it was missing: the engine's own semantic seed had no
        // try/catch and was called AFTER store.SeedAsync had already produced lexical seeds, so a transient
        // embedder fault threw out of GatherAsync, hit RecallAsync's best-effort catch, and returned
        // MemoryRecall.Empty — good seeds discarded, and indistinguishable from "the query matched nothing".
        // Design §5.7.0: "enrichment is best-effort and its failure degrades QUALITY, never CORRECTNESS."
        // The catch now lives in SemanticSeedSource, and this asserts the same promise through the seam.
        //
        // The write must go in with a WORKING embedder (the write path is separately guarded, but this test is
        // about recall), so the throwing one is installed for the read only.
        var store = new InMemoryMemoryGraphStore();
        var writingEmbedder = new FakeEmbedder();
        var writingVectors = new InMemoryVectorStore();
        var writing = new GraphMemoryEngine("e", store,
            embedder: writingEmbedder, vectors: writingVectors,
            seedSources: [new LexicalSeedSource(),
                new SemanticSeedSource(writingEmbedder, writingVectors, new SemanticSeedOptions { K = 5 })]);
        await writing.RememberAsync(new MemoryWrite("t", "s", "the deploy pipeline needs approval"));

        var throwing = new ThrowingEmbedder();
        var readingVectors = new InMemoryVectorStore();
        var reading = new GraphMemoryEngine("e", store,
            embedder: throwing, vectors: readingVectors,
            seedSources: [new LexicalSeedSource(),
                new SemanticSeedSource(throwing, readingVectors, new SemanticSeedOptions { K = 5 })]);

        var recall = await reading.RecallAsync(new MemoryQuery("t", "s", "deploy pipeline"));

        Assert.NotEmpty(recall.Items);
        Assert.Contains(recall.Items, i => i.Headline.Contains("deploy", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failing_vector_STORE_costs_links_not_the_entry()
    {
        // The sibling of the embedder fact above, and a DIFFERENT link in the chain: a working embedder
        // produces a vector and the INDEX is what refuses it. The embedder case short-circuits in
        // SearchAsync before enrichment runs at all, so it never exercised the store's own write.
        //
        // "A partial projection failure cannot lose the canonical write" is the invariant, and it is the one
        // this engine's whole best-effort posture rests on: enrichment sits ON TOP of a model-free floor.
        var engine = Engine(new FakeEmbedder(), new WriteHostileVectorStore());

        var reference = await engine.RememberAsync(new MemoryWrite("t", "s", "still stored"));

        Assert.NotNull(reference.Id);
        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "still"));
        Assert.Single(recall.Items);
    }

    [Fact]
    public async Task An_unrelated_entry_is_not_linked()
    {
        // without a floor a new entry links to its k nearest however unrelated, which in a young graph
        // means linking to nearly everything
        var engine = Engine(new FakeEmbedder(), new InMemoryVectorStore(),
            new GraphMemoryOptions { MinSimilarity = 0.99 });

        await engine.RememberAsync(new MemoryWrite("t", "s", "the deploy pipeline needs approval"));
        await engine.RememberAsync(new MemoryWrite("t", "s", "kittens are small"));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "kittens"));

        Assert.All(recall.Items, i => Assert.Equal(0, i.Degree));
    }

    private sealed class ThrowingEmbedder : IEmbedder
    {
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts,
            CancellationToken ct = default) =>
            throw new InvalidOperationException("embedding endpoint is down");
    }

    /// <summary>A vector store that SEARCHES fine and refuses to be written to — so enrichment gets past the
    /// shared search and fails at the index, which is the half a failing embedder can never reach.</summary>
    private sealed class WriteHostileVectorStore : IVectorStore
    {
        private readonly InMemoryVectorStore _inner = new();

        public Task UpsertAsync(string collection, string id, float[] vector, string payload,
            CancellationToken ct = default) =>
            throw new InvalidOperationException("the vector store is read-only");

        public Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k,
            CancellationToken ct = default) => _inner.SearchAsync(collection, query, k, ct);

        public Task DeleteAsync(string collection, string id, CancellationToken ct = default) =>
            _inner.DeleteAsync(collection, id, ct);

        public Task RemoveCollectionAsync(string collection, CancellationToken ct = default) =>
            _inner.RemoveCollectionAsync(collection, ct);
    }
}
