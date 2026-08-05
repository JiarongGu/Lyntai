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
