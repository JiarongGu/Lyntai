using Lyntai.Agents;

namespace Lyntai.Providers;

/// <summary>Validation of <see cref="AgentSessionOptions.McpServers"/> shared by every CLI agent session in
/// this package, so the two backends refuse the SAME inputs for the same reasons. Rendering stays
/// per-backend (<c>ClaudeMcpConfig</c> writes JSON, <c>CodexMcpConfig</c> writes TOML overrides); only the
/// question "can this be rendered at all" is common.
///
/// <para><b>Why REFUSE rather than drop.</b> An MCP server the caller named and the agent never received is
/// a silent capability loss — the agent runs, answers, and simply cannot do the thing it was embedded to do,
/// with no error anywhere. That is the exact failure CLI14 exists to prevent, so an unusable entry ends the
/// turn before anything is spawned, the same way <see cref="CodexCli.CodexExecArgs.TryBuildResume"/> refuses
/// a resume token the CLI would read as an option.</para></summary>
internal static class AgentMcpServers
{
    /// <summary>Can every entry be rendered? Returns false with a caller-actionable
    /// <paramref name="refusal"/> otherwise. An empty list is valid and means "no app servers".</summary>
    public static bool TryValidate(IReadOnlyList<AgentMcpServer> servers, out string? refusal)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var server in servers)
        {
            if (!IsUsableName(server.Name))
            {
                refusal = $"'{server.Name}' is not a usable MCP server name — a name becomes a key in the " +
                    "backend's own configuration (a JSON member for claude, a dotted TOML path segment for " +
                    "codex), so it must be letters, digits, '_' or '-'. A dot, a quote or whitespace would " +
                    "silently register a DIFFERENT server than the one named.";
                return false;
            }

            // OrdinalIgnoreCase: the backends key their config on the name, and two entries differing only
            // in case would collapse into one on a case-insensitive reader — losing a server silently, which
            // is the outcome this whole class exists to prevent.
            if (!seen.Add(server.Name))
            {
                refusal = $"MCP server '{server.Name}' is listed more than once — the second entry would " +
                    "overwrite the first in the backend's configuration and one of the two servers would " +
                    "silently not exist.";
                return false;
            }

            switch (server.Transport)
            {
                case McpTransport.Stdio when server.Command is not { Length: > 0 }:
                    refusal = $"MCP server '{server.Name}' is Stdio but has no Command — there is nothing " +
                        "to launch. Use AgentMcpServer.Stdio(name, command, …), or Http(name, url) for a " +
                        "server that is already running.";
                    return false;

                case McpTransport.Http when !IsAbsoluteUrl(server.Url):
                    refusal = $"MCP server '{server.Name}' is Http but its Url ('{server.Url}') is not an " +
                        "absolute URL. Use AgentMcpServer.Http(name, \"https://host/path\", …).";
                    return false;

                default:
                    break;
            }
        }

        refusal = null;
        return true;
    }

    /// <summary>The name rule, stated once. Deliberately narrower than either backend would accept: the
    /// intersection is what keeps one <see cref="AgentSessionOptions"/> renderable on both.</summary>
    private static bool IsUsableName(string? name)
    {
        if (name is not { Length: > 0 }) return false;
        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-') return false;
        }
        return true;
    }

    private static bool IsAbsoluteUrl(string? url) =>
        url is { Length: > 0 } && Uri.TryCreate(url, UriKind.Absolute, out _);
}
