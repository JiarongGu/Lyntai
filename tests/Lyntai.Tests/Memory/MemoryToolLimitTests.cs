using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

/// <summary>The recall tool is a MODEL-facing seam, so its <c>limit</c> is bounded the way expansion's
/// <c>hops</c> already is — and the engine's candidate arithmetic cannot overflow whatever arrives.</summary>
public class MemoryToolLimitTests
{
    private sealed class LimitRecordingEngine : IMemoryEngine
    {
        public int? SeenLimit { get; private set; }
        public string Name => "e";
        public MemoryGrades Supported => MemoryGrades.Associative;

        public Task<MemoryWriteResult> RememberAsync(MemoryWrite write, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default)
        {
            SeenLimit = query.Limit;
            return Task.FromResult(MemoryRecall.Empty);
        }
    }

    [Fact]
    public async Task A_model_asking_for_a_huge_limit_gets_the_tool_ceiling()
    {
        var engine = new LimitRecordingEngine();
        var tool = MemoryTools.Recall(engine, "mem", "t", null);

        await tool.InvokeAsync("""{"query":"x","limit":600000000}""");

        Assert.Equal(MemoryTools.MaxRecallLimit, engine.SeenLimit);
    }

    [Fact]
    public async Task A_model_asking_for_a_non_positive_limit_gets_one()
    {
        var engine = new LimitRecordingEngine();
        var tool = MemoryTools.Recall(engine, "mem", "t", null);

        await tool.InvokeAsync("""{"query":"x","limit":-5}""");

        Assert.Equal(1, engine.SeenLimit);
    }

    [Theory]
    [InlineData(600_000_000)]
    [InlineData(int.MaxValue)]
    public async Task An_overflowing_candidate_count_still_recalls_the_matching_entry(int limit)
    {
        var engine = new GraphMemoryEngine("g", new InMemoryMemoryGraphStore());
        await engine.RememberAsync(new MemoryWrite("t", "s", "the ferry leaves at nine"));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "ferry", Limit: limit));

        Assert.Single(recall.Items);
    }

    /// <summary>Records the candidate count it was asked for — a negative one is a full-scope scan on SQLite.</summary>
    private sealed class RecordingSeedSource : Lyntai.Memory.Seeding.IMemorySeedSource
    {
        public List<int> Limits { get; } = [];
        public string Name => "recording";

        public Task<IReadOnlyList<GraphNode>> SeedAsync(Lyntai.Memory.Seeding.MemorySeedRequest request,
            CancellationToken ct)
        {
            Limits.Add(request.Limit);
            return Task.FromResult<IReadOnlyList<GraphNode>>([]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task A_non_positive_limit_asks_no_channel_for_candidates(int limit)
    {
        var source = new RecordingSeedSource();
        var engine = new GraphMemoryEngine("g", new InMemoryMemoryGraphStore(), seedSources: [source]);

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "ferry", Limit: limit));

        Assert.Empty(recall.Items);
        Assert.Empty(source.Limits);
    }
}
