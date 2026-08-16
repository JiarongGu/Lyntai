using Lyntai.Agents;
using Lyntai.Tools.Mcp;
using Lyntai.Tools.Mcp.Hosting;
using ModelContextProtocol.Client;

namespace Lyntai.Tests.Tools;

/// <summary>
/// Deterministic proof of the GENERIC CLI tool-hosting path WITHOUT any real CLI binary: the in-process
/// MCP host exposes the app's ITools over HTTP, and we connect with Lyntai's OWN MCP client (the exact
/// thing a CLI's agent does) to list + call them. Also covers the provider-neutral half of the
/// provisioner — endpoint hand-off to the dialect and temp-file lifecycle. Vendor-specific flags live in
/// <see cref="ClaudeCliMcpDialectTests"/>.
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
        // THE JAIL, found 2026-08-15. The archive records "Guards don't cover the agent tool loop" being
        // closed for ToolLoop in 2026-07; MCP hosting arrived later and reopened it from the other side. The
        // SAME ITool instances (sp.GetServices<ITool>()) are reachable both ways, so a consumer who
        // registered a guard had it enforced through IToolLoop and silently not through the hosted endpoint
        // the CLI's own agent calls. Neither ChatOrchestrator gate can see it either: gate 1 saw the user
        // message, gate 2 sees only the final answer.
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

    [Fact]
    public async Task With_no_guard_rail_a_hosted_tool_runs_exactly_as_before()
    {
        // The control. Guarding must be free when nothing is registered — the overwhelmingly common case,
        // and the one where a regression here would be most expensive.
        ITool echo = new FunctionTool("echo", (args, _) => Task.FromResult($"echoed:{args}"), "echoes",
            """{"type":"object","properties":{"message":{"type":"string"}}}""");

        const string token = "test-bearer-token";
        await using var host = await McpToolHost.StartAsync([echo], token);

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(host.Url),
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });
        await using var client = await McpClient.CreateAsync(transport);
        var tool = Assert.Single(await McpToolset.FromClientAsync(client));

        Assert.Contains("echoed", await tool.InvokeAsync("""{"message":"hi"}"""));
    }

    private sealed class BlockingRail : Lyntai.Guards.IGuardRail
    {
        public Task<Lyntai.Guards.GuardOutcome> InspectRequestAsync(Lyntai.Llm.LlmRequest req, CancellationToken ct = default) =>
            Task.FromResult(Lyntai.Guards.GuardOutcome.Block("blocked by policy"));

        public Task<Lyntai.Guards.GuardOutcome> InspectResponseAsync(Lyntai.Llm.LlmReply reply, CancellationToken ct = default) =>
            Task.FromResult(Lyntai.Guards.GuardOutcome.Allow);
    }

    private sealed class RedactingRail : Lyntai.Guards.IGuardRail
    {
        public Task<Lyntai.Guards.GuardOutcome> InspectRequestAsync(Lyntai.Llm.LlmRequest req, CancellationToken ct = default) =>
            Task.FromResult(Lyntai.Guards.GuardOutcome.Allow);

        public Task<Lyntai.Guards.GuardOutcome> InspectResponseAsync(Lyntai.Llm.LlmReply reply, CancellationToken ct = default) =>
            Task.FromResult(Lyntai.Guards.GuardOutcome.Replace("[redacted]"));
    }

    [Fact]
    public async Task Host_rejects_requests_without_the_bearer_token()
    {
        ITool echo = new FunctionTool("echo", (a, _) => Task.FromResult(a));
        await using var host = await McpToolHost.StartAsync([echo], "the-real-token");

        // no Authorization header → the MCP handshake must fail (401)
        var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(host.Url) });
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var client = await McpClient.CreateAsync(transport);
            await McpToolset.FromClientAsync(client);
        });
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

    /// <summary>A stand-in for a vendor dialect: records what the provisioner handed it, writes whatever
    /// temp files it was told to, and returns fixed args.</summary>
    private sealed class RecordingDialect : IMcpCliDialect
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
