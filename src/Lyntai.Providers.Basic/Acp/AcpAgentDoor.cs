using Lyntai.Agents;

namespace Lyntai.Providers.Acp;

/// <summary>What the Agent Client Protocol door states about itself, for <see cref="AgentProviderDescriptor"/>.
/// Nothing here is <see cref="AgentAbilitySource.Measured"/> for an agent: the door is generic, so it states what
/// IT does, and what only an agent's own answer can say (whether it continues a conversation, whether it takes a
/// model) is <see cref="AgentSupport.Unknown"/> with the source <see cref="AgentAbilitySource.Negotiated"/>:
/// settled at run time, per agent, and refused before the prompt when the agent states it cannot.</summary>
internal static class AcpAgentDoor
{
    public const string DoorId = "acp";

    public static AgentProviderDescriptor Describe(
        string providerId, string? accountHomeVariable, string? agent = null, IEnumerable<string>? accountVariables = null,
        bool permissionApprover = false, AcpPermissionWait permissionWait = AcpPermissionWait.HalfTheWindow,
        AcpKeptRun keptRun = AcpKeptRun.None) => new()
    {
        Id = providerId,
        Agent = agent,
        Accounts = accountHomeVariable is null ? null : new AgentAccountMapping(accountHomeVariable, accountVariables),
        Doors =
        [
            new AgentDoor
            {
                Id = DoorId,
                Kind = AgentDoorKinds.Acp,
                Abilities =
                [
                    new AgentAbility(AgentAbilities.Resume, AgentSupport.Unknown, AgentAbilitySource.Negotiated)
                        { Evidence = "per agent from initialize: sessionCapabilities.resume, else loadSession; neither refuses the turn before the prompt" },
                    Declared(AgentAbilities.ReadOnlyGate, AgentSupport.Supported, "every session/request_permission is rejected; an agent in a mode that asks for nothing is gated by its mode"),
                    Declared(AgentAbilities.McpServers, AgentSupport.Supported, "session mcpServers; an http server needs the agent's mcpCapabilities.http, else the turn is refused"),
                    Declared(AgentAbilities.DisallowedTools, AgentSupport.Unsupported, "the protocol has no deny list"),
                    Declared(AgentAbilities.SystemPrompt, AgentSupport.Supported, "a leading text block of the prompt; the protocol has no system field"),
                    new AgentAbility(AgentAbilities.Model, AgentSupport.Unknown, AgentAbilitySource.Negotiated)
                        { Evidence = "per agent from session/new: a config option of category model, set by session/set_config_option before the prompt; none, or an agent that refuses the id, ends the turn before the prompt" },
                    Declared(AgentAbilities.Steering, AgentSupport.Unsupported, "through IAgentSession a turn is one prompt in one process and a second is never sent during it; a word on a run is queued-words"),
                    Declared(AgentAbilities.PlanEvents, AgentSupport.Unknown, "the plan update is in the published schema and mapped; whether an agent sends one is its own"),
                    Declared(AgentAbilities.LiveUsage, AgentSupport.Unsupported, "usage_update carries context figures, not counts; the prompt's answer carries the turn's usage"),
                    Declared(AgentAbilities.TypedFailures, AgentSupport.Supported, "the JSON-RPC error's code and data, and the AIR sessionFailure field the door declares at initialize"),
                    Declared(AgentAbilities.Accounts,
                        accountHomeVariable is null ? AgentSupport.Unsupported : AgentSupport.Supported,
                        accountHomeVariable is null
                            ? "registered without the agent's configuration-home variable"
                            : $"the account's home in {accountHomeVariable}, and its entries among the declared variables, for the child"),
                    Declared(AgentAbilities.UsageReading, AgentSupport.Unsupported, "the door reads no allowance: the protocol has no usage question, and a turn reports its own usage"),
                    Declared(AgentAbilities.PermissionRequests,
                        permissionApprover ? AgentSupport.Supported : AgentSupport.Unsupported,
                        permissionApprover
                            ? "each session/request_permission of a write turn is put to the registered IAcpPermissionApprover; a read-only turn rejects it unasked, and "
                                + (permissionWait == AcpPermissionWait.AsLongAsApproverHolds
                                    ? "the request waits as long as the approver holds it, with the runner's inactivity window paused meanwhile"
                                    : "no answer within half the inactivity window is a refusal")
                            : "no IAcpPermissionApprover is registered, so the turn's tool policy answers every request"),
                    new AgentAbility(AgentAbilities.PermissionMode, AgentSupport.Unknown, AgentAbilitySource.Negotiated)
                        { Evidence = "per agent from session/new: the modes answer's availableModes, set by session/set_mode before the prompt; an agent that offers no such id ends the turn before the prompt" },
                    new AgentAbility(AgentAbilities.Effort, AgentSupport.Unknown, AgentAbilitySource.Negotiated)
                        { Evidence = "per agent from session/new: a config option of category thought_level, set by session/set_config_option before the prompt; none ends the turn before the prompt" },
                    Declared(AgentAbilities.Plugins, AgentSupport.Unsupported, "the protocol names no plugin folder; the agent loads what its own configuration says"),
                    Declared(AgentAbilities.Environment, AgentSupport.Supported, "the turn's variables in the agent process's environment, over the registration's and beneath the account's; a null removes one"),
                    Declared(AgentAbilities.SignIn, AgentSupport.Unsupported, "the protocol's authenticate call is not made: an agent that wants a sign-in ends the turn as an access failure, and the host signs in through the agent's own program"),
                    .. KeptRunClaims(keptRun),
                ],
            },
        ],
    };

    // Only a measured record states a capability Supported; the rest of a kept run's abilities have none for any agent yet.
    private static IEnumerable<AgentAbility> KeptRunClaims(AcpKeptRun keptRun)
    {
        const string Record = "claude-agent-acp 0.84.0";
        var claude = keptRun == AcpKeptRun.ClaudeAgentAcp;
        yield return claude
            ? Recorded(AgentAbilities.KeptRun, Record, "a second session/prompt on one process after one initialize and one session/new, recorded by a host's run (its steer note); IAgentRunProvider opens it")
            : Declared(AgentAbilities.KeptRun, AgentSupport.Unknown, "the protocol allows more than one prompt on a session and only claude-agent-acp's record shows it; register that agent with AcpKeptRun.ClaudeAgentAcp");
        yield return claude
            ? Recorded(AgentAbilities.QueuedWords, Record, "a prompt sent while a turn runs is queued and folded at the next step: the earlier prompt answers end_turn with every usage count 0 (initialize states _meta.claudeCode.promptQueueing)")
            : Declared(AgentAbilities.QueuedWords, AgentSupport.Unknown, "codex-acp replaces its tracked prompt on a second one, and no record shows another agent queueing it");
        yield return Declared(AgentAbilities.KeepingStop, AgentSupport.Unknown, "a cancelled turn's zero counts are recorded and a usable next turn is not; a cancel with a word on its way loses its answer (read from source)");
        yield return Declared(AgentAbilities.ModelReask, AgentSupport.Unknown, "a model is set after session/new and before the first prompt only; no run set one between turns");
        yield return Declared(AgentAbilities.ReplayTold, AgentSupport.Unknown, "session/load replays the conversation as updates and session/resume sends none (read from source); no run loaded one, so the door drops what a load replays by position, not by telling");
        yield return Declared(AgentAbilities.RepeatableOpen, AgentSupport.Unknown, "the handshake refuses a second open and no run repeated one; the library never repeats an open itself");
    }

    private static AgentAbility Recorded(string name, string appliesTo, string evidence) =>
        new(name, AgentSupport.Supported, AgentAbilitySource.Declared) { Evidence = evidence, AppliesTo = appliesTo };

    private static AgentAbility Declared(string name, AgentSupport support, string evidence) =>
        new(name, support, AgentAbilitySource.Declared) { Evidence = evidence };
}
