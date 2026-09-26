using Lyntai.Agents;
using Lyntai.Tools.Mcp;
using Lyntai.Tools.Mcp.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using Lyntai.Inference;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Tools;

/// <summary>
/// Deterministic proof of the GENERIC CLI tool-hosting path WITHOUT any real CLI binary: the in-process
/// MCP host exposes the app's ITools over HTTP, and we connect with Lyntai's OWN MCP client (the exact
/// thing a CLI's agent does) to list + call them. Also covers the provider-neutral half of the
/// provisioner — endpoint hand-off to the dialect and temp-file lifecycle. Vendor-specific flags live in
/// <see cref="ClaudeCliMcpConnectorTests"/>.
/// </summary>
public class McpToolHostTests
{
    [Fact]
    public async Task Host_exposes_registered_ITools_over_http_and_executes_them()
    {
        var received = "";
        ITool echo = new FunctionTool("echo",
            (args, _) => { received = args; return Task.FromResult($"echoed:{args}"); },
            "echoes its message",
            """{"type":"object","properties":{"message":{"type":"string"}},"required":["message"]}""");

        const string token = "test-bearer-token";
        await using var host = await McpToolHost.StartAsync([echo], token);
        Assert.StartsWith("http://127.0.0.1:", host.Url);
        Assert.EndsWith("/mcp", host.Url);

        // connect exactly as the CLI would: an MCP client over the hosted HTTP endpoint, with the bearer
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(host.Url),
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });
        await using var client = await McpClient.CreateAsync(transport);
        var tools = await McpToolset.FromClientAsync(client);

        var tool = Assert.Single(tools);
        Assert.Equal("echo", tool.Name);

        var result = await tool.InvokeAsync("""{"message":"hi"}""");

        Assert.Contains("echoed", result);          // the server returned the tool's output
        Assert.Contains("hi", received);            // the in-process ITool actually ran with the model's args
    }

    [Fact]
    public async Task A_guard_blocks_a_hosted_tool_call_the_same_way_it_blocks_one_in_the_tool_loop()
    {
        // THE JAIL. The SAME ITool instances (sp.GetServices<ITool>()) are reachable through IToolLoop and
        // through the hosted endpoint the CLI's own agent calls, so a guard enforced on one path and not the
        // other is silently absent there. Neither ChatOrchestrator gate can see a hosted call either: gate 1
        // sees the user message, gate 2 only the final answer.
        var ran = false;
        ITool secret = new FunctionTool("read_secret",
            (_, _) => { ran = true; return Task.FromResult("SECRET_KEY=hunter2"); },
            "reads a secret",
            """{"type":"object","properties":{}}""");

        const string token = "test-bearer-token";
        await using var host = await McpToolHost.StartAsync([secret], token, guards: new BlockingRail());

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(host.Url),
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });
        await using var client = await McpClient.CreateAsync(transport);
        var tool = Assert.Single(await McpToolset.FromClientAsync(client));

        var result = await tool.InvokeAsync("""{}""");

        Assert.False(ran);                              // the tool did not execute at all
        Assert.DoesNotContain("hunter2", result);       // and the model never saw the payload
        Assert.Contains("blocked", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_hosted_block_is_LOGGED_the_way_the_tool_loop_logs_its_own()
    {
        // The signal half of the two-doors problem. Both doors emit the guard-decision COUNTER (IGuardRail
        // records it), and ToolLoop also LOGS both of its block paths at Information — so this door must too,
        // or an operator reading logs sees a refusal from one door and silence from the other. The difference
        // in FORCE between the doors is deliberate (ToolFunction's own remarks say why); a difference in
        // SIGNAL is not. `docs/DECISIONS.md` D75.
        var logs = new List<string>();
        ITool secret = new FunctionTool("read_secret",
            (_, _) => Task.FromResult("SECRET_KEY=hunter2"), "reads a secret",
            """{"type":"object","properties":{}}""");

        const string token = "test-bearer-token";
        await using var host = await McpToolHost.StartAsync(
            [secret], token, guards: new BlockingRail(), logger: new CapturingLogger(logs, LogLevel.Trace));

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(host.Url),
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });
        await using var client = await McpClient.CreateAsync(transport);
        var tool = Assert.Single(await McpToolset.FromClientAsync(client));

        await tool.InvokeAsync("""{}""");

        var line = Assert.Single(logs);
        Assert.Contains("guard blocked", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("read_secret", line, StringComparison.Ordinal);   // WHICH tool, or the line is unactionable
    }

    [Fact]
    public async Task A_THROWING_tool_becomes_an_error_observation_on_this_door_too()
    {
        // `ITool.InvokeAsync` promises every implementer: "Throwing is tolerated — the loop turns a thrown
        // message into an error observation so the model can recover". That is a promise about the TOOL
        // seam, not about one caller, and this endpoint executes the same ITool instances the in-process
        // loop does. Left bare, a BYO tool throwing HttpRequestException or KeyNotFoundException escapes
        // Lyntai entirely: whatever the model sees is the MCP SDK's choice, with no Lyntai log and no
        // `error:` prefix that ToolObservations.IsError recognises. Same second-door argument this class's
        // own remarks make for guards.
        var logs = new List<string>();
        ITool boom = new FunctionTool("boom",
            (_, _) => throw new KeyNotFoundException("no such record"), "throws",
            """{"type":"object","properties":{}}""");

        const string token = "test-bearer-token";
        await using var host = await McpToolHost.StartAsync(
            [boom], token, logger: new CapturingLogger(logs, LogLevel.Trace));

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(host.Url),
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });
        await using var client = await McpClient.CreateAsync(transport);
        var tool = Assert.Single(await McpToolset.FromClientAsync(client));

        var result = (await tool.InvokeAsync("""{}""")).ToString() ?? string.Empty;

        Assert.Contains("error:", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no such record", result, StringComparison.Ordinal);
        Assert.Contains(logs, l => l.Contains("boom", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_guard_redacts_a_hosted_tools_OBSERVATION_before_the_model_sees_it()
    {
        // The output gate. Blocking the call is only half: a tool that legitimately runs can still return
        // something a guard must not let out, which is why IGuardRail has InspectToolResultAsync at all.
        ITool leaky = new FunctionTool("read_file",
            (_, _) => Task.FromResult("token=hunter2"),
            "reads a file",
            """{"type":"object","properties":{}}""");

        const string token = "test-bearer-token";
        await using var host = await McpToolHost.StartAsync([leaky], token, guards: new RedactingRail());

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(host.Url),
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });
        await using var client = await McpClient.CreateAsync(transport);
        var tool = Assert.Single(await McpToolset.FromClientAsync(client));

        var result = await tool.InvokeAsync("""{}""");

        Assert.DoesNotContain("hunter2", result);
        Assert.Contains("[redacted]", result, StringComparison.Ordinal);
    }

    private sealed class BlockingRail : Lyntai.Guards.IGuardRail
    {
        public Task<Lyntai.Guards.GuardOutcome> InspectRequestAsync(Lyntai.Inference.TextRequest req, CancellationToken ct = default) =>
            Task.FromResult(Lyntai.Guards.GuardOutcome.Block("blocked by policy"));

        public Task<Lyntai.Guards.GuardOutcome> InspectResponseAsync(Lyntai.Inference.TextResponse reply, CancellationToken ct = default) =>
            Task.FromResult(Lyntai.Guards.GuardOutcome.Allow);
    }

    private sealed class RedactingRail : Lyntai.Guards.IGuardRail
    {
        public Task<Lyntai.Guards.GuardOutcome> InspectRequestAsync(Lyntai.Inference.TextRequest req, CancellationToken ct = default) =>
            Task.FromResult(Lyntai.Guards.GuardOutcome.Allow);

        public Task<Lyntai.Guards.GuardOutcome> InspectResponseAsync(Lyntai.Inference.TextResponse reply, CancellationToken ct = default) =>
            Task.FromResult(Lyntai.Guards.GuardOutcome.Replace("[redacted]"));
    }

    [Theory]
    [InlineData(null)]                       // no Authorization header
    [InlineData("Bearer the-wrong-token")]   // a token, but not this host's
    [InlineData("the-real-token")]           // the right secret without its scheme
    public async Task Host_rejects_a_request_without_its_exact_bearer_token(string? authorization)
    {
        var ran = false;
        ITool echo = new FunctionTool("echo", (a, _) => { ran = true; return Task.FromResult(a); });
        await using var host = await McpToolHost.StartAsync([echo], "the-real-token");

        // raw HTTP rather than an MCP client, so the assertion is the host's 401 and not whatever a client
        // happens to throw on a failed handshake
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, host.Url)
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"echo","arguments":{}}}""",
                System.Text.Encoding.UTF8, "application/json"),
        };
        if (authorization is not null) request.Headers.TryAddWithoutValidation("Authorization", authorization);

        using var response = await http.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(ran);
    }

    [Fact]
    public async Task Provisioner_with_no_tools_is_a_noop()
    {
        var dialect = new RecordingDialect();
        await using var session = await new McpToolHostProvisioner([], dialect, new McpToolHostOptions()).ProvisionAsync();

        Assert.Empty(session.ExtraArgs);  // no host, no CLI args — the CLI runs exactly as before
        Assert.Null(dialect.SeenEndpoint); // and the dialect is never consulted
    }

    [Fact]
    public async Task Provisioner_hands_the_dialect_a_live_endpoint_and_returns_its_args()
    {
        ITool echo = new FunctionTool("echo", (a, _) => Task.FromResult(a));
        var dialect = new RecordingDialect();

        await using var session = await new McpToolHostProvisioner([echo], dialect, new McpToolHostOptions())
            .ProvisionAsync();

        Assert.NotNull(dialect.SeenEndpoint);
        Assert.StartsWith("http://127.0.0.1:", dialect.SeenEndpoint!.Url);
        Assert.NotEmpty(dialect.SeenEndpoint.AuthToken);
        Assert.Equal("lyntai", dialect.SeenEndpoint.ServerName);        // the default server name
        Assert.Equal(["--fake", "arg"], session.ExtraArgs);             // the dialect's args are passed through verbatim
    }

    [Fact]
    public async Task Server_name_is_configurable_not_hard_coded()
    {
        ITool echo = new FunctionTool("echo", (a, _) => Task.FromResult(a));
        var dialect = new RecordingDialect();
        var options = new McpToolHostOptions { ServerName = "my-tools" };

        await using var session = await new McpToolHostProvisioner([echo], dialect, options).ProvisionAsync();

        Assert.Equal("my-tools", dialect.SeenEndpoint!.ServerName);
    }

    [Fact]
    public async Task Provisioner_cleans_up_every_temp_file_the_dialect_wrote()
    {
        ITool echo = new FunctionTool("echo", (a, _) => Task.FromResult(a));
        var dialect = new RecordingDialect { TempFiles = { ["one"] = "{}", ["two"] = "{}" } };

        IReadOnlyList<string> written;
        await using (await new McpToolHostProvisioner([echo], dialect, new McpToolHostOptions()).ProvisionAsync())
        {
            written = dialect.WrittenPaths;
            Assert.Equal(2, written.Count);
            Assert.All(written, p => Assert.True(File.Exists(p), $"{p} should exist while the session is live"));
        }
        Assert.All(written, p => Assert.False(File.Exists(p), $"{p} should be deleted on session dispose"));
    }

    [Fact]
    public async Task Provisioner_tears_the_host_and_temp_files_down_when_the_dialect_throws()
    {
        ITool echo = new FunctionTool("echo", (a, _) => Task.FromResult(a));
        var dialect = new RecordingDialect { TempFiles = { ["one"] = "{}" }, Throw = true };
        var provisioner = new McpToolHostProvisioner([echo], dialect, new McpToolHostOptions());

        await Assert.ThrowsAsync<InvalidOperationException>(() => provisioner.ProvisionAsync());

        // a half-provisioned session must not leak the temp file it already wrote
        Assert.Single(dialect.WrittenPaths);   // guard: an empty list would make the Assert.All below vacuous
        Assert.All(dialect.WrittenPaths, p => Assert.False(File.Exists(p)));
    }

    /// <summary>A stand-in for a vendor connector: records what the provisioner handed it, writes whatever
    /// temp files it was told to, and returns fixed args.</summary>
    private sealed class RecordingDialect : IMcpCliConnector
    {
        private readonly List<string> _written = [];

        public string ProviderId => "fake-cli";
        public McpEndpoint? SeenEndpoint { get; private set; }
        public Dictionary<string, string> TempFiles { get; } = [];
        public bool Throw { get; init; }
        public IReadOnlyList<string> WrittenPaths => _written;

        public ValueTask<IReadOnlyList<string>> BuildArgsAsync(McpCliContext context, CancellationToken ct = default)
        {
            SeenEndpoint = context.Endpoint;
            foreach (var (kind, content) in TempFiles)
                _written.Add(context.WriteTempFile(kind, content));
            if (Throw) throw new InvalidOperationException("dialect blew up");
            return ValueTask.FromResult<IReadOnlyList<string>>(["--fake", "arg"]);
        }
    }
}
