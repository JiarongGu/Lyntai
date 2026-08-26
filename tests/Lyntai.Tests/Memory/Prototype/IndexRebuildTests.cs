using System.Globalization;
using Lyntai.Embeddings;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;
using Xunit;

namespace Lyntai.Tests.Memory.Prototype;

/// <summary>
/// <b>Is the similarity index a PROJECTION — droppable and rebuildable from the store — or is it a second
/// source of truth?</b> The proposal's §3.3 asserts the first and treats it as a design principle. This
/// checks it against the shipped code, because the answer decides whether "delete the index and rebuild it"
/// is a real recovery procedure or a hope.
///
/// <para><b>The answer is yes, with one wrinkle worth reporting.</b> Everything a rebuild needs is public:
/// the graph store enumerates its own nodes with full content, and the vector store takes upserts. But the
/// COLLECTION ADDRESS is engine-private (<c>GraphMemoryEngine.VectorCollection</c>), so an application
/// rebuilding the index has to hard-code a format string it was never told — and if that format ever
/// changed, a rebuild would silently populate collections nothing reads. That is the one thing a
/// first-class version would have to expose, and it is a smaller ask than the rebuild itself.</para>
///
/// <para>The last Phase-1 invariant of the proposal's own list; the rest were closed by the facts in
/// <c>MemoryRemovalCompletenessTests</c>, <c>MemoryBurialNotDeletionTests</c> and
/// <c>MemoryAuthoritativeSurvivalTests</c>.</para>
/// </summary>
public class IndexRebuildTests
{
    private const string Engine = "rebuild";

    /// <summary>What an application would have to write today to rebuild its own similarity index.
    /// <para>Reads the STORE — the source of truth — and re-embeds every entry's content back into the
    /// vector collection. Nothing here needs the engine at all, which is the point: the index carries no
    /// information the store does not already hold.</para></summary>
    private static async Task<int> RebuildAsync(IMemoryGraphStore store, IVectorStore vectors,
        IEmbedder embedder, string taskKey, string? scope)
    {
        var nodes = await store.SeedAsync(Engine, taskKey, scope, query: null, limit: int.MaxValue);
        foreach (var node in nodes)
        {
            var vector = await embedder.EmbedAsync(node.Content);
            // THE WRINKLE: this format is GraphMemoryEngine's private convention. An application has no
            // supported way to learn it, and a rebuild that guessed it wrongly would look like it worked.
            await vectors.UpsertAsync($"{Engine}|{node.TaskKey}|{node.Scope}",
                node.Id.ToString(CultureInfo.InvariantCulture), vector, node.Content);
        }
        return nodes.Count;
    }

    private static async Task<IReadOnlyList<string>> IndexedIdsAsync(IVectorStore vectors, string collection) =>
        [.. (await vectors.SearchAsync(collection, new float[64], 1000)).Select(m => m.Id).Order(StringComparer.Ordinal)];

    [Fact]
    public async Task Dropping_the_similarity_index_loses_no_evidence_and_it_rebuilds_to_parity()
    {
        var store = new InMemoryMemoryGraphStore();
        var vectors = new InMemoryVectorStore();
        var embedder = new FakeEmbedder();
        var engine = new GraphMemoryEngine(Engine, store, agePolicies: [new PerWriteAgePolicy()],
            embedder: embedder, vectors: vectors);

        for (var i = 0; i < 12; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s", $"fact number {i} about the deployment"));

        var collection = $"{Engine}|t|s";
        var indexed = await IndexedIdsAsync(vectors, collection);
        Assert.Equal(12, indexed.Count);

        // DROP THE PROJECTION — the operational disaster this is a recovery procedure for.
        await vectors.RemoveCollectionAsync(collection);
        Assert.Empty(await IndexedIdsAsync(vectors, collection));

        // The EVIDENCE is untouched: recall still works, because seeding is lexical and the store is the
        // source of truth. That is what makes the index a projection rather than a second truth.
        var survived = await engine.RecallAsync(new MemoryQuery("t", "s", "deployment", Limit: 20));
        Assert.Equal(12, survived.Items.Count);

        var rebuilt = await RebuildAsync(store, vectors, embedder, "t", "s");

        Assert.Equal(12, rebuilt);
        Assert.Equal(indexed, await IndexedIdsAsync(vectors, collection));   // PARITY, id for id
    }

    [Fact]
    public async Task A_rebuilt_index_answers_the_semantic_query_the_dropped_one_could()
    {
        // Parity of IDS is necessary and not sufficient: the vectors have to be usable, not merely present.
        // This asks the index the question it exists for -- a nearest-neighbour search -- before and after,
        // and requires the same answer. A rebuild that stored zero vectors would pass the fact above.
        var store = new InMemoryMemoryGraphStore();
        var vectors = new InMemoryVectorStore();
        var embedder = new FakeEmbedder();
        var engine = new GraphMemoryEngine(Engine, store, agePolicies: [new PerWriteAgePolicy()],
            embedder: embedder, vectors: vectors);

        await engine.RememberAsync(new MemoryWrite("t", "s", "the production database runs on postgres"));
        await engine.RememberAsync(new MemoryWrite("t", "s", "kittens are small and unrelated"));

        var collection = $"{Engine}|t|s";
        var query = await embedder.EmbedAsync("the production database runs on postgres");
        var before = (await vectors.SearchAsync(collection, query, 1)).Single();

        await vectors.RemoveCollectionAsync(collection);
        await RebuildAsync(store, vectors, embedder, "t", "s");

        var after = (await vectors.SearchAsync(collection, query, 1)).Single();

        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.Payload, after.Payload);
        Assert.Equal(before.Score, after.Score, 6);
    }
}
