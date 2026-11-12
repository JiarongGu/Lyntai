using Lyntai.Agents;
using Lyntai.Providers.Basic;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>What the <c>claude</c> CLI door states about itself, for <see cref="AgentProviderDescriptor"/>.
/// Every claim is declared from the adapter's own argv and reader; none is a wire measurement, and an ability
/// the adapter has not been run against is unknown rather than supported.</summary>
internal static class ClaudeAgentDoor
{
    public const string DoorId = "cli";

    public const string AccountHomeVariable = "CLAUDE_CONFIG_DIR";

    /// <summary>The home variable and the credentials and gateway address the program reads, declared from its
    /// documentation; none is measured here.</summary>
    public static readonly AgentAccountMapping Mapping = new(
        AccountHomeVariable, ["ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN", "ANTHROPIC_BASE_URL"]);

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
                    Claim(AgentAbilities.Resume, AgentSupport.Supported, "--resume with the token"),
                    Claim(AgentAbilities.ReadOnlyGate, AgentSupport.Supported, "a deny list of the write tools"),
                    Claim(AgentAbilities.McpServers, AgentSupport.Supported, "--mcp-config; an unrenderable server refuses the turn"),
                    Claim(AgentAbilities.DisallowedTools, AgentSupport.Supported, "--disallowed-tools, merged with the adapter's own"),
                    Claim(AgentAbilities.SystemPrompt, AgentSupport.Supported, "--append-system-prompt"),
                    Claim(AgentAbilities.Model, AgentSupport.Supported, "--model with the id; the CLI ends a turn on a model it does not know, model_not_found"),
                    Claim(AgentAbilities.Steering, AgentSupport.Unsupported, "a turn is one process fed its prompt on stdin"),
                    Claim(AgentAbilities.PlanEvents, AgentSupport.Supported, "a TodoWrite call read as the plan; no real capture"),
                    Claim(AgentAbilities.LiveUsage, AgentSupport.Supported, "UsageLive on each assistant message"),
                    Claim(AgentAbilities.TypedFailures, AgentSupport.Supported, "the result's error fields before any sentence"),
                    Claim(AgentAbilities.Accounts, AgentSupport.Supported, "the account's home in CLAUDE_CONFIG_DIR for the child"),
                    new AgentAbility(AgentAbilities.UsageReading, AgentSupport.Supported, AgentAbilitySource.Declared)
                    {
                        Evidence = "get_usage control request to `claude -p --input-format stream-json`, no model request; "
                            + "a start wrote .claude.json, backups, plugins and sessions into the home",
                        AppliesTo = string.Join(", ", ClaudeUsageDialect.Validated.Order(StringComparer.Ordinal)),
                        SideEffects = CliAgentProvider.ServerStart,
                    },
                    Claim(AgentAbilities.PermissionRequests, AgentSupport.Unsupported, "a turn is one non-interactive process: the tool policy and the deny list decide, and no request reaches the host"),
                    Claim(AgentAbilities.PermissionMode, AgentSupport.Supported,
                        "--permission-mode with a word `claude --help` lists (2.1.319), or `default`, which that version accepts unlisted: " + string.Join(", ", ClaudeAgentArgs.PermissionModes)
                        + "; any other word refuses the turn, and the tool policy's own mapping is not applied beside it. `--help` does not say that bypassPermissions equals --dangerously-skip-permissions, so none is claimed"),
                    Claim(AgentAbilities.Effort, AgentSupport.Supported,
                        "--effort with a level `claude --help` lists (2.1.319): " + string.Join(", ", ClaudeAgentArgs.EffortLevels) + "; any other refuses the turn"),
                    Claim(AgentAbilities.Plugins, AgentSupport.Supported, "--plugin-dir per fully qualified folder; its contents are never read"),
                    Claim(AgentAbilities.Environment, AgentSupport.Supported, "the turn's variables in the child's environment, over the registration's and beneath the account's; a null removes one"),
                    Claim(AgentAbilities.KeptRun, AgentSupport.Unknown, "not built: a host's record shows the CLI taking two turns on one process with stdin held open, and this door starts one process per turn"),
                    Claim(AgentAbilities.QueuedWords, AgentSupport.Unknown, "no run is built, and a word mid-turn on held stdin was read from the bundle and never run"),
                    Claim(AgentAbilities.KeepingStop, AgentSupport.Unknown, "no run is built"),
                    Claim(AgentAbilities.ModelReask, AgentSupport.Unknown, "no run is built"),
                    Claim(AgentAbilities.ReplayTold, AgentSupport.Unknown, "no run is built"),
                    Claim(AgentAbilities.RepeatableOpen, AgentSupport.Unsupported, "the CLI has no open to repeat: a new open is a new process"),
                    Claim(AgentAbilities.SignIn, AgentSupport.Supported, "claude auth status --json and claude auth login, under the account's CLAUDE_CONFIG_DIR, as the inference provider drives them"),
                ],
            },
        ],
    };

    private static AgentAbility Claim(string name, AgentSupport support, string evidence) =>
        new(name, support, AgentAbilitySource.Declared) { Evidence = evidence };
}
