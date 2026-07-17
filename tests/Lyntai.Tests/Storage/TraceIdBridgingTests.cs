using System.Diagnostics;
using Lyntai.Cortex;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>A run trace captures the ambient OpenTelemetry trace id at Begin and round-trips it
/// through storage — the join key between a persisted trace and the distributed trace.</summary>
public class TraceIdBridgingTests : IDisposable
{
    private readonly TempDb _db = new();
    public void Dispose() => _db.Dispose();

    // a listener is required or ActivitySource.StartActivity returns null (nothing is sampling)
    private static ActivityListener AllDataListener(string sourceName) => new()
    {
        ShouldListenTo = s => s.Name == sourceName,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    };

    [Fact]
    public async Task Ambient_trace_id_is_captured_and_persisted()
    {
        using var source = new ActivitySource("test.trace.bridge");
        using var listener = AllDataListener("test.trace.bridge");
        ActivitySource.AddActivityListener(listener);

        var store = new SqliteTraceStore(_db.Factory);
        var service = new TraceService(store);

        string expectedTraceId;
        using (var activity = source.StartActivity("run"))
        {
            Assert.NotNull(activity); // sanity: the listener is sampling
            expectedTraceId = activity.TraceId.ToString();

            var recorder = service.Begin("session-1", "chat"); // Begin inside the activity scope
            recorder.Record(new TraceStep { Kind = "llm", Label = "complete", InputTokens = 10, OutputTokens = 2 });
            await recorder.CompleteAsync();
        }

        var loaded = await service.GetAsync("session-1");
        Assert.NotNull(loaded);
        Assert.Equal(expectedTraceId, loaded.TraceId);
        Assert.Equal(32, loaded.TraceId!.Length); // W3C trace id is 32 hex chars
    }

    [Fact]
    public async Task No_ambient_activity_leaves_trace_id_null()
    {
        var store = new SqliteTraceStore(_db.Factory);
        var service = new TraceService(store);

        // no Activity.Current here (no listener/source in scope)
        var recorder = service.Begin("session-2", "chat");
        recorder.Record(new TraceStep { Kind = "phase", Label = "noop" });
        await recorder.CompleteAsync();

        var loaded = await service.GetAsync("session-2");
        Assert.NotNull(loaded);
        Assert.Null(loaded.TraceId);
    }
}
