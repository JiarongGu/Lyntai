using Lyntai.Cortex;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Cortex;

/// <summary>The recorder stamps each step's timeline position — <see cref="TraceStep.Sequence"/> (0-based
/// insertion order) and <see cref="TraceStep.OffsetMs"/> (ms from the run start) — using the injectable
/// clock, so a consumer reading the trace back gets an explicit, store-independent timeline.</summary>
public sealed class TraceServiceTests
{
    [Fact]
    public async Task Recorder_stamps_sequence_and_wall_clock_offset_on_each_step()
    {
        var start = new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero);
        var now = start;
        var store = new InMemoryTraceStore();
        var svc = new TraceService(store, () => now);

        var rec = svc.Begin("s", "chat"); // startedAt = start
        now = start.AddMilliseconds(100);
        rec.Record(new TraceStep { Kind = "phase", Label = "a" });
        now = start.AddMilliseconds(250);
        rec.Record(new TraceStep { Kind = "llm", Label = "b" });
        now = start.AddMilliseconds(500);
        await rec.CompleteAsync();

        var loaded = await svc.GetAsync("s");
        Assert.NotNull(loaded);
        Assert.Equal([0, 1], loaded!.Steps.Select(x => x.Sequence));
        Assert.Equal([100, 250], loaded.Steps.Select(x => x.OffsetMs));
    }

    [Fact] // the recorder's stamp is authoritative — it overrides whatever the caller left on the step
    public async Task Recorder_stamp_overrides_a_caller_supplied_sequence()
    {
        var now = new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero);
        var store = new InMemoryTraceStore();
        var svc = new TraceService(store, () => now);

        var rec = svc.Begin("s", "chat");
        rec.Record(new TraceStep { Kind = "phase", Label = "a", Sequence = 99, OffsetMs = 7777 });
        await rec.CompleteAsync();

        var loaded = await svc.GetAsync("s");
        Assert.Equal(0, loaded!.Steps[0].Sequence);
        Assert.Equal(0, loaded.Steps[0].OffsetMs); // now == start → 0ms in
    }
}
