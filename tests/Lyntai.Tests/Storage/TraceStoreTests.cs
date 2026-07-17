using Lyntai.Cortex;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

public class TraceStoreTests : IDisposable
{
    private readonly TempDb _db = new();
    private readonly SqliteTraceStore _store;

    public TraceStoreTests() => _store = new SqliteTraceStore(_db.Factory);

    public void Dispose() => _db.Dispose();

    private static RunTrace Sample(string session) => new()
    {
        SessionId = session,
        Mode = "chat",
        StartedAt = new DateTimeOffset(2026, 7, 17, 10, 0, 0, TimeSpan.Zero),
        EndedAt = new DateTimeOffset(2026, 7, 17, 10, 5, 0, TimeSpan.Zero),
        Steps =
        [
            new TraceStep { Kind = "phase", Label = "plan", DurationMs = 1200 },
            new TraceStep { Kind = "llm", Label = "complete", InputTokens = 1200, OutputTokens = 340, CostUsd = 0.012, DurationMs = 2100, Detail = "claude-cli" },
            new TraceStep { Kind = "llm", Label = "judge", InputTokens = 300, OutputTokens = 40, CostUsd = 0.003, DurationMs = 800 },
        ],
    };

    [Fact]
    public async Task Save_and_load_with_steps_and_totals()
    {
        await _store.SaveAsync(Sample("s1"));

        var loaded = await _store.GetAsync("s1");

        Assert.NotNull(loaded);
        Assert.Equal("chat", loaded.Mode);
        Assert.Equal(3, loaded.Steps.Count);
        Assert.Equal(["plan", "complete", "judge"], loaded.Steps.Select(s => s.Label));
        Assert.Equal(1500, loaded.TotalInputTokens);
        Assert.Equal(380, loaded.TotalOutputTokens);
        Assert.Equal(0.015, loaded.TotalCostUsd, precision: 10);
        Assert.Equal(Sample("s1").StartedAt, loaded.StartedAt);
        Assert.Equal("claude-cli", loaded.Steps[1].Detail);
    }

    [Fact]
    public async Task Saving_the_same_session_replaces_the_trace()
    {
        await _store.SaveAsync(Sample("s1"));
        await _store.SaveAsync(Sample("s1") with { Mode = "replay", Steps = [new TraceStep { Kind = "phase", Label = "only" }] });

        var loaded = await _store.GetAsync("s1");

        Assert.NotNull(loaded);
        Assert.Equal("replay", loaded.Mode);
        Assert.Single(loaded.Steps);
    }

    [Fact]
    public async Task Unknown_session_returns_null()
    {
        Assert.Null(await _store.GetAsync("nope"));
    }
}
