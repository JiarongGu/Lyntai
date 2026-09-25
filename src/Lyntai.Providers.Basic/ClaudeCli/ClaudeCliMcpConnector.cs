using System.Text.Json.Nodes;
using Lyntai.Agents;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>
/// The <see cref="IMcpCliConnector"/> for the <c>claude</c> CLI — the ONLY claude-specific part of the tool-
/// hosting path: the <c>--mcp-config</c> / <c>--settings</c> / <c>--allowedTools</c> flags, the two config
/// files' shapes, and the <c>mcp__&lt;server&gt;__*</c> permission pattern.
///
/// <para>It ships in the provider package (not the host package) because it is knowledge about
/// <c>claude</c>, and it costs this package NO new dependencies — it is JSON + strings over Core types.
/// The host that consumes it lives in <c>Lyntai.Tools.Mcp</c> and runs on
/// <c>System.Net.HttpListener</c> (BCL, no ASP.NET Core); keeping the connector out of it is what keeps that
/// package's <c>ModelContextProtocol.Core</c> dependency off the graph of apps that use the plain CLI
/// provider.</para>
///
/// <para>Wire it with <c>AddMcpToolHost(new ClaudeCliMcpConnector())</c> from
/// <c>Lyntai.Tools.Mcp</c>, alongside <c>AddClaudeCliProvider()</c> and your tool
/// registrations.</para>
/// </summary>
public sealed class ClaudeCliMcpConnector : IMcpCliConnector
{
    /// <summary>A connector for every claude registration that has none of its own: keyed on
    /// <see cref="ClaudeCliProvider.ProviderId"/>, the id claude registrations fall back to.</summary>
    public ClaudeCliMcpConnector() : this(ClaudeCliProvider.ProviderId) { }

    /// <summary>A connector for ONE claude registration — the one registered under
    /// <paramref name="providerId"/>, which prefers it over the shared one.</summary>
    /// <param name="providerId">The id that claude registration was given.</param>
    public ClaudeCliMcpConnector(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ProviderId = providerId;
    }

    /// <inheritdoc />
    public string ProviderId { get; }

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
    /// carrying the per-host bearer token — rendered by the same code as an agent session's servers.</summary>
    internal static string McpConfigJson(McpEndpoint endpoint) =>
        ClaudeMcpConfig.Json([AgentMcpServer.Http(endpoint.ServerName, endpoint.Url, endpoint.AuthToken)]);

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
