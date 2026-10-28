using Lyntai.Agents;
using Lyntai.Providers.Basic;

namespace Lyntai.Providers.CodexCli;

/// <summary>What the <c>codex</c> CLI door states about itself, for <see cref="AgentProviderDescriptor"/>.
/// <see cref="AgentAbilities.Resume"/> is the one claim measured on the wire; the rest are declared from the
/// adapter's own argv and reader, and a plan read from the published event schema alone is unknown.</summary>
internal static class CodexAgentDoor
{
    public const string DoorId = "cli";

    public const string AccountHomeVariable = "CODEX_HOME";

    public static AgentProviderDescriptor Describe(string providerId) => new()
    {
        Id = providerId,
        Accounts = new AgentAccountMapping(AccountHomeVariable),
        Doors =
        [
            new AgentDoor
            {
                Id = DoorId,
                Kind = AgentDoorKinds.Cli,
                Abilities =
                [
                    new AgentAbility(AgentAbilities.Resume, AgentSupport.Supported, AgentAbilitySource.Measured)
                        { Evidence = "exec resume, measured against a real codex" },
                    Declared(AgentAbilities.ReadOnlyGate, AgentSupport.Supported, "--sandbox read-only"),
                    Declared(AgentAbilities.McpServers, AgentSupport.Supported, "-c overrides; an unrenderable server refuses the turn"),
                    Declared(AgentAbilities.DisallowedTools, AgentSupport.Unsupported, "its tool gate is the sandbox, not a deny list"),
                    Declared(AgentAbilities.SystemPrompt, AgentSupport.Supported, "a leading block of the prompt; exec has no flag"),
                    Declared(AgentAbilities.Steering, AgentSupport.Unsupported, "a turn is one process fed its prompt on stdin"),
                    Declared(AgentAbilities.PlanEvents, AgentSupport.Unknown, "read from the published todo_list item; no real capture"),
                    Declared(AgentAbilities.LiveUsage, AgentSupport.Unsupported, "usage arrives once, on turn.completed"),
                    Declared(AgentAbilities.TypedFailures, AgentSupport.Supported, "turn.failed's message, never the agent's items; no typed field on the wire"),
                    Declared(AgentAbilities.Accounts, AgentSupport.Supported, "the account's home in CODEX_HOME for the child"),
                    new AgentAbility(AgentAbilities.UsageReading, AgentSupport.Supported, AgentAbilitySource.Declared)
                    {
                        Evidence = "account/rateLimits/read on `codex app-server`, no model request; a start created state "
                            + "databases and skills in the home and made a network request",
                        AppliesTo = string.Join(", ", CodexUsageDialect.Validated.Order(StringComparer.Ordinal)),
                        SideEffects = UsageAgentProvider.ServerStart,
                    },
                ],
            },
        ],
    };

    private static AgentAbility Declared(string name, AgentSupport support, string evidence) =>
        new(name, support, AgentAbilitySource.Declared) { Evidence = evidence };
}
