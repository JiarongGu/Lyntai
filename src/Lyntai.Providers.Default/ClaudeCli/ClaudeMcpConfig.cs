using System.Text.Json.Nodes;
using Lyntai.Agents;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>Renders the host application's <see cref="AgentSessionOptions.McpServers"/> into claude's own
/// vocabulary: one <c>--mcp-config</c> document.
///
/// <para><b>MEASURED against the installed claude CLI (2026-08-05), turn-free.</b> <c>claude --help</c>
/// states <c>--mcp-config &lt;configs...&gt;</c>: "Load MCP servers from JSON files or strings
/// (space-separated)" — so the flag takes a LIST, which is what lets a Lyntai-rendered document sit
/// alongside a caller's own <c>ClaudeAgentOptions.McpConfigPath</c> instead of displacing it. The document
/// SHAPE was then confirmed by placing it as a project <c>.mcp.json</c> and reading back what
/// <c>claude mcp list</c> — which reads configuration and spends no turn — had parsed: both entries came
/// back with their name, transport and command/url intact.</para>
///
/// <para><b>Why a file rather than the inline-string form the flag also accepts.</b> A config can carry a
/// bearer token or a secret in a stdio server's <c>env</c>, and argv is readable by any process that can
/// list processes. The file is created owner-only and deleted when the turn ends — the same reasoning as
/// <see cref="Agents.McpCliContext.WriteTempFile"/>, which the in-process tool host uses for the same
/// reason.</para></summary>
internal static class ClaudeMcpConfig
{
    /// <summary>The <c>--mcp-config</c> document for <paramref name="servers"/>. Callers validate through
    /// <see cref="AgentMcpServers.TryValidate"/> first; this method assumes usable entries.</summary>
    public static string Json(IReadOnlyList<AgentMcpServer> servers)
    {
        var entries = new JsonObject();
        foreach (var server in servers) entries[server.Name] = Entry(server);
        return new JsonObject { ["mcpServers"] = entries }.ToJsonString();
    }

    private static JsonObject Entry(AgentMcpServer server)
    {
        if (server.Transport == McpTransport.Stdio)
        {
            var stdio = new JsonObject { ["type"] = "stdio", ["command"] = server.Command };
            if (server.Arguments.Count > 0)
                stdio["args"] = new JsonArray([.. server.Arguments.Select(a => (JsonNode?)a)]);
            if (server.Environment.Count > 0)
            {
                var env = new JsonObject();
                foreach (var (key, value) in server.Environment) env[key] = value;
                stdio["env"] = env;
            }
            return stdio;
        }

        var http = new JsonObject { ["type"] = "http", ["url"] = server.Url };
        if (server.AuthToken is { Length: > 0 } token)
            http["headers"] = new JsonObject { ["Authorization"] = $"Bearer {token}" };
        return http;
    }
}
