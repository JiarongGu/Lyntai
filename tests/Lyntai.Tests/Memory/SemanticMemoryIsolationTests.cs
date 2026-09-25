using Lyntai.Memory;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Memory;

/// <summary>Semantic memory and a graph engine can share ONE vector store. A task named like an engine must not
/// let a scope-less semantic recall read the graph engine's collections — they belong to OTHER tasks.</summary>
public class SemanticMemoryIsolationTests
{
    [Fact]
    public async Task A_scope_less_recall_never_reads_a_graph_engines_collection_under_the_same_head()
    {
        var vectors = new InMemoryVectorStore();
        var provider = new FakeVectorProvider();
        var semantic = new SemanticMemory([provider], vectors);
        await semantic.RememberAsync("chat", "s", "the ferry leaves at nine");

        // a graph engine named "chat" indexing a DIFFERENT task, "other", in the same store
        var graphVector = (await provider.EmbedAsync(["the ferry leaves at noon"]))[0];
        await vectors.UpsertAsync(MemoryVectorCollection.For("chat", "other", "s"), "1", graphVector,
            "the ferry leaves at noon");

        var hits = await semantic.RecallAsync("chat", scope: null, "ferry", k: 10);

        Assert.Contains(hits, h => h.Content == "the ferry leaves at nine");
        Assert.DoesNotContain(hits, h => h.Content == "the ferry leaves at noon");
    }
}
