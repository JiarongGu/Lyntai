using Lyntai.Agents;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>What the <c>claude</c> CLI door states about itself, for <see cref="AgentProviderDescriptor"/>.
/// Every claim is declared from the adapter's own argv and reader; none is a wire measurement, and an ability
/// the adapter has not been run against is unknown rather than supported.</summary>
internal static class ClaudeAgentDoor
{
    public const string DoorId = "cli";

    public static AgentProviderDescriptor Describe(string providerId) => new()
    {
        Id = providerId,
        Doors =
        [
            new AgentDoor
            {
                Id = DoorId,
                Kind = AgentDoorKinds.Cli,
                Abilities =
                [
                    Claim(AgentAbilities.Resume, AgentSupport.Supported, "--resume with the token"),
                    Claim(AgentAbilities.ReadOnlyGate, AgentSupport.Supported, "a deny list of the write tools"),
                    Claim(AgentAbilities.McpServers, AgentSupport.Supported, "--mcp-config; an unrenderable server refuses the turn"),
                    Claim(AgentAbilities.DisallowedTools, AgentSupport.Supported, "--disallowed-tools, merged with the adapter's own"),
                    Claim(AgentAbilities.SystemPrompt, AgentSupport.Supported, "--append-system-prompt"),
                    Claim(AgentAbilities.Steering, AgentSupport.Unsupported, "a turn is one process fed its prompt on stdin"),
                    Claim(AgentAbilities.PlanEvents, AgentSupport.Supported, "a TodoWrite call read as the plan; no real capture"),
                    Claim(AgentAbilities.LiveUsage, AgentSupport.Supported, "UsageLive on each assistant message"),
                    Claim(AgentAbilities.TypedFailures, AgentSupport.Supported, "the result's error fields before any sentence"),
                ],
            },
        ],
    };

    private static AgentAbility Claim(string name, AgentSupport support, string evidence) =>
        new(name, support, AgentAbilitySource.Declared) { Evidence = evidence };
}
