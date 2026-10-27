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
    /// choice, and the spelling is the backend's.
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
    /// (<see cref="AgentAccountMapping.HomeVariable"/>); a door that does not, and an account that belongs to
    /// another provider, refuse the turn before anything is spawned. Needs <see cref="AgentAbilities.Accounts"/>.</summary>
    public AgentAccount? Account { get; init; }
}

/// <summary>An account for one provider: a name the host calls it by and a configuration home the host already set
/// up and signed in. Lyntai creates no profile, signs in to none and stores no credential (<c>D20</c>): nothing
/// here is a secret, and the home's contents are the agent's own, never read or written by the library.</summary>
public sealed record AgentAccount
{
    /// <summary>The id of the registered provider this account belongs to (<see cref="AgentProviderDescriptor.Id"/>,
    /// <c>"claude-cli"</c> by default). Another provider refuses it.</summary>
    public required string Provider { get; init; }

    /// <summary>What the host calls the account. Carried for the host's own bookkeeping; never sent to the agent.</summary>
    public required string Name { get; init; }

    /// <summary>The account's configuration home, passed to the child verbatim in the door's home variable. It must
    /// exist already and should be absolute: a relative path resolves against the child's working directory, and a
    /// missing one is created by the agent itself, signed out. No control character is allowed.</summary>
    public required string Home { get; init; }
}

/// <summary>The caller-facing outcome of a session (the fold of the event stream).</summary>
public sealed record AgentSessionResult(
    string? SessionId, string FinalText, ProviderVerdict Verdict, bool IsError,
    string? Subtype, string? Diagnostic, UsageFinal? Usage);
