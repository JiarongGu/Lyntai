using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

/// <summary>A model that sends a string argument as <c>""</c> has left it unset: the recall tool treats an
/// empty <c>scope</c> or <c>query</c> exactly as an omitted one.</summary>
public class MemoryToolEmptyArgumentTests
{
    private sealed class QueryRecordingEngine : IMemoryEngine
    {
        public MemoryQuery? Seen { get; private set; }
        public string Name => "e";
        public MemoryGrades Supported => MemoryGrades.Associative;

        public Task<MemoryWriteResult> RememberAsync(MemoryWrite write, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default)
        {
            Seen = query;
            return Task.FromResult(MemoryRecall.Empty);
        }
    }

    [Fact]
    public async Task An_empty_scope_falls_back_to_the_registered_scope_as_an_omitted_one_does()
    {
        var engine = new QueryRecordingEngine();
        var tool = MemoryTools.Recall(engine, "mem", "t", "registered");

        await tool.InvokeAsync("""{"query":"x","scope":""}""");

        Assert.Equal("registered", engine.Seen!.Scope);
    }

    [Fact]
    public async Task An_empty_query_reaches_the_engine_as_no_query()
    {
        var engine = new QueryRecordingEngine();
        var tool = MemoryTools.Recall(engine, "mem", "t", null);

        await tool.InvokeAsync("""{"query":""}""");

        Assert.Null(engine.Seen!.Query);
    }
}
