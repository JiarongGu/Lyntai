using Lyntai.Cortex;
using Lyntai.Storage;

namespace Lyntai.Tests.Storage;

/// <summary>Backend-agnostic <see cref="ITraceStore"/> contract — run by the InMemory, SQLite, and
/// Postgres test classes so save/load, step round-trip, totals, trace-id, replace-on-resave and
/// unknown-session semantics are pinned identically. Sessions are namespaced by a caller-supplied
/// <paramref name="key"/> so the methods are safe on the shared Postgres container.</summary>
public static class TraceStoreContract
{
    private static RunTrace Sample(string session) => new()
    {
        SessionId = session,
        Mode = "chat",
        StartedAt = new DateTimeOffset(2026, 7, 17, 10, 0, 0, TimeSpan.Zero),
        EndedAt = new DateTimeOffset(2026, 7, 17, 10, 5, 0, TimeSpan.Zero),
        TraceId = "0af7651916cd43dd8448eb211c80319c",
        Steps =
        [
            new TraceStep { Kind = "phase", Label = "plan", Sequence = 0, OffsetMs = 0, DurationMs = 1200 },
            new TraceStep { Kind = "llm", Label = "complete", Sequence = 1, OffsetMs = 1200, InputTokens = 1200, OutputTokens = 340, CostUsd = 0.012, DurationMs = 2100, Detail = "claude-cli" },
            new TraceStep { Kind = "llm", Label = "judge", Sequence = 2, OffsetMs = 3300, InputTokens = 300, OutputTokens = 40, CostUsd = 0.003, DurationMs = 800 },
        ],
    };

    public static async Task Save_and_load_with_steps_totals_and_trace_id(ITraceStore store, string key)
    {
        await store.SaveAsync(Sample(key));

        var loaded = await store.GetAsync(key);
        Assert.NotNull(loaded);
        Assert.Equal("chat", loaded!.Mode);
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", loaded.TraceId);
        Assert.Equal(3, loaded.Steps.Count);
        Assert.Equal(["plan", "complete", "judge"], loaded.Steps.Select(s => s.Label)); // step order preserved
        Assert.Equal("claude-cli", loaded.Steps[1].Detail);
        Assert.Equal(1500, loaded.TotalInputTokens);  // 1200 + 300
        Assert.Equal(380, loaded.TotalOutputTokens);   // 340 + 40
        Assert.Equal(0.015, loaded.TotalCostUsd, precision: 10); // 0.012 + 0.003 — the double affinity trap
        Assert.Equal(Sample(key).StartedAt, loaded.StartedAt);
    }

    public static async Task Saving_the_same_session_replaces_the_trace(ITraceStore store, string key)
    {
        await store.SaveAsync(Sample(key));
        await store.SaveAsync(Sample(key) with { Mode = "replay", Steps = [new TraceStep { Kind = "phase", Label = "only" }] });

        var loaded = await store.GetAsync(key);
        Assert.NotNull(loaded);
        Assert.Equal("replay", loaded!.Mode);
        Assert.Single(loaded.Steps); // replaced, not appended
    }

    public static async Task Unknown_session_returns_null(ITraceStore store, string key)
    {
        Assert.Null(await store.GetAsync(key + "-nope"));
    }

    public static async Task Step_sequence_and_offset_round_trip(ITraceStore store, string key)
    {
        await store.SaveAsync(Sample(key));

        var loaded = await store.GetAsync(key);
        Assert.NotNull(loaded);
        Assert.Equal([0L, 1L, 2L], loaded!.Steps.Select(s => s.Sequence)); // explicit timeline ordinal
        Assert.Equal([0L, 1200L, 3300L], loaded.Steps.Select(s => s.OffsetMs)); // wall-clock offset from start
    }
}
