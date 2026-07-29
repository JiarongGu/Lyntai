using Lyntai.Agents;
using Lyntai.Providers.ClaudeCli;

namespace Lyntai.Tests.Tools;

/// <summary>
/// The claude-CLI DIALECT — the only vendor-specific slice of the tool-hosting path (flag names + the two
/// config-file shapes). It lives in the provider package and needs no host to test: hand it an
/// <see cref="McpCliContext"/> and assert the argv + files it asks for.
/// </summary>
public class ClaudeCliMcpDialectTests
{
    [Fact]
    public void Targets_the_claude_cli_provider()
    {
        Assert.Equal(ClaudeCliProvider.ProviderId, new ClaudeCliMcpDialect().ProviderId);
    }

    [Fact]
    public void McpConfig_points_the_cli_at_the_host_over_http_with_the_bearer()
    {
        var json = ClaudeCliMcpDialect.McpConfigJson(new McpEndpoint("http://127.0.0.1:1234/mcp", "sekret", "lyntai"));
        Assert.Contains("\"type\":\"http\"", json);
        Assert.Contains("http://127.0.0.1:1234/mcp", json);
        Assert.Contains("lyntai", json);
        Assert.Contains("Bearer sekret", json); // the CLI is told the per-host token
    }

    [Fact]
    public void Settings_allow_list_only_our_server()
    {
        Assert.Contains("mcp__lyntai__*", ClaudeCliMcpDialect.SettingsJson("lyntai"));
    }

    [Fact]
    public void Config_shapes_follow_the_configured_server_name()
    {
        Assert.Contains("my-tools", ClaudeCliMcpDialect.McpConfigJson(new McpEndpoint("http://x/mcp", "t", "my-tools")));
        Assert.Contains("mcp__my-tools__*", ClaudeCliMcpDialect.SettingsJson("my-tools"));
    }

    [Fact]
    public async Task Builds_the_claude_flags_over_two_temp_files()
    {
        var written = new Dictionary<string, string>();
        var context = new McpCliContext(
            new McpEndpoint("http://127.0.0.1:1234/mcp", "sekret", "lyntai"),
            (kind, content) => { written[kind] = content; return $"/tmp/{kind}.json"; });

        var args = await new ClaudeCliMcpDialect().BuildArgsAsync(context);

        Assert.Contains("--mcp-config", args);
        Assert.Contains("--settings", args);
        Assert.Contains("--allowedTools", args);
        Assert.Contains("mcp__lyntai__*", args);
        Assert.Equal("/tmp/mcp.json", args[args.ToList().IndexOf("--mcp-config") + 1]);
        Assert.Equal("/tmp/settings.json", args[args.ToList().IndexOf("--settings") + 1]);

        // both files were asked for through the context, so the provisioner tracks + deletes them
        Assert.Equal(2, written.Count);
        Assert.Contains("Bearer sekret", written["mcp"]);
        Assert.Contains("mcp__lyntai__*", written["settings"]);
    }
}
