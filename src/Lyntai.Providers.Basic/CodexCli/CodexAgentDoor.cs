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

    /// <summary>The home variable and the two API-key variables the program reads, declared from its
    /// documentation; none is measured here.</summary>
    public static readonly AgentAccountMapping Mapping = new(AccountHomeVariable, ["OPENAI_API_KEY", "CODEX_API_KEY"]);

    public static AgentProviderDescriptor Describe(string providerId, string? agent = null) => new()
    {
        Id = providerId,
        Agent = agent,
        Accounts = Mapping,
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
                    Declared(AgentAbilities.Model, AgentSupport.Supported, "--model with the id"),
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
                        SideEffects = CliAgentProvider.ServerStart,
                    },
                    Declared(AgentAbilities.PermissionRequests, AgentSupport.Unsupported, "a turn is one non-interactive process: the sandbox decides, and no request reaches the host"),
                    Declared(AgentAbilities.PermissionMode, AgentSupport.Supported,
                        "the sandbox, as --sandbox with a word codex exec --help lists: " + string.Join(", ", CodexAgentArgs.SandboxModes)
                        + "; no approval word, since exec asks no one (its approval policy is never) and any other word refuses the turn"),
                    Declared(AgentAbilities.Effort, AgentSupport.Supported,
                        "-c model_reasoning_effort with a level from the maker's configuration reference, never run here: " + string.Join(", ", CodexAgentArgs.EffortLevels)
                        + "; any other refuses the turn, and a level the model does not take is the agent's own failure"),
                    Declared(AgentAbilities.Plugins, AgentSupport.Unsupported, "no plugin flag is measured here"),
                    Declared(AgentAbilities.Environment, AgentSupport.Supported, "the turn's variables in the child's environment, over the registration's and beneath the MCP bearer variables and the account's; a null removes one"),
                    Declared(AgentAbilities.KeptRun, AgentSupport.Unsupported, "exec takes its prompt once; a resume is a new process (read from the source, never run)"),
                    Declared(AgentAbilities.QueuedWords, AgentSupport.Unsupported, "exec reads no input after the start"),
                    Declared(AgentAbilities.KeepingStop, AgentSupport.Unknown, "no run exists to keep"),
                    Declared(AgentAbilities.ModelReask, AgentSupport.Unknown, "no run exists to ask again"),
                    Declared(AgentAbilities.ReplayTold, AgentSupport.Unknown, "no run exists"),
                    Declared(AgentAbilities.RepeatableOpen, AgentSupport.Unsupported, "a new open is a new process"),
                    Declared(AgentAbilities.ChildSessions, AgentSupport.Unsupported, "a host's audit of exec --json saw no child's lines, only a wait with no receivers; nothing here reads one"),
                    Declared(AgentAbilities.SignIn,AgentSupport.Supported, "codex login status (prose, measured) and codex login, under the account's CODEX_HOME, as the inference provider drives them"),
                ],
            },
        ],
    };

    private static AgentAbility Declared(string name, AgentSupport support, string evidence) =>
        new(name, support, AgentAbilitySource.Declared) { Evidence = evidence };
}
