using Lyntai.Inference;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Memory;

/// <summary>A remember reports which tiers took the write (<see cref="MemoryWriteResult.Ran"/>), so a write
/// stored WITHOUT its vector is observable rather than silent — the write side of
/// <see cref="MemoryRecall.Ran"/>.</summary>
public class MemoryWriteResultTests
{
    private const string GraphName = "project/graph";

    private static GraphMemoryEngine Graph(IModelProvider? provider, IVectorStore? vectors,
        IMemoryGraphStore? store = null, GraphMemoryOptions? options = null) =>
        new(GraphName, store ?? new InMemoryMemoryGraphStore(), options,
            agePolicies: [new PerWriteAgePolicy()], providers: provider is null ? null : [provider],
            vectors: vectors);

    /// <summary>Every id indexed in the graph engine's collection. A zero vector is a legal probe and a search
    /// returns the collection's top-k whatever the scores, so this reads what is THERE.</summary>
    private static async Task<IReadOnlyList<string>> IndexedIdsAsync(IVectorStore vectors) =>
        [.. (await vectors.SearchAsync(MemoryVectorCollection.For(GraphName, "t", "s"), new float[64], 1000))
            .Select(m => m.Id)];

    [Fact]
    public async Task A_lexical_write_reports_the_lexical_tier_and_its_content_address()
    {
        var engine = new LexicalMemoryEngine("chat/lexical", new FakeMemoryStore());

        var result = await engine.RememberAsync(new MemoryWrite("t", "s", "the deploy pipeline needs approval"));

        Assert.Equal(MemorySources.Lexical, result.Ran);
        Assert.Equal(
            new MemoryRef("chat/lexical", MemoryContentId.For("t", "s", "the deploy pipeline needs approval")),
            result.Reference);
    }

    [Fact]
    public async Task A_curated_write_reports_the_curated_tier()
    {
        var engine = new CuratedMemoryEngine("project/glossary", new FakeCuratedStore(), "glossary");

        var result = await engine.RememberAsync(new MemoryWrite("t", "s", "SLA: service level agreement"));

        Assert.Equal(MemorySources.Curated, result.Ran);
    }

    [Fact]
    public async Task A_semantic_write_reports_the_semantic_tier()
    {
        var engine = new SemanticMemoryEngine("project/semantic",
            new SemanticMemory([new FakeVectorProvider()], new InMemoryVectorStore()));

        var result = await engine.RememberAsync(new MemoryWrite("t", "s", "the release owner signs off"));

        Assert.Equal(MemorySources.Semantic, result.Ran);
    }

    [Fact]
    public async Task A_blank_semantic_write_stores_nothing_and_reports_nothing()
    {
        // SemanticMemory skips blank content without storing it, so claiming the tier would be false
        var engine = new SemanticMemoryEngine("project/semantic",
            new SemanticMemory([new FakeVectorProvider()], new InMemoryVectorStore()));

        var result = await engine.RememberAsync(new MemoryWrite("t", "s", "   "));

        Assert.Equal(MemorySources.None, result.Ran);
    }

    [Fact]
    public async Task A_graph_write_with_no_vector_store_reports_the_graph_alone()
    {
        var engine = Graph(new FakeVectorProvider(), vectors: null);

        var result = await engine.RememberAsync(new MemoryWrite("t", "s", "stored on the model-free floor"));

        Assert.Equal(MemorySources.Graph, result.Ran);
    }

    [Fact]
    public async Task A_graph_write_with_an_embedder_reports_similarity_and_indexes_the_vector()
    {
        var vectors = new InMemoryVectorStore();
        var engine = Graph(new FakeVectorProvider(), vectors);

        var result = await engine.RememberAsync(new MemoryWrite("t", "s", "the deploy pipeline needs approval"));

        Assert.Equal(MemorySources.Graph | MemorySources.Similarity, result.Ran);
        Assert.Contains(result.Reference.Id, await IndexedIdsAsync(vectors));
    }

    [Fact]
    public async Task A_failed_embed_stores_the_entry_and_reports_no_similarity()
    {
        var vectors = new InMemoryVectorStore();
        var engine = Graph(new ThrowingVectorProvider(), vectors);

        var result = await engine.RememberAsync(new MemoryWrite("t", "s", "still stored"));

        Assert.Equal(MemorySources.Graph, result.Ran);
        Assert.Empty(await IndexedIdsAsync(vectors));
        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "still"));
        Assert.Equal(result.Reference, Assert.Single(recall.Items).Reference);
    }

    [Fact]
    public async Task An_embedder_that_is_unavailable_right_now_reports_no_similarity()
    {
        // it WOULD embed if asked, so a regression that ignored availability would index and report it
        var vectors = new InMemoryVectorStore();
        var engine = Graph(new UnavailableVectorProvider(), vectors);

        var result = await engine.RememberAsync(new MemoryWrite("t", "s", "written while the embedder is down"));

        Assert.Equal(MemorySources.Graph, result.Ran);
        Assert.Empty(await IndexedIdsAsync(vectors));
    }

    [Fact]
    public async Task A_failed_similarity_link_still_indexes_the_vector()
    {
        // the vector is indexed BEFORE the links, each best-effort on its own — a link failure costs links only
        var vectors = new InMemoryVectorStore();
        var engine = Graph(new FakeVectorProvider(), vectors,
            store: new TimingOutGraphStore(nameof(IMemoryGraphStore.LinkAsync)),
            options: new GraphMemoryOptions { MinSimilarity = 0.1 });
        await engine.RememberAsync(new MemoryWrite("t", "s", "you can cancel your subscription anytime"));

        var result = await engine.RememberAsync(new MemoryWrite("t", "s", "cancel your subscription from settings"));

        Assert.True(result.Ran.HasFlag(MemorySources.Similarity));
        Assert.Contains(result.Reference.Id, await IndexedIdsAsync(vectors));
    }

    [Fact]
    public async Task A_failed_vector_upsert_reports_no_similarity()
    {
        var engine = Graph(new FakeVectorProvider(), new WriteHostileVectorStore());

        var result = await engine.RememberAsync(new MemoryWrite("t", "s", "still stored"));

        Assert.Equal(MemorySources.Graph, result.Ran);
    }

    [Fact]
    public async Task A_fanned_out_composite_write_reports_the_union_and_the_first_members_reference()
    {
        var graph = Graph(new FakeVectorProvider(), new InMemoryVectorStore());
        var lexical = new LexicalMemoryEngine("project/lexical", new FakeMemoryStore());
        var composite = new CompositeMemoryEngine("project", [graph, lexical])
        {
            WriteRouting = MemoryWriteRouting.EveryCapable,
        };

        var result = await composite.RememberAsync(new MemoryWrite("t", "s", "the release owner signs off"));

        Assert.Equal(MemorySources.Graph | MemorySources.Similarity | MemorySources.Lexical, result.Ran);
        Assert.Equal(GraphName, result.Reference.Engine);
    }

    private sealed class ThrowingVectorProvider : FakeVectorProviderBase
    {
        public override Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts,
            CancellationToken ct = default) =>
            throw new InvalidOperationException("embedding endpoint is down");
    }

    /// <summary>Re-implements <see cref="IModelProvider"/> so its own <see cref="IsAvailable"/> replaces the
    /// interface's default.</summary>
    private sealed class UnavailableVectorProvider : FakeVectorProviderBase, IModelProvider
    {
        private readonly FakeVectorProvider _inner = new();

        public bool IsAvailable => false;

        public override Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts,
            CancellationToken ct = default) => _inner.EmbedAsync(texts, ct);
    }

    /// <summary>Searches fine and refuses every write, so the embed succeeds and the index is what fails.</summary>
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
