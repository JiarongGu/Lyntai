using System.Diagnostics;
using System.Diagnostics.Metrics;
using Lyntai;
using Lyntai.Agents;
using Lyntai.Diagnostics;
using Lyntai.Guards;
using Lyntai.Jobs;
using Lyntai.Llm;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Jobs;

namespace Lyntai.Tests.Core;

/// <summary>The agentic telemetry surface (source/meter "Lyntai.Agents"): tool-loop + per-tool spans,
/// job spans/metrics, guard-decision counters. Every test tags its work uniquely (consumer / tool name /
/// lane / guard name) — the listeners are process-global and other agentic tests run concurrently.</summary>
public class AgentDiagnosticsTests
{
    private static ActivityListener SpanListener(List<Activity> sink) => new()
    {
        ShouldListenTo = s => s.Name == LyntaiDiagnostics.AgentActivitySourceName,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        ActivityStopped = a => { lock (sink) sink.Add(a); },
    };

    private static List<Activity> SpansWith(List<Activity> spans, string tag, object value)
    {
        lock (spans) return [.. spans.Where(s => Equals(s.GetTagItem(tag), value))];
    }

    // ---- tool loop -----------------------------------------------------------------------------------

    [Fact]
    public async Task Tool_loop_emits_a_loop_span_and_a_span_per_tool_call()
    {
        var spans = new List<Activity>();
        using var listener = SpanListener(spans);
        ActivitySource.AddActivityListener(listener);

        var client = new FakeLlmClient();
        client.Replies.Enqueue(new LlmReply("""{"tool":"echo-tl","arguments":{}}""", LlmVerdict.Ok));
        client.Replies.Enqueue(new LlmReply("""{"final":"done"}""", LlmVerdict.Ok));
        var tool = new FunctionTool("echo-tl", (a, _) => Task.FromResult($"observed:{a}"), "echoes");
        var loop = new ToolLoop(client, new ToolRegistry([tool]), new LyntaiOptions());

        await loop.RunAsync(new LlmRequest { Consumer = "tl-consumer", Messages = [LlmMessage.User("go")] });

        var loopSpan = Assert.Single(SpansWith(spans, "lyntai.consumer", "tl-consumer"));
        Assert.Equal("tool_loop", loopSpan.DisplayName);
        Assert.Equal("prompt", loopSpan.GetTagItem("lyntai.tool_loop.mode"));
        Assert.Equal(1, loopSpan.GetTagItem("lyntai.tool_loop.steps"));

        var toolSpan = Assert.Single(SpansWith(spans, "lyntai.tool.name", "echo-tl"));
        Assert.Equal("execute_tool echo-tl", toolSpan.DisplayName);
        // the tool call nests under the loop span
        Assert.Equal(loopSpan.SpanId, toolSpan.ParentSpanId);
    }

    [Fact]
    public async Task Tool_invocation_counter_tags_the_error_flag()
    {
        var invocations = new List<(string? Name, bool Error)>();
        using var meter = AgentMeterListener("lyntai.tool.invocations", (value, tags) =>
        {
            string? name = null;
            var error = false;
            foreach (var t in tags)
            {
                if (t.Key == "lyntai.tool.name") name = (string?)t.Value;
                if (t.Key == "error") error = (bool)t.Value!;
            }
            lock (invocations) invocations.Add((name, error));
        });

        var client = new FakeLlmClient();
        client.Replies.Enqueue(new LlmReply("""{"tool":"boom-tl","arguments":{}}""", LlmVerdict.Ok));
        client.Replies.Enqueue(new LlmReply("""{"final":"handled"}""", LlmVerdict.Ok));
        var tool = new FunctionTool("boom-tl", (_, _) => throw new InvalidOperationException("kaboom"));
        await new ToolLoop(client, new ToolRegistry([tool]), new LyntaiOptions())
            .RunAsync(new LlmRequest { Messages = [LlmMessage.User("go")] });

        lock (invocations) Assert.Contains(("boom-tl", true), invocations); // throwing tool → error=true
    }

    // ---- durable jobs --------------------------------------------------------------------------------

    [Fact]
    public async Task Job_run_emits_a_span_and_processed_metric_tagged_by_outcome()
    {
        var spans = new List<Activity>();
        using var listener = SpanListener(spans);
        ActivitySource.AddActivityListener(listener);

        var processed = new List<(string? Lane, string? Outcome)>();
        using var meter = AgentMeterListener("lyntai.jobs.processed", (_, tags) =>
        {
            string? lane = null, outcome = null;
            foreach (var t in tags)
            {
                if (t.Key == "lyntai.job.lane") lane = (string?)t.Value;
                if (t.Key == "lyntai.job.outcome") outcome = (string?)t.Value;
            }
            lock (processed) processed.Add((lane, outcome));
        });

        var clock = new MutableClock();
        var store = new InMemoryJobStore(clock.Get);
        var options = new LyntaiOptions();
        var handler = new FakeJobHandler("greet-job", _ => Task.FromResult(JobOutcome.Complete));
        var runner = new JobRunner(store, new JobHandlerRegistry([handler]), options, clock: clock.Get);
        await new JobQueue(store, options).EnqueueAsync("lane-tel", "greet-job", "{}");

        await runner.RunOnceAsync();

        var span = Assert.Single(SpansWith(spans, "lyntai.job.lane", "lane-tel"));
        Assert.Equal("run_job greet-job", span.DisplayName);
        Assert.Equal("succeeded", span.GetTagItem("lyntai.job.outcome"));
        lock (processed) Assert.Contains(("lane-tel", "succeeded"), processed);
    }

    // ---- guards --------------------------------------------------------------------------------------

    [Fact]
    public async Task Guard_block_records_a_decision_tagged_by_gate_and_name()
    {
        var decisions = new List<(string? Gate, string? Name, string? Result)>();
        using var meter = AgentMeterListener("lyntai.guard.decisions", (_, tags) =>
        {
            string? gate = null, name = null, result = null;
            foreach (var t in tags)
            {
                if (t.Key == "lyntai.guard.gate") gate = (string?)t.Value;
                if (t.Key == "lyntai.guard.name") name = (string?)t.Value;
                if (t.Key == "lyntai.guard.result") result = (string?)t.Value;
            }
            lock (decisions) decisions.Add((gate, name, result));
        });

        var rail = new GuardRail([new BlockGuard("guard-tel")]);
        await rail.InspectRequestAsync(new LlmRequest { Messages = [LlmMessage.User("hi")] });

        lock (decisions) Assert.Contains(("input", "guard-tel", "block"), decisions);
    }

    private sealed class BlockGuard(string name) : IGuard
    {
        public string Name => name;
        public Task<GuardOutcome> InspectRequestAsync(LlmRequest req, CancellationToken ct = default) =>
            Task.FromResult(GuardOutcome.Block("nope"));
    }

    // ---- helper --------------------------------------------------------------------------------------

    /// <summary>A MeterListener scoped to the Lyntai.Agents meter that invokes <paramref name="onMeasure"/>
    /// for the named instrument (long counters and double histograms both routed through it).</summary>
    private static MeterListener AgentMeterListener(string instrument,
        Action<double, ReadOnlySpan<KeyValuePair<string, object?>>> onMeasure)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (inst, l) =>
            {
                if (inst.Meter.Name == LyntaiDiagnostics.AgentMeterName && inst.Name == instrument)
                    l.EnableMeasurementEvents(inst);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) => onMeasure(value, tags));
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) => onMeasure(value, tags));
        listener.Start();
        return listener;
    }
}
