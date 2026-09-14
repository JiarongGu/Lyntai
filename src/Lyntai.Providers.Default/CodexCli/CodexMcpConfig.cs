using System.Globalization;
using System.Text;
using Lyntai.Agents;

namespace Lyntai.Providers.CodexCli;

/// <summary>Renders the host application's <see cref="AgentSessionOptions.McpServers"/> into codex's own
/// vocabulary: repeated <c>-c mcp_servers.&lt;name&gt;.…=&lt;TOML&gt;</c> config overrides.
///
/// <para><b>MEASURED against codex-cli 0.146.0 (2026-08-05), turn-free.</b> <c>codex exec --help</c> states
/// <c>-c, --config &lt;key=value&gt;</c>: "Override a configuration value that would otherwise be loaded from
/// <c>~/.codex/config.toml</c>. Use a dotted path (<c>foo.bar.baz</c>) to override nested values. The
/// <c>value</c> portion is parsed as TOML." The keys below were then confirmed by driving
/// <c>codex mcp list</c> / <c>codex mcp get</c> — which READ configuration and spend no turn — with the
/// overrides applied, and reading back the server codex actually registered:</para>
/// <code>
/// codex mcp list -c 'mcp_servers.x.command="node"' -c 'mcp_servers.x.args=["a","b"]' \
///                -c 'mcp_servers.x.env={ "K1" = "v1" }'
///   → Name x  Command node  Args a b  Env K1=*****  Status enabled
/// codex mcp get h -c 'mcp_servers.h.url="https://example.invalid/mcp"' \
///                 -c 'mcp_servers.h.bearer_token_env_var="LYNTAI_MCP_BEARER_H"'
///   → transport: streamable_http · url: … · bearer_token_env_var: LYNTAI_MCP_BEARER_H
/// </code>
///
/// <para><b>Why a bearer token travels in the ENVIRONMENT and not in argv.</b> codex accepts only
/// <c>bearer_token_env_var</c> — the NAME of a variable to read — and never a literal token, which is the
/// shape to want anyway: a command line is readable by any process that can list processes, so rendering
/// <c>-c …http_headers={ "Authorization" = "Bearer …" }</c> would publish the caller's secret to the whole
/// machine. So the value goes into the child's environment under a generated name and only that name
/// reaches argv.</para></summary>
internal static class CodexMcpConfig
{
    /// <summary>The prefix of the environment variable a bearer token is passed through. The server name is
    /// appended so several servers can each carry their own.</summary>
    private const string BearerPrefix = "LYNTAI_MCP_BEARER_";

    /// <summary>Build the <c>-c</c> arguments for <paramref name="servers"/>, plus any environment variables
    /// the spawn must carry for them. Callers validate through <see cref="AgentMcpServers.TryValidate"/>
    /// first; this method assumes usable entries.</summary>
    /// <param name="servers">The host application's servers. Empty yields empty.</param>
    /// <param name="environment">Variables to merge into the child's environment (bearer tokens).</param>
    public static List<string> Build(
        IReadOnlyList<AgentMcpServer> servers, out Dictionary<string, string> environment)
    {
        var args = new List<string>();
        environment = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var server in servers)
        {
            var key = $"mcp_servers.{server.Name}";
            if (server.Transport == McpTransport.Stdio)
            {
                Add(args, $"{key}.command", Toml(server.Command!));
                if (server.Arguments.Count > 0)
                    Add(args, $"{key}.args", TomlArray(server.Arguments));
                if (server.Environment.Count > 0)
                    Add(args, $"{key}.env", TomlTable(server.Environment));
            }
            else
            {
                Add(args, $"{key}.url", Toml(server.Url!));
                if (server.AuthToken is { Length: > 0 } token)
                {
                    // the NAME goes to argv; the VALUE goes to the child's environment (see the class doc)
                    // AgentMcpServers.EnvKey, not a local copy: TryValidate refuses a pair whose names collide
                    // under this exact normalisation, and a second copy here would let the two disagree.
                    var variable = BearerPrefix + AgentMcpServers.EnvKey(server.Name);
                    environment[variable] = token;
                    Add(args, $"{key}.bearer_token_env_var", Toml(variable));
                }
            }
        }

        return args;
    }

    private static void Add(List<string> args, string key, string tomlValue)
    {
        // `-c` and its value are separate ArgumentList entries — never a shell, so nothing here is quoted
        // for one; the quoting below is TOML's, which codex parses out of the value itself.
        args.Add("-c");
        args.Add($"{key}={tomlValue}");
    }

    private static string TomlArray(IReadOnlyList<string> values) =>
        $"[{string.Join(", ", values.Select(Toml))}]";

    private static string TomlTable(IReadOnlyDictionary<string, string> entries) =>
        $"{{ {string.Join(", ", entries.Select(e => $"{Toml(e.Key)} = {Toml(e.Value)}"))} }}";

    /// <summary>A TOML basic string. Every value is quoted and escaped rather than emitted raw, because
    /// codex's documented fallback for a value that FAILS to parse as TOML is to use the raw string as a
    /// literal — so a bad escape does not fail loudly, it silently changes the value. Windows paths are the
    /// case that matters: <c>C:\app\mcp.exe</c> inside a basic string contains <c>\a</c>, which is not a TOML
    /// escape. Quoted keys are legal in an inline table, so the same helper serves both halves.</summary>
    private static string Toml(string value)
    {
        var sb = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            _ = c switch
            {
                '\\' => sb.Append("\\\\"),
                '"' => sb.Append("\\\""),
                '\n' => sb.Append("\\n"),
                '\r' => sb.Append("\\r"),
                '\t' => sb.Append("\\t"),
                '\b' => sb.Append("\\b"),
                '\f' => sb.Append("\\f"),
                < ' ' or '\u007f' => sb.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}"),
                _ => sb.Append(c),
            };
        }
        return sb.Append('"').ToString();
    }
}
