using System.Text.Json.Nodes;
using Lyntai.Agents;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>
/// The <see cref="IMcpCliDialect"/> for the <c>claude</c> CLI — the ONLY claude-specific part of the tool-
/// hosting path: the <c>--mcp-config</c> / <c>--settings</c> / <c>--allowedTools</c> flags, the two config
/// files' shapes, and the <c>mcp__&lt;server&gt;__*</c> permission pattern.
///
/// <para>It ships in the provider package (not the host package) because it is knowledge about
/// <c>claude</c>, and it costs this package NO new dependencies — it is JSON + strings over Core types.
/// The Kestrel host that consumes it lives in <c>Lyntai.Tools.Mcp.Hosting</c>, which is what keeps ASP.NET
/// Core off the dependency graph of apps that use the plain CLI provider.</para>
///
/// <para>Pair it with <c>AddMcpToolHost(new ClaudeCliMcpDialect())</c> (or the equivalent
/// <c>AddClaudeCliMcpTools()</c> shorthand in <c>Lyntai.Providers.ClaudeCli.Mcp</c>).</para>
/// </summary>
public sealed class ClaudeCliMcpDialect : IMcpCliDialect
{
    /// <inheritdoc />
    public string ProviderId => ClaudeCliProvider.ProviderId;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<string>> BuildArgsAsync(McpCliContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var server = context.Endpoint.ServerName;
        var mcpConfigPath = context.WriteTempFile("mcp", McpConfigJson(context.Endpoint));
        var settingsPath = context.WriteTempFile("settings", SettingsJson(server));

        // allow-list ONLY our server's tools so they run non-interactively in print mode; built-ins stay off
        IReadOnlyList<string> args =
            ["--mcp-config", mcpConfigPath, "--settings", settingsPath, "--allowedTools", ToolPattern(server)];
        return ValueTask.FromResult(args);
    }

    /// <summary>The <c>--mcp-config</c> file: points the CLI's MCP client at the running host over HTTP,
    /// carrying the per-host bearer token.</summary>
    internal static string McpConfigJson(McpEndpoint endpoint) => new JsonObject
    {
        ["mcpServers"] = new JsonObject
        {
            [endpoint.ServerName] = new JsonObject
            {
                ["type"] = "http",
                ["url"] = endpoint.Url,
                ["headers"] = new JsonObject { ["Authorization"] = $"Bearer {endpoint.AuthToken}" },
            },
        },
    }.ToJsonString();

    /// <summary>The <c>--settings</c> file: pre-approves our server's tools so print mode never blocks on
    /// a permission prompt.</summary>
    internal static string SettingsJson(string serverName) => new JsonObject
    {
        ["permissions"] = new JsonObject
        {
            ["allow"] = new JsonArray(ToolPattern(serverName)),
        },
    }.ToJsonString();

    private static string ToolPattern(string serverName) => $"mcp__{serverName}__*";
}
