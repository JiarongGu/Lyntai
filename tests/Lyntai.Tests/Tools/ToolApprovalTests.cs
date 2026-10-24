using Lyntai.Agents;
using Lyntai.Guards;
using Lyntai.Inference;
using Lyntai.Tests.Fakes;
using Lyntai.Tools.Mcp;
using Lyntai.Tools.Mcp.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace Lyntai.Tests.Tools;

/// <summary><see cref="IToolApprover"/>: one seam consulted before a tool runs, on every door onto the app's
/// tools — the native and prompt tool loop and the hosted MCP endpoint — through <see cref="ToolInvocation"/>.
/// Every approver must approve; a refusal, or an approver that throws, is a tool result the model sees and the
/// loop goes on (<c>docs/DECISIONS.md</c> D210).</summary>
public sealed class ToolApprovalTests
{
    private sealed class RecordingApprover(Func<ToolApprovalRequest, ToolApprovalResult?> decide) : IToolApprover
    {
        public List<ToolApprovalRequest> Seen { get; } = [];

        public Task<ToolApprovalResult> ApproveAsync(ToolApprovalRequest request, CancellationToken ct = default)
        {
            lock (Seen) Seen.Add(request);
            return Task.FromResult(decide(request)!);
        }
    }

    private sealed class ThrowingApprover : IToolApprover
    {
        public Task<ToolApprovalResult> ApproveAsync(ToolApprovalRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("approval service unreachable");
    }

    private static RecordingApprover Approving() => new(_ => ToolApprovalResult.Approve);

    private static RecordingApprover Denying(string reason) => new(_ => ToolApprovalResult.Deny(reason));

    private static (FunctionTool Tool, Func<int> Runs) Counted(string name = "write_file")
    {
        var runs = 0;
        var tool = new FunctionTool(name, (args, _) => { Interlocked.Increment(ref runs); return Task.FromResult($"wrote:{args}"); });
        return (tool, () => runs);
    }

    // ---- the shared flow ------------------------------------------------------------------------------------

    [Fact]
    public async Task With_no_approver_the_tool_runs_as_before()
    {
        var (tool, runs) = Counted();

        var result = await ToolInvocation.InvokeGatedAsync(tool, "{}", guards: null, approvers: [], consumer: "study");

        Assert.Equal(1, runs());
        Assert.False(result.Denied);
        Assert.False(result.Blocked);
        Assert.Equal("wrote:{}", result.Observation);
    }

    [Fact]
    public async Task A_denial_is_an_error_observation_carrying_the_reason_and_the_tool_never_runs()
    {
        var (tool, runs) = Counted();

        var result = await ToolInvocation.InvokeGatedAsync(tool, "{}", null, [Denying("needs a human")], "study");

        Assert.Equal(0, runs());
        Assert.True(result.Denied);
        Assert.False(result.Blocked);                       // not a jail violation: no door ends its run on it
        Assert.Equal("needs a human", result.Reason);
        Assert.Equal(ToolObservations.Error("tool call refused by approval: needs a human"), result.Observation);
    }

    [Fact]
    public async Task Every_approver_must_approve_and_the_first_denial_short_circuits()
    {
        var (tool, runs) = Counted();
        var first = Approving();
        var second = Denying("over budget");
        var third = Approving();

        var result = await ToolInvocation.InvokeGatedAsync(tool, "{}", null, [first, second, third], "study");

        Assert.True(result.Denied);
        Assert.Equal("over budget", result.Reason);
        Assert.Single(first.Seen);
        Assert.Single(second.Seen);
        Assert.Empty(third.Seen);                           // registration order; the first refusal decides
        Assert.Equal(0, runs());

        var all = await ToolInvocation.InvokeGatedAsync(tool, "{}", null, [Approving(), Approving()], "study");
        Assert.False(all.Denied);
        Assert.Equal(1, runs());
    }

    [Fact]
    public async Task A_throwing_or_silent_approver_fails_closed_as_a_denial_and_is_logged()
    {
        var (tool, runs) = Counted();
        var log = new CapturingLogger();

        var threw = await ToolInvocation.InvokeGatedAsync(tool, "{}", null, [new ThrowingApprover()], "study", logger: log);
        var silent = await ToolInvocation.InvokeGatedAsync(tool, "{}", null, [new RecordingApprover(_ => null)], "study");

        Assert.Equal(0, runs());
        Assert.True(threw.Denied);
        Assert.True(ToolObservations.IsError(threw.Observation));
        Assert.DoesNotContain("unreachable", threw.Observation);   // the exception's text never reaches the model
        Assert.True(silent.Denied);
        Assert.Contains(log.Warnings, w => w.Contains("write_file"));
    }

    [Fact]
    public async Task A_caller_cancel_during_approval_propagates()
    {
        var (tool, runs) = Counted();
        using var cts = new CancellationTokenSource();
        var cancelling = new RecordingApprover(_ =>
        {
            cts.Cancel();
            cts.Token.ThrowIfCancellationRequested();
            return ToolApprovalResult.Approve;
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ToolInvocation.InvokeGatedAsync(tool, "{}", null, [cancelling], "study", ct: cts.Token));
        Assert.Equal(0, runs());
    }

    [Fact]
    public async Task The_approver_sees_the_arguments_a_guard_rewrote_the_consumer_and_the_call_id()
    {
        var (tool, _) = Counted();
        var approver = Approving();

        await ToolInvocation.InvokeGatedAsync(tool, """{"path":"secret"}""", new RewritingRail("""{"path":"safe"}"""),
            [approver], "study", callId: "call_7");

        var seen = Assert.Single(approver.Seen);
        Assert.Equal("write_file", seen.ToolName);
        Assert.Equal("""{"path":"safe"}""", seen.ArgumentsJson);   // what will actually run
        Assert.Equal("study", seen.Consumer);
        Assert.Equal("call_7", seen.CallId);
    }

    [Fact]
    public async Task A_denial_is_logged_with_its_reason()
    {
        var (tool, _) = Counted();
        var log = new CapturingLogger();

        await ToolInvocation.InvokeGatedAsync(tool, "{}", null, [Denying("needs a human")], "study", logger: log);

        Assert.Contains(log.Entries, e => e.Level == LogLevel.Information
            && e.Message.Contains("write_file") && e.Message.Contains("needs a human"));
    }

    // ---- the tool loop: native and prompt ---------------------------------------------------------------------

    private static TextRequest Ask(string consumer = "study") =>
        new() { Messages = [TextMessage.User("save it")], Consumer = consumer };

    private static ToolLoop Loop(FakeTextClient client, IToolApprover approver, params ITool[] tools) =>
        new(client, new ToolRegistry(tools), new LyntaiOptions(), logger: null, guards: null, selector: null,
            approvers: [approver]);

    [Fact]
    public async Task Native_a_denial_is_a_tool_result_and_the_loop_goes_on_to_its_answer()
    {
        var (tool, runs) = Counted();
        var approver = Denying("read-only session");
        var client = new FakeTextClient { SupportsToolCallsResult = true };
        client.Replies.Enqueue(new TextResponse("", ProviderVerdict.Ok)
            { ToolCalls = [new TextToolCall("call_1", "write_file", """{"x":1}""")] });
        client.Replies.Enqueue(new TextResponse("I could not save it.", ProviderVerdict.Ok));

        var events = await Loop(client, approver, tool).StreamAsync(Ask()).ToListAsync();

        Assert.Equal(0, runs());
        var result = Assert.Single(events.OfType<ToolResult>());
        Assert.Equal("call_1", result.CallId);
        Assert.True(result.IsError);
        Assert.Contains("read-only session", result.Content);
        var ended = Assert.Single(events.OfType<SessionEnded>());
        Assert.Equal(ProviderVerdict.Ok, ended.Verdict);
        Assert.Equal("I could not save it.", ended.FinalText);
        // the model was told: the refusal went back as the call's tool-role message
        Assert.Contains(client.Calls[1].Messages, m => m.Role == "tool" && m.Content.Contains("read-only session"));
        var seen = Assert.Single(approver.Seen);
        Assert.Equal("study", seen.Consumer);
        Assert.Equal("call_1", seen.CallId);
    }

    [Fact]
    public async Task Prompt_a_denial_is_a_tool_result_and_the_loop_goes_on_to_its_answer()
    {
        var (tool, runs) = Counted();
        var approver = Denying("read-only session");
        var client = new FakeTextClient();
        client.Replies.Enqueue(new TextResponse("""{"tool":"write_file","arguments":{"x":1}}""", ProviderVerdict.Ok));
        client.Replies.Enqueue(new TextResponse("""{"final":"not saved"}""", ProviderVerdict.Ok));

        var run = await Loop(client, approver, tool).RunAsync(Ask("notes"));

        Assert.Equal(0, runs());
        Assert.True(run.Ok);
        Assert.Equal("not saved", run.Answer);
        var step = Assert.Single(run.Steps);
        Assert.Contains("read-only session", step.Result);
        Assert.Contains(client.Calls[1].Messages, m => m.Role == "user" && m.Content.Contains("read-only session"));
        var seen = Assert.Single(approver.Seen);
        Assert.Equal("notes", seen.Consumer);
        Assert.Null(seen.CallId);                           // the prompt protocol carries no call id
    }

    [Fact]
    public async Task An_unknown_tool_is_not_put_to_the_approver()
    {
        var approver = Approving();
        var client = new FakeTextClient();
        client.Replies.Enqueue(new TextResponse("""{"tool":"nope","arguments":{}}""", ProviderVerdict.Ok));
        client.Replies.Enqueue(new TextResponse("""{"final":"ok"}""", ProviderVerdict.Ok));

        var run = await Loop(client, approver, Counted().Tool).RunAsync(Ask());

        Assert.Contains("unknown tool", Assert.Single(run.Steps).Result);
        Assert.Empty(approver.Seen);                        // nothing would run, so nothing is asked
    }

    [Fact]
    public async Task The_registered_tool_loop_consults_every_registered_approver()
    {
        var (tool, runs) = Counted();
        var approver = Denying("not on weekends");
        var provider = new FakeTextProvider("p");
        provider.Replies.Enqueue(new TextResponse("""{"tool":"write_file","arguments":{}}""", ProviderVerdict.Ok));
        provider.Replies.Enqueue(new TextResponse("""{"final":"skipped"}""", ProviderVerdict.Ok));

        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => provider)
            .AddTool(_ => tool)
            .AddToolApprover(_ => approver)
            .UseDefaultCandidates("p"));
        using var sp = services.BuildServiceProvider();

        var run = await sp.GetRequiredService<IToolLoop>().RunAsync(Ask());

        Assert.True(run.Ok);
        Assert.Equal(0, runs());
        Assert.Single(approver.Seen);
    }

    // ---- the hosted MCP endpoint ------------------------------------------------------------------------------

    private static async Task<string> CallHostedAsync(McpEndpoint endpoint, string tool)
    {
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(endpoint.Url),
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {endpoint.AuthToken}" },
        }));
        var hosted = Assert.Single(await McpToolset.FromClientAsync(client), t => t.Name == tool);
        return await hosted.InvokeAsync("{}");
    }

    private sealed class Connector : IMcpCliConnector
    {
        public string ProviderId => "fake-cli";
        public McpEndpoint? Seen { get; private set; }

        public ValueTask<IReadOnlyList<string>> BuildArgsAsync(McpCliContext context, CancellationToken ct = default)
        {
            Seen = context.Endpoint;
            return ValueTask.FromResult<IReadOnlyList<string>>(["--tools"]);
        }
    }

    [Fact]
    public async Task The_mcp_door_reports_a_denial_as_the_tools_result_and_never_runs_it()
    {
        var (tool, runs) = Counted("write_file_mcp");
        var approver = Denying("needs a human");
        const string token = "approval-bearer";
        await using var host = await McpToolHost.StartAsync([tool], token, approvers: [approver], consumer: "study");

        var observation = await CallHostedAsync(new McpEndpoint(host.Url, token, "lyntai"), "write_file_mcp");

        Assert.Equal(0, runs());
        Assert.Contains("needs a human", observation);
        var seen = Assert.Single(approver.Seen);
        Assert.Equal("study", seen.Consumer);
        Assert.Equal("write_file_mcp", seen.ToolName);
    }

    [Fact]
    public async Task The_registered_mcp_host_puts_each_call_to_the_approvers_under_the_requests_consumer()
    {
        var (tool, runs) = Counted("write_file_hosted");
        var approver = new RecordingApprover(r => r.Consumer == "study"
            ? ToolApprovalResult.Approve
            : ToolApprovalResult.Deny("only study may write"));
        var connector = new Connector();

        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeTextProvider("p"))
            .AddTool(_ => tool)
            .AddToolApprover(_ => approver)
            .AddMcpToolHost(connector)
            .UseDefaultCandidates("p"));
        using var sp = services.BuildServiceProvider();
        var provisioner = sp.GetRequiredKeyedService<ICliToolProvisioner>("fake-cli");

        static CliToolRequest For(string consumer) =>
            new(new TextRequest { Messages = [TextMessage.User("hi")], Consumer = consumer }, "fake-cli");

        await using (await provisioner.ProvisionAsync(For("memory")))
            Assert.Contains("only study may write", await CallHostedAsync(connector.Seen!, "write_file_hosted"));
        Assert.Equal(0, runs());

        await using (await provisioner.ProvisionAsync(For("study")))
            Assert.Equal("wrote:{}", await CallHostedAsync(connector.Seen!, "write_file_hosted"));
        Assert.Equal(1, runs());
        Assert.Equal(["memory", "study"], approver.Seen.Select(s => s.Consumer));
    }

    private sealed class RewritingRail(string replacement) : IGuardRail
    {
        public Task<GuardOutcome> InspectRequestAsync(TextRequest req, CancellationToken ct = default) =>
            Task.FromResult(GuardOutcome.Allow);

        public Task<GuardOutcome> InspectResponseAsync(TextResponse reply, CancellationToken ct = default) =>
            Task.FromResult(GuardOutcome.Allow);

        public Task<GuardOutcome> InspectToolCallAsync(string toolName, string argumentsJson, CancellationToken ct = default) =>
            Task.FromResult(GuardOutcome.Replace(replacement));
    }
}
