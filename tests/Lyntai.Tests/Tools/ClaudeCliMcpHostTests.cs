using Lyntai.Agents;
using Lyntai.Providers.ClaudeCli.Mcp;
using Lyntai.Tools.Mcp;
using ModelContextProtocol.Client;

namespace Lyntai.Tests.Tools;

/// <summary>
/// Deterministic proof of the CLI tool-hosting path WITHOUT the real claude binary: the in-process MCP
/// host exposes the app's ITools over HTTP, and we connect with Lyntai's OWN MCP client (the exact thing
/// the CLI does) to list + call them. Also covers the provisioner's CLI args + temp-file lifecycle.
/// </summary>
public class ClaudeCliMcpHostTests
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
    public void McpConfig_points_the_cli_at_the_host_over_http_with_the_bearer()
    {
        var json = McpCliToolProvisioner.McpConfigJson("http://127.0.0.1:1234/mcp", "sekret");
        Assert.Contains("\"type\":\"http\"", json);
        Assert.Contains("http://127.0.0.1:1234/mcp", json);
        Assert.Contains("lyntai", json);
        Assert.Contains("Bearer sekret", json); // the CLI is told the per-host token
    }

    [Fact]
    public void Settings_allow_list_only_our_server()
    {
        Assert.Contains("mcp__lyntai__*", McpCliToolProvisioner.SettingsJson());
    }

    [Fact]
    public async Task Provisioner_with_no_tools_is_a_noop()
    {
        await using var session = await new McpCliToolProvisioner([]).ProvisionAsync();
        Assert.Empty(session.ExtraArgs); // no host, no CLI args — the CLI runs exactly as before
    }

    [Fact]
    public async Task Provisioner_hosts_returns_cli_args_and_cleans_up_temp_files()
    {
        ITool echo = new FunctionTool("echo", (a, _) => Task.FromResult(a));
        var provisioner = new McpCliToolProvisioner([echo]);

        string configPath;
        await using (var session = await provisioner.ProvisionAsync())
        {
            var args = session.ExtraArgs;
            Assert.Contains("--mcp-config", args);
            Assert.Contains("--settings", args);
            Assert.Contains("--allowedTools", args);
            configPath = args[args.ToList().IndexOf("--mcp-config") + 1];
            Assert.True(File.Exists(configPath));   // temp config written while the session is live
        }
        Assert.False(File.Exists(configPath));       // deleted on session dispose
    }
}
