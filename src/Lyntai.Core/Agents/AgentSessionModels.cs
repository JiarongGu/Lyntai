using Lyntai.Inference;

namespace Lyntai.Agents;

/// <summary>Read-only (plan gate) vs write (execute gate) tool policy for a gated agent session.</summary>
public enum AgentToolPolicy { ReadOnly, Write }

/// <summary>Neutral per-call inputs for an <see cref="IAgentSession"/>. Adapters subtype this to add
/// provider-specific options (e.g. ClaudeAgentOptions). Inheritable — do NOT seal.</summary>
public record AgentSessionOptions
{
    /// <summary>The user turn. Adapters send this over stdin, never argv.</summary>
    public required string Prompt { get; init; }

    /// <summary>Standing instruction for the session, separate from the turn. Null = none.
    /// <para><b>Its POSITION relative to <see cref="Prompt"/> is the adapter's, not yours</b>, because
    /// backends disagree: one appends it through a dedicated flag, another has no such flag and carries it
    /// as a leading block of the prompt itself. So write it to be order-independent — a persona or a
    /// constraint, not "ignore what follows". Each adapter's own docs say which it does.</para></summary>
    public string? SystemPrompt { get; init; }

    /// <summary>Whether this session may only READ or may also WRITE. <b>Defaults to
    /// <see cref="AgentToolPolicy.ReadOnly"/></b>: a plan gate is the recoverable failure and an unintended
    /// edit is not, so the safe end is the default rather than the convenient one.
    /// <para>Adapters express it in their OWN vocabulary — a deny list on one backend, a sandbox mode on
    /// another — so the guarantee is the INTENT, not a particular mechanism. It is not a substitute for the
    /// backend's own permission gate.</para></summary>
    public AgentToolPolicy ToolPolicy { get; init; } = AgentToolPolicy.ReadOnly;

    /// <summary>Opaque resume handle (a prior run's session id); null = a fresh session.</summary>
    public string? ResumeToken { get; init; }

    /// <summary>The BACKEND's own model id, passed through verbatim; null = whatever that backend defaults
    /// to. Lyntai neither validates it nor maps it — which model to run is the consuming application's
    /// choice, and the spelling is the backend's. Needs <see cref="AgentAbilities.Model"/>: a door never runs the
    /// turn on another model, so the protocol door ends the turn before the prompt when its agent refuses the id.
    /// <para><b>Unrelated to router candidate selection.</b> An agent session drives one configured backend
    /// directly, so this does not participate in <see cref="Lyntai.Inference.TextRequest.Model"/>'s routing
    /// precedence and nothing here falls over to another provider.</para></summary>
    public string? Model { get; init; }

    /// <summary>Per-call timeout; null = the global default.</summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>Tools this session must not use, by the backend's own tool names. Merged with whatever the
    /// adapter always denies and with <see cref="ToolPolicy"/>'s own denials — it only ever subtracts.
    /// <para><b>Not every backend can honour it, and one that cannot SAYS SO rather than failing
    /// silently</b> — a backend whose tool gate is a sandbox has no deny list to add to, and reports the
    /// option as unhonoured. Treat it as defence in depth over the backend's gate, never as the gate.</para>
    /// </summary>
    public IReadOnlyList<string> DisallowedTools { get; init; } = [];
    /// <summary>Where a CLI-agent adapter runs the loop (its cwd). Adapters without a filesystem context ignore it.</summary>
    public string? WorkingDirectory { get; init; }
    /// <summary>The host application's OWN MCP servers, so an embedded agent can reach the app's domain
    /// through the app's tools. Each adapter renders these in its backend's vocabulary; see
    /// <see cref="AgentMcpServer"/> for the two transports and for what this does NOT grant (tool
    /// permission, which stays the caller's).</summary>
    public IReadOnlyList<AgentMcpServer> McpServers { get; init; } = [];

    /// <summary>The account this turn runs under; null = the door's own default home. A door that reads one starts
    /// its child with the account's <see cref="AgentAccount.Home"/> in its own configuration-home variable
    /// (<see cref="AgentAccountMapping.HomeVariable"/>) and its <see cref="AgentAccount.Environment"/> beside it; a
    /// door that does not, an account that belongs to another provider (neither the door's id nor its
    /// <see cref="AgentProviderDescriptor.Agent"/>), and an entry the provider does not read, refuse the turn
    /// before anything is spawned. Needs <see cref="AgentAbilities.Accounts"/>.</summary>
    public AgentAccount? Account { get; init; }
}

/// <summary>An account for one provider: a name the host calls it by, and either a configuration home the host
/// already set up and signed in, or environment entries the host supplies for the turn (an API key, a gateway's
/// address), or both. Lyntai creates no profile, signs in to none and stores no credential (<c>D20</c>): the
/// home's contents are the agent's own, never read or written by the library, and an <see cref="Environment"/>
/// value is held only as long as the host holds this record, reaches the child's environment and nowhere else.</summary>
public sealed record AgentAccount
{
    /// <summary>The account's key: the id of the registered provider it belongs to
    /// (<see cref="AgentProviderDescriptor.Id"/>, <c>"claude-cli"</c> by default), or the agent several providers
    /// share (<see cref="AgentProviderDescriptor.Agent"/>), which makes one account usable through each of them. A
    /// provider that is neither refuses it. The pool holds a restriction on the account for every provider that
    /// shares the key.</summary>
    public required string Provider { get; init; }

    /// <summary>What the host calls the account. Carried for the host's own bookkeeping; never sent to the agent.</summary>
    public required string Name { get; init; }

    /// <summary>The account's configuration home, passed to the child verbatim in the door's home variable. It must
    /// exist already and should be absolute: a relative path resolves against the child's working directory, and a
    /// missing one is created by the agent itself, signed out. No control character is allowed. Null = the account
    /// is its <see cref="Environment"/> alone, and the door's home variable is left as the session has it. An
    /// account with neither a home nor an environment entry is refused.</summary>
    public string? Home { get; init; }

    /// <summary>Variables the child is started with for this turn, over the session's own environment: an API key, a
    /// gateway's base address. Each name must be one the provider reads (<see cref="AgentAccountMapping.Variables"/>,
    /// matched exactly), else the turn is refused before a spawn; a value is empty or free of control characters,
    /// and an empty one is set empty, which is how a variable an agent would otherwise read is blanked. The home
    /// variable is set by <see cref="Home"/>, never here.
    /// <para><b>A value is a secret and goes nowhere but the child's environment</b> (the shape of
    /// <see cref="AgentMcpServer.AuthToken"/>): never argv, a log line, a diagnostic or an event, and not
    /// <see cref="ToString"/> on this record, which lists the names. The pool files an account by
    /// <see cref="Provider"/> and <see cref="Name"/> only, so nothing here is persisted; two listings of one account
    /// carry the same entries.</para></summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = EmptyEnvironment;

    private static readonly IReadOnlyDictionary<string, string> EmptyEnvironment =
        new Dictionary<string, string>(0, StringComparer.Ordinal);

    // the compiler's ToString lists every member, which would print each value of Environment
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Provider = ").Append(Provider).Append(", Name = ").Append(Name).Append(", Home = ").Append(Home)
            .Append(", Environment = [").Append(string.Join(", ", Environment.Keys.Order(StringComparer.Ordinal))).Append(']');
        return true;
    }
}

/// <summary>The caller-facing outcome of a session (the fold of the event stream).</summary>
public sealed record AgentSessionResult(
    string? SessionId, string FinalText, ProviderVerdict Verdict, bool IsError,
    string? Subtype, string? Diagnostic, UsageFinal? Usage);
