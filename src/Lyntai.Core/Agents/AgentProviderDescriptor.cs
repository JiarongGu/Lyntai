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
}

/// <summary>The kinds of door the library names. A door's <see cref="AgentDoor.Kind"/> is open: a host's own
/// provider states its own.</summary>
public static class AgentDoorKinds
{
    /// <summary>An agent run as a command-line process, one process per turn.</summary>
    public const string Cli = "cli";

    /// <summary>An agent reached over the Agent Client Protocol.</summary>
    public const string Acp = "acp";
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

    /// <summary>Take a word from the host while a turn is running.</summary>
    public const string Steering = "steering";

    /// <summary>Emit <see cref="PlanUpdated"/> when the agent keeps a plan.</summary>
    public const string PlanEvents = "plan-events";

    /// <summary>Emit <see cref="UsageLive"/> while a turn is running, not only when it ends.</summary>
    public const string LiveUsage = "live-usage";

    /// <summary>State a failed turn in <see cref="SessionEnded.Failure"/>, read from the door's own fields.</summary>
    public const string TypedFailures = "typed-failures";

    /// <summary>Every ability a provider states, in a fixed order.</summary>
    public static IReadOnlyList<string> All { get; } =
        [Resume, ReadOnlyGate, McpServers, DisallowedTools, SystemPrompt, Steering, PlanEvents, LiveUsage, TypedFailures];

    /// <summary>The abilities <paramref name="options"/> asks a door for. Only the option-shaped ones appear:
    /// an event ability (plan events, live usage) is something a door emits, not something a turn asks for.</summary>
    public static IReadOnlyList<string> Needed(AgentSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var needed = new List<string>(5);
        if (options.ResumeToken is not null) needed.Add(Resume);
        if (options.ToolPolicy == AgentToolPolicy.ReadOnly) needed.Add(ReadOnlyGate);
        if (options.McpServers.Count > 0) needed.Add(McpServers);
        if (options.DisallowedTools.Count > 0) needed.Add(DisallowedTools);
        if (!string.IsNullOrEmpty(options.SystemPrompt)) needed.Add(SystemPrompt);
        return needed;
    }
}
