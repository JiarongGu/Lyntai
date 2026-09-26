using System.Diagnostics;
using System.Diagnostics.Metrics;
using Lyntai.Agents;
using Lyntai.Diagnostics;
using Lyntai.Guards;
using Lyntai.Inference;
using Lyntai.Tools.Mcp;
using Lyntai.Tools.Mcp.Hosting;
using ModelContextProtocol.Client;

namespace Lyntai.Tests.Tools;

/// <summary>The ONE gated-invocation flow both doors onto the app's tools share: through
/// <see cref="ToolInvocation"/> the MCP door records the same span and counter as the tool loop. Tool names
/// are unique per test: the listeners are process-global.</summary>
public sealed class ToolInvocationTests
{
    [Fact]
    public async Task The_mcp_door_records_the_tool_span_and_the_invocation_counter()
    {
        var spans = new List<Activity>();
        using var spanListener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == LyntaiDiagnostics.AgentActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => { lock (spans) spans.Add(a); },
        };
        ActivitySource.AddActivityListener(spanListener);
        var counted = 0;
        using var meter = new MeterListener
        {
            InstrumentPublished = (inst, l) =>
            {
                if (inst.Meter.Name == LyntaiDiagnostics.AgentMeterName && inst.Name == "lyntai.tool.invocations")
                    l.EnableMeasurementEvents(inst);
            },
        };
        meter.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var t in tags)
                if (t.Key == "lyntai.tool.name" && Equals(t.Value, "echo-mcp-door")) Interlocked.Increment(ref counted);
        });
        meter.Start();

        ITool echo = new FunctionTool("echo-mcp-door", (args, _) => Task.FromResult($"echoed:{args}"), "echoes",
            """{"type":"object","properties":{}}""");
        const string token = "test-bearer-token";
        await using var host = await McpToolHost.StartAsync([echo], token);
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(host.Url),
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        }));
        var tool = Assert.Single(await McpToolset.FromClientAsync(client));

        await tool.InvokeAsync("{}");

        lock (spans) Assert.Single(spans, s => s.DisplayName == "execute_tool echo-mcp-door");
        Assert.Equal(1, counted);
    }

    [Fact]
    public async Task A_block_carries_the_guards_reason_and_an_observation_naming_the_gate()
    {
        var ran = false;
        var tool = new FunctionTool("gated", (_, _) => { ran = true; return Task.FromResult("payload"); });

        var result = await ToolInvocation.InvokeGatedAsync(tool, "{}", new BlockingRail("not today"));

        Assert.False(ran);
        Assert.True(result.Blocked);
        Assert.Equal("not today", result.Reason);
        Assert.Equal("error: tool call blocked by guard: not today", result.Observation);
        Assert.True(ToolObservations.IsError(result.Observation));
    }

    [Fact]
    public async Task A_throwing_tool_becomes_an_error_observation_and_a_caller_cancel_propagates()
    {
        var boom = new FunctionTool("boom-flow", (_, _) => throw new InvalidOperationException("no such record"));
        var result = await ToolInvocation.InvokeGatedAsync(boom, "{}", guards: null);
        Assert.Equal(ToolObservations.Error("no such record"), result.Observation);

        using var cts = new CancellationTokenSource();
        var cancelling = new FunctionTool("cancel-flow", (_, ct) =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult("unreached");
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ToolInvocation.InvokeGatedAsync(cancelling, "{}", guards: null, ct: cts.Token));
    }

    private sealed class BlockingRail(string reason) : IGuardRail
    {
        public Task<GuardOutcome> InspectRequestAsync(TextRequest req, CancellationToken ct = default) =>
            Task.FromResult(GuardOutcome.Block(reason));

        public Task<GuardOutcome> InspectResponseAsync(TextResponse reply, CancellationToken ct = default) =>
            Task.FromResult(GuardOutcome.Allow);
    }
}
