using Lyntai.Agents;

namespace Lyntai.Providers.Acp;

/// <summary>What the Agent Client Protocol door states about itself, for <see cref="AgentProviderDescriptor"/>.
/// Nothing here is <see cref="AgentAbilitySource.Measured"/> for an agent: the door is generic, so it states what
/// IT does, and what only an agent's <c>initialize</c> answer can say (whether it continues a conversation) is
/// <see cref="AgentSupport.Unknown"/> with the source <see cref="AgentAbilitySource.Negotiated"/>: settled at run
/// time, per agent, and refused before the prompt when the agent states it cannot.</summary>
internal static class AcpAgentDoor
{
    public const string DoorId = "acp";

    public static AgentProviderDescriptor Describe(string providerId, string? accountHomeVariable) => new()
    {
        Id = providerId,
        Accounts = accountHomeVariable is null ? null : new AgentAccountMapping(accountHomeVariable),
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
                    Declared(AgentAbilities.Steering, AgentSupport.Unsupported, "a turn is one prompt in one process; a second is never sent during it"),
                    Declared(AgentAbilities.PlanEvents, AgentSupport.Unknown, "the plan update is in the published schema and mapped; whether an agent sends one is its own"),
                    Declared(AgentAbilities.LiveUsage, AgentSupport.Unsupported, "usage_update carries context figures, not counts; the prompt's answer carries the turn's usage"),
                    Declared(AgentAbilities.TypedFailures, AgentSupport.Supported, "the JSON-RPC error's code and data, and the AIR sessionFailure field the door declares at initialize"),
                    Declared(AgentAbilities.Accounts,
                        accountHomeVariable is null ? AgentSupport.Unsupported : AgentSupport.Supported,
                        accountHomeVariable is null
                            ? "registered without the agent's configuration-home variable"
                            : $"the account's home in {accountHomeVariable} for the child"),
                    Declared(AgentAbilities.UsageReading, AgentSupport.Unsupported, "the door reads no allowance: the protocol has no usage question, and a turn reports its own usage"),
                ],
            },
        ],
    };

    private static AgentAbility Declared(string name, AgentSupport support, string evidence) =>
        new(name, support, AgentAbilitySource.Declared) { Evidence = evidence };
}
