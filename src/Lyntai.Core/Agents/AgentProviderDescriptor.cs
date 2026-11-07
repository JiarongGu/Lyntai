namespace Lyntai.Agents;

/// <summary>Whether a door can do something. <see cref="Unknown"/> is a value, not an absence: a door states an
/// ability it has not measured as unknown, and a host reads it as "not promised" rather than "not possible".</summary>
public enum AgentSupport
{
    /// <summary>Not measured, or not stated. The default, so a claim nobody made is never read as a promise.</summary>
    Unknown = 0,

    /// <summary>The door does it.</summary>
    Supported,

    /// <summary>The door cannot. An option that needs it is refused before anything is spawned.</summary>
    Unsupported,
}

/// <summary>Where a claim about an ability came from, which says how far to trust it.</summary>
public enum AgentAbilitySource
{
    /// <summary>The adapter's own statement of what it does, read from its code or the door's documentation.</summary>
    Declared = 0,

    /// <summary>Agreed with the door at run time (a protocol's capability exchange).</summary>
    Negotiated,

    /// <summary>Observed on the door's real wire.</summary>
    Measured,
}

/// <summary>What using an ability may do to the machine beyond the answer a host asked for. Declared, never
/// measured away: a vendor's start is not the library's to promise about, so a flag here means "may", and an
/// ability that starts the vendor's own process states every effect it cannot rule out.</summary>
[Flags]
public enum AgentSideEffects
{
    /// <summary>Nothing beyond the answer; the default, which an ability that spawns nothing keeps.</summary>
    None = 0,

    /// <summary>The start may refresh the account's credentials in place, changing files in its home.</summary>
    RefreshesCredentials = 1,

    /// <summary>The start may create or change configuration or state files (the account's home, the vendor's caches).</summary>
    ChangesConfiguration = 2,

    /// <summary>The start may load the vendor's extensions: plugins, skills, hooks, or servers it is configured with.</summary>
    LoadsExtensions = 4,

    /// <summary>The start may open a listener (a port, a pipe, a remote-control endpoint) beyond its stdio.</summary>
    OpensListener = 8,

    /// <summary>The start may leave a process running after the library has ended the one it started.</summary>
    LeavesProcess = 16,
}

/// <summary>One claim a door makes about one ability.</summary>
/// <param name="Name">The ability, one of <see cref="AgentAbilities.All"/> or a host's own.</param>
/// <param name="Support">What the door says about it.</param>
/// <param name="Source">Where the claim comes from.</param>
public sealed record AgentAbility(string Name, AgentSupport Support, AgentAbilitySource Source)
{
    /// <summary>What the claim rests on, in a line a reader can act on. Null when the source says it all.</summary>
    public string? Evidence { get; init; }

    /// <summary>The door's version the claim applies to; null when no version is stated.</summary>
    public string? AppliesTo { get; init; }

    /// <summary>What using the ability may do beyond its answer; <see cref="AgentSideEffects.None"/> unless the
    /// ability starts a process of the vendor's whose effects are not the library's own.</summary>
    public AgentSideEffects SideEffects { get; init; }
}

/// <summary>The kinds of door the library names. A door's <see cref="AgentDoor.Kind"/> is open: a host's own
/// provider states its own.</summary>
public static class AgentDoorKinds
{
    /// <summary>An agent run as a command-line process, one process per turn.</summary>
    public const string Cli = "cli";

    /// <summary>An agent reached over the Agent Client Protocol.</summary>
    public const string Acp = "acp";

    /// <summary>A chat-completions endpoint reached over HTTP, whose tool loop is Lyntai's own
    /// (<see cref="IToolLoop"/>) rather than an agent's.</summary>
    public const string Http = "http";
}

/// <summary>One way into an agent, and what it can do. A provider with two doors (a CLI and a protocol) states
/// each separately, because the same agent may honour an option through one and not the other.</summary>
public sealed record AgentDoor
{
    /// <summary>The door's id, unique within its provider.</summary>
    public required string Id { get; init; }

    /// <summary>What kind of door it is; see <see cref="AgentDoorKinds"/>.</summary>
    public required string Kind { get; init; }

    /// <summary>The door's claims, at most one per ability name. A door states EVERY ability in
    /// <see cref="AgentAbilities.All"/>, an unmeasured one as <see cref="AgentSupport.Unknown"/>.</summary>
    public required IReadOnlyList<AgentAbility> Abilities { get; init; }

    /// <summary>What the door says about <paramref name="ability"/>; <see cref="AgentSupport.Unknown"/> for one
    /// it states nothing about.</summary>
    public AgentSupport SupportFor(string ability) =>
        Abilities.FirstOrDefault(a => string.Equals(a.Name, ability, StringComparison.Ordinal))?.Support
        ?? AgentSupport.Unknown;

    /// <summary>The abilities <paramref name="options"/> needs that this door states
    /// <see cref="AgentSupport.Unsupported"/>, in the order of <see cref="AgentAbilities.All"/>; empty when the
    /// door can be handed the options. An ability the door has not measured is not here: refusing it would be a
    /// guess, so the options reach the door.</summary>
    public IReadOnlyList<string> Unhonourable(AgentSessionOptions options) =>
        [.. AgentAbilities.Needed(options).Where(a => SupportFor(a) == AgentSupport.Unsupported)];
}

/// <summary>What one agent is: its doors, and what each can do. The host reads this and never branches on an id.
/// <para>A claim is held by a test that fails when the wire disagrees (<c>claims-need-checks</c>); an ability a
/// door has not measured is <see cref="AgentSupport.Unknown"/>.</para></summary>
public sealed record AgentProviderDescriptor
{
    /// <summary>The id the provider is registered and keyed under (<c>"claude-cli"</c>, <c>"codex-cli"</c>).</summary>
    public required string Id { get; init; }

    /// <summary>The doors, at least one.</summary>
    public required IReadOnlyList<AgentDoor> Doors { get; init; }

    /// <summary>How the provider reads an account, or null when it reads none. Set exactly when its doors state
    /// <see cref="AgentAbilities.Accounts"/> <see cref="AgentSupport.Supported"/>.</summary>
    public AgentAccountMapping? Accounts { get; init; }

    /// <summary>The agent whose accounts this provider reads, when several providers are doors of one agent (its CLI,
    /// its protocol adapter, an HTTP door): providers that state the same agent share one set of accounts, and an
    /// <see cref="AgentAccount"/> whose <see cref="AgentAccount.Provider"/> is that string is accepted by each. Null =
    /// the provider's own id, so a provider that states none keeps its accounts to itself. Needs
    /// <see cref="Accounts"/>, and every provider of one agent must read the home through the same variable: the
    /// account's home is the agent's, so a provider reading another variable is refused when the registry is built
    /// (<see cref="AgentProviderRegistry"/>) and, through <see cref="LyntaiBuilder.AddAgentProvider(AgentProviderDescriptor, Func{IServiceProvider, IAgentProvider})"/>,
    /// when it is registered.</summary>
    public string? Agent { get; init; }

    /// <summary>The key accounts of this provider are filed under: <see cref="Agent"/>, else <see cref="Id"/>.</summary>
    public string AccountKey => Agent ?? Id;
}

/// <summary>How a provider's doors take an <see cref="AgentAccount"/>: the environment variable its agent reads its
/// configuration home from, and the others it reads that choose whose identity or route a turn runs under, all set on
/// the child it starts for the turn. A host that wants an agent not to inherit another provider's credentials
/// computes its strip from <see cref="Variables"/> (a null entry in the session's environment removes a variable,
/// <see cref="Lyntai.Processes.IProcessRunner"/>); which to strip is the host's policy.</summary>
public sealed record AgentAccountMapping
{
    /// <param name="homeVariable">The variable name (<c>CLAUDE_CONFIG_DIR</c> for claude, <c>CODEX_HOME</c> for codex).
    /// For the turn it wins over the same variable in the session's own environment.</param>
    /// <param name="variables">The others the agent reads for an API key, a token or a gateway's address, exact names.
    /// A repeat of a name is dropped.</param>
    /// <exception cref="ArgumentException">A blank name, or one holding <c>=</c> or a control character, none of
    /// which an environment can carry.</exception>
    public AgentAccountMapping(string homeVariable, IEnumerable<string>? variables = null)
    {
        var all = new List<string>();
        foreach (var name in new[] { homeVariable }.Concat(variables ?? []))
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains('=') || name.Any(char.IsControl))
                throw new ArgumentException($"'{name}' is not a name an environment variable can have.", nameof(variables));
            if (!all.Contains(name, StringComparer.Ordinal)) all.Add(name);
        }
        HomeVariable = homeVariable;
        Variables = all;
    }

    /// <summary>The variable the agent reads its configuration home from; <see cref="AgentAccount.Home"/> is set in it.</summary>
    public string HomeVariable { get; }

    /// <summary>Every variable an <see cref="AgentAccount"/> may set for this provider: the home variable, then the
    /// others. An <see cref="AgentAccount.Environment"/> entry outside this set is refused before a spawn.</summary>
    public IReadOnlyList<string> Variables { get; }
}

/// <summary>The abilities a host asks about, by name. A host's own provider may state more; these are the ones
/// every provider states.</summary>
public static class AgentAbilities
{
    /// <summary>Continue a conversation from <see cref="AgentSessionOptions.ResumeToken"/>.</summary>
    public const string Resume = "resume";

    /// <summary>Hold a turn to reading when <see cref="AgentSessionOptions.ToolPolicy"/> is
    /// <see cref="AgentToolPolicy.ReadOnly"/>.</summary>
    public const string ReadOnlyGate = "read-only-gate";

    /// <summary>Reach the host's tools through <see cref="AgentSessionOptions.McpServers"/>.</summary>
    public const string McpServers = "mcp-servers";

    /// <summary>Deny tools by name through <see cref="AgentSessionOptions.DisallowedTools"/>.</summary>
    public const string DisallowedTools = "disallowed-tools";

    /// <summary>Carry <see cref="AgentSessionOptions.SystemPrompt"/> as a standing instruction.</summary>
    public const string SystemPrompt = "system-prompt";

    /// <summary>Run the turn on <see cref="AgentSessionOptions.Model"/>. A door that states it carries the id or ends
    /// the turn before the prompt; none runs a turn on another model and says nothing.</summary>
    public const string Model = "model";

    /// <summary>Take a word from the host while a turn is running.</summary>
    public const string Steering = "steering";

    /// <summary>Emit <see cref="PlanUpdated"/> when the agent keeps a plan.</summary>
    public const string PlanEvents = "plan-events";

    /// <summary>Emit <see cref="UsageLive"/> while a turn is running, not only when it ends.</summary>
    public const string LiveUsage = "live-usage";

    /// <summary>State a failed turn in <see cref="SessionEnded.Failure"/>, read from the door's own fields.</summary>
    public const string TypedFailures = "typed-failures";

    /// <summary>Run under <see cref="AgentSessionOptions.Account"/>: the account's home reaches the agent through
    /// <see cref="AgentProviderDescriptor.Accounts"/>.</summary>
    public const string Accounts = "accounts";

    /// <summary>Read an account's allowance through the vendor's own server (<see cref="IAgentUsageSource"/>). Not
    /// something a turn asks for or a stream emits: a host invokes it, after enabling it, and the claim names the
    /// versions it is validated on and the <see cref="AgentAbility.SideEffects"/> of the start.</summary>
    public const string UsageReading = "usage-reading";

    /// <summary>Hand the host a request the agent makes before it runs a tool, to answer, rather than decide it from
    /// <see cref="AgentSessionOptions.ToolPolicy"/>. Not something a turn asks for: a host registers the callback
    /// the door's own registration names, and a door with none states this <see cref="AgentSupport.Unsupported"/>.</summary>
    public const string PermissionRequests = "permission-requests";

    /// <summary>Run the turn under <see cref="AgentSessionOptions.PermissionMode"/>, the agent's own word for how it gates
    /// a tool use. A door that states it carries the word or refuses the turn before a spawn.</summary>
    public const string PermissionMode = "permission-mode";

    /// <summary>Run the turn at <see cref="AgentSessionOptions.Effort"/>, the agent's own word for how hard it works.
    /// A door that states it carries the word or refuses the turn before a spawn.</summary>
    public const string Effort = "effort";

    /// <summary>Load <see cref="AgentSessionOptions.PluginDirectories"/> for the turn.</summary>
    public const string Plugins = "plugins";

    /// <summary>Start the turn's child with <see cref="AgentSessionOptions.Environment"/>. A door with no process of its own
    /// states it <see cref="AgentSupport.Unsupported"/>.</summary>
    public const string Environment = "environment";

    /// <summary>Open an <see cref="IAgentRun"/> through <see cref="IAgentRunProvider"/>: one process kept across turns. Not
    /// something a turn asks for. A door states it <see cref="AgentSupport.Supported"/> only where a run of it was measured.</summary>
    public const string KeptRun = "kept-run";

    /// <summary>Take a second prompt on a run's busy process: the agent queues it and folds it into the running turn at its next
    /// step, so the earlier turn ends <c>folded</c>. Needs <see cref="KeptRun"/>.</summary>
    public const string QueuedWords = "queued-words";

    /// <summary>Stop a run's turn and then run an ordinary one on the same process and conversation. Needs <see cref="KeptRun"/>.</summary>
    public const string KeepingStop = "keeping-stop";

    /// <summary>Change the model between a run's turns. Needs <see cref="KeptRun"/>.</summary>
    public const string ModelReask = "model-reask";

    /// <summary>Tell the frames an agent replays when it loads a conversation from the ones it sends live. Needs <see cref="KeptRun"/>.</summary>
    public const string ReplayTold = "replay-told";

    /// <summary>Open the session again on a run's live process. The library never does it itself: an open that fails ends the
    /// run, and the host opens another one. Needs <see cref="KeptRun"/>.</summary>
    public const string RepeatableOpen = "repeatable-open";

    /// <summary>Every ability a provider states, in a fixed order.</summary>
    public static IReadOnlyList<string> All { get; } =
        [Resume, ReadOnlyGate, McpServers, DisallowedTools, SystemPrompt, Model, Steering, PlanEvents, LiveUsage, TypedFailures, Accounts, UsageReading, PermissionRequests,
            PermissionMode, Effort, Plugins, Environment, KeptRun, QueuedWords, KeepingStop, ModelReask, ReplayTold, RepeatableOpen];

    /// <summary>The abilities <paramref name="options"/> asks a door for. Only the option-shaped ones appear:
    /// an event ability (plan events, live usage) is something a door emits, not something a turn asks for.</summary>
    public static IReadOnlyList<string> Needed(AgentSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var needed = new List<string>(11);
        if (options.ResumeToken is not null) needed.Add(Resume);
        if (options.ToolPolicy == AgentToolPolicy.ReadOnly) needed.Add(ReadOnlyGate);
        if (options.McpServers.Count > 0) needed.Add(McpServers);
        if (options.DisallowedTools.Count > 0) needed.Add(DisallowedTools);
        if (!string.IsNullOrEmpty(options.SystemPrompt)) needed.Add(SystemPrompt);
        if (!string.IsNullOrEmpty(options.Model)) needed.Add(Model);
        if (options.Account is not null) needed.Add(Accounts);
        if (options.PermissionMode is not null) needed.Add(PermissionMode);
        if (options.Effort is not null) needed.Add(Effort);
        if (options.PluginDirectories.Count > 0) needed.Add(Plugins);
        if (options.Environment.Count > 0) needed.Add(Environment);
        return needed;
    }
}
