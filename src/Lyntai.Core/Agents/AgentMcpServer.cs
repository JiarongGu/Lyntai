namespace Lyntai.Agents;

/// <summary>How an agent's CLI reaches an <see cref="AgentMcpServer"/>.</summary>
public enum McpTransport
{
    /// <summary>A child process speaking MCP over stdin/stdout. How an application ships its OWN tools
    /// without opening a port or authenticating one, and the shape MCP's reference servers ship in.</summary>
    Stdio,

    /// <summary>A streamable-HTTP endpoint the CLI connects to.</summary>
    Http,
}

/// <summary>One MCP server the HOST APPLICATION owns and wants an <see cref="IAgentSession"/> to reach, so
/// an embedded agent can act on the app's domain through the app's own tools.
///
/// <para><b>Why this is neutral rather than per-backend.</b> Both shipped CLI backends accept app-provided
/// MCP servers natively and both were measured doing it (2026-08-05): <c>claude</c> through
/// <c>--mcp-config</c> (JSON files or strings, space-separated), <c>codex</c> through repeated
/// <c>-c mcp_servers.&lt;name&gt;.…</c> config overrides whose values are parsed as TOML. The VOCABULARY
/// differs; the NEED is identical — which is what an adapter absorbs. Without this, the two backends were
/// interchangeable only for an agent that needs no app tools, which is the case the
/// <see cref="IAgentSession"/> abstraction is least often reached for.</para>
///
/// <para><b>Distinct from <see cref="McpEndpoint"/>, which is not a substitute.</b> That type describes the
/// loopback host LYNTAI stands up to expose the app's in-process <see cref="ITool"/>s over
/// <see cref="ICliToolProvisioner"/>, and it is HTTP-only — it cannot say <c>command</c>. This type
/// describes a server the APP already runs (or launches) and Lyntai only points the CLI at. The two
/// coexist: an app can host its in-proc tools through the provisioner AND hand the session an external
/// server it already ships.</para>
///
/// <para><b>Tool PERMISSION is not granted here.</b> Naming a server makes its tools reachable, not
/// pre-approved: on the claude backend a headless run still needs the tools allow-listed
/// (<c>ClaudeAgentOptions.AllowedTools</c>) or prompts bypassed
/// (<c>ClaudeAgentOptions.SkipAllPermissions</c>), and on codex the gate remains the
/// <c>--sandbox</c> mode. Auto-approving an app's servers would be a silent change of security posture, so
/// it is deliberately left to the caller.</para></summary>
public sealed record AgentMcpServer
{
    /// <summary>The name the server is published under. It becomes a key in the backend's own
    /// configuration — a JSON member for <c>claude</c>, a TOML dotted-path segment for <c>codex</c> — and
    /// CLIs also build tool-permission patterns from it (<c>mcp__&lt;name&gt;__*</c>).
    /// <para>Restricted to letters, digits, <c>_</c> and <c>-</c>, and an adapter REFUSES anything else
    /// rather than emitting it: a name carrying a dot, a quote or whitespace would silently produce a
    /// different key than the one asked for (a dot opens a nested table in codex's dotted path), and the
    /// agent would then run with a server nobody named.</para></summary>
    public required string Name { get; init; }

    /// <summary>Which of the two shapes below is filled in.</summary>
    public McpTransport Transport { get; init; } = McpTransport.Stdio;

    /// <summary><see cref="McpTransport.Stdio"/>: the executable to launch. Required for that transport.
    /// Passed to the backend as a value, never through a shell.</summary>
    public string? Command { get; init; }

    /// <summary><see cref="McpTransport.Stdio"/>: the child's arguments, each a separate entry.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary><see cref="McpTransport.Stdio"/>: environment variables for the child — how an app usually
    /// passes its own server a workspace path, a database handle or a token.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary><see cref="McpTransport.Http"/>: the server's absolute URL. Required for that transport.</summary>
    public string? Url { get; init; }

    /// <summary><see cref="McpTransport.Http"/>: an optional bearer token, sent as
    /// <c>Authorization: Bearer &lt;token&gt;</c>.
    /// <para>Never rendered into argv, on either backend: a command line is readable by every process on
    /// the machine. The claude adapter writes it into an owner-only temp file deleted when the turn ends;
    /// the codex adapter passes only the NAME of an environment variable (<c>bearer_token_env_var</c>, the
    /// one shape that CLI accepts) and puts the value in the child's environment.</para></summary>
    public string? AuthToken { get; init; }

    /// <summary>An MCP server the agent's CLI launches as a child process and speaks to over stdio.</summary>
    /// <param name="name">The published name — letters, digits, <c>_</c> and <c>-</c>.</param>
    /// <param name="command">The executable to launch.</param>
    /// <param name="arguments">The child's arguments, if any.</param>
    /// <param name="environment">Environment variables for the child, if any.</param>
    public static AgentMcpServer Stdio(
        string name,
        string command,
        IEnumerable<string>? arguments = null,
        IReadOnlyDictionary<string, string>? environment = null) => new()
        {
            Name = name,
            Transport = McpTransport.Stdio,
            Command = command,
            Arguments = arguments is null ? [] : [.. arguments],
            Environment = environment ?? new Dictionary<string, string>(StringComparer.Ordinal),
        };

    /// <summary>An MCP server the agent's CLI reaches over streamable HTTP.</summary>
    /// <param name="name">The published name — letters, digits, <c>_</c> and <c>-</c>.</param>
    /// <param name="url">The server's absolute URL.</param>
    /// <param name="authToken">An optional bearer token. See <see cref="AuthToken"/> for where it travels.</param>
    public static AgentMcpServer Http(string name, string url, string? authToken = null) => new()
        {
            Name = name,
            Transport = McpTransport.Http,
            Url = url,
            AuthToken = authToken,
        };
}
