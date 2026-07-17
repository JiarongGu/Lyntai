using System.Diagnostics;
using System.Diagnostics.Metrics;
using Lyntai;
using Lyntai.Diagnostics;
using Lyntai.Llm;
using Lyntai.Llm.Routing;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Core;

/// <summary>Every test filters on a unique model tag: the listener is process-global, and other
/// router tests emit spans/metrics concurrently.</summary>
public class LyntaiDiagnosticsTests
{
    private static LlmRequest Req => new() { Messages = [LlmMessage.User("hi")] };

    private static LlmRouter Router(params ILlmProvider[] providers) =>
        new(providers, new DeadHostTracker(), new LyntaiOptions());

    private static ActivityListener SpanListener(List<Activity> sink) => new()
    {
        ShouldListenTo = s => s.Name == LyntaiDiagnostics.ActivitySourceName,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        ActivityStopped = a => { lock (sink) sink.Add(a); },
    };

    private static List<Activity> SpansFor(List<Activity> spans, string model)
    {
        lock (spans) return [.. spans.Where(s => Equals(s.GetTagItem("gen_ai.request.model"), model))];
    }

    [Fact]
    public async Task Completion_emits_a_gen_ai_span_with_usage_tags()
    {
        var spans = new List<Activity>();
        using var listener = SpanListener(spans);
        ActivitySource.AddActivityListener(listener);

        var p = new FakeLlmProvider("p1");
        p.Replies.Enqueue(new LlmReply("hi", LlmVerdict.Ok, new LlmUsage(100, 20)));
        await Router(p).CompleteAsync([new("p1", "m-span-ok")], Req);

        var span = Assert.Single(SpansFor(spans, "m-span-ok"));
        Assert.Equal("chat m-span-ok", span.DisplayName);
        Assert.Equal("chat", span.GetTagItem("gen_ai.operation.name"));
        Assert.Equal("p1", span.GetTagItem("gen_ai.system"));
        Assert.Equal(100L, span.GetTagItem("gen_ai.usage.input_tokens"));
        Assert.Equal(20L, span.GetTagItem("gen_ai.usage.output_tokens"));
        Assert.Null(span.GetTagItem("error.type"));
    }

    [Fact]
    public async Task Failed_attempt_sets_error_type_and_status()
    {
        var spans = new List<Activity>();
        using var listener = SpanListener(spans);
        ActivitySource.AddActivityListener(listener);

        var p = new FakeLlmProvider("p1");
        p.Replies.Enqueue(new LlmReply("", LlmVerdict.Timeout, Detail: "too slow"));
        await Router(p).CompleteAsync([new("p1", "m-span-err")], Req);

        var span = Assert.Single(SpansFor(spans, "m-span-err"));
        Assert.Equal("Timeout", span.GetTagItem("error.type"));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
    }

    [Fact]
    public async Task Duration_and_token_metrics_are_recorded()
    {
        var durations = 0;
        var tokenRecords = new List<(long Value, string? Type)>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == LyntaiDiagnostics.MeterName) l.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<double>((inst, _, tags, _) =>
        {
            if (inst.Name == "gen_ai.client.operation.duration" && HasModel(tags, "m-metrics"))
                Interlocked.Increment(ref durations);
        });
        meterListener.SetMeasurementEventCallback<long>((inst, value, tags, _) =>
        {
            if (inst.Name != "gen_ai.client.token.usage" || !HasModel(tags, "m-metrics")) return;
            string? type = null;
            foreach (var tag in tags) if (tag.Key == "gen_ai.token.type") type = (string?)tag.Value;
            lock (tokenRecords) tokenRecords.Add((value, type));
        });
        meterListener.Start();

        var p = new FakeLlmProvider("p1");
        p.Replies.Enqueue(new LlmReply("hi", LlmVerdict.Ok, new LlmUsage(7, 3)));
        await Router(p).CompleteAsync([new("p1", "m-metrics")], Req);

        Assert.Equal(1, durations);
        lock (tokenRecords)
        {
            Assert.Contains((7L, "input"), tokenRecords);
            Assert.Contains((3L, "output"), tokenRecords);
        }
    }

    [Fact]
    public async Task Streaming_records_time_to_first_chunk()
    {
        var firstChunkRecords = 0;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == LyntaiDiagnostics.MeterName &&
                instrument.Name == "gen_ai.client.operation.time_to_first_chunk")
                l.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
        {
            if (HasModel(tags, "m-ttfc")) Interlocked.Increment(ref firstChunkRecords);
        });
        meterListener.Start();

        var p = new FakeLlmProvider("p1")
        {
            StreamScript = _ => [LlmChunk.Content("a"), LlmChunk.Content("b"), LlmChunk.Final()],
        };
        await foreach (var _ in Router(p).StreamAsync([new("p1", "m-ttfc")], Req)) { }

        Assert.Equal(1, firstChunkRecords); // once per stream, at the fallback point of no return
    }

    private static bool HasModel(ReadOnlySpan<KeyValuePair<string, object?>> tags, string model)
    {
        foreach (var tag in tags)
            if (tag.Key == "gen_ai.request.model" && Equals(tag.Value, model)) return true;
        return false;
    }
}
