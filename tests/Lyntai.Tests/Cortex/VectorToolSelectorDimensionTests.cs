using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Cortex;

/// <summary><see cref="VectorToolSelector"/> scores with the library's ONE cosine, so a tool vector of the wrong
/// dimension — a document embedded by a different backend than the query after a fallback — ranks LAST, as it
/// does in every vector store, instead of scoring a meaningless partial product.</summary>
public sealed class VectorToolSelectorDimensionTests
{
    [Fact]
    public async Task A_tool_vector_of_another_dimension_ranks_last()
    {
        var selector = new VectorToolSelector([new ScriptedVectors()], new ToolSelectorOptions { Limit = 1 });
        ITool[] tools = [Tool("mismatched"), Tool("close"), Tool("far")];

        var selected = await selector.SelectAsync(
            new TextRequest { Messages = [TextMessage.User("anything")] }, tools);

        Assert.Equal(["close"], selected.Select(t => t.Name));
    }

    private static FunctionTool Tool(string name) => new(name, (_, _) => Task.FromResult(""), name);

    /// <summary>The query is [1,0,0,0]. "mismatched" shares its first four components exactly but has six, so
    /// a cosine over the shorter length would score it 1.0 and pick it.</summary>
    private sealed class ScriptedVectors : FakeVectorProviderBase
    {
        public override Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<float[]>>([.. texts.Select(Vector)]);

        public override Task<IReadOnlyList<float[]>> EmbedAsync(
            IReadOnlyList<string> texts, EmbeddingRole role, CancellationToken ct = default) =>
            role == EmbeddingRole.Query
                ? Task.FromResult<IReadOnlyList<float[]>>([.. texts.Select(_ => new[] { 1f, 0, 0, 0 })])
                : EmbedAsync(texts, ct);

        private static float[] Vector(string described) => described.Split(' ')[0] switch
        {
            "mismatched" => [1f, 0, 0, 0, 0, 0],
            "close" => [0.5f, 0.5f, 0, 0],
            _ => [0f, 1, 0, 0],
        };
    }
}
