using Lyntai.Agents;
using Lyntai.Providers.Basic;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>What the <c>claude</c> CLI door states about itself, for <see cref="AgentProviderDescriptor"/>.
/// Every claim is declared from the adapter's own argv and reader, or, where it names a version, from a host's recorded run of
/// that version; none is a measurement of the library's own, and an ability no record shows is unknown rather than supported.</summary>
internal static class ClaudeAgentDoor
{
    public const string DoorId = "cli";

    /// <summary>The version a host's record of a kept run and its stop was taken on (its stream-json note).</summary>
    private const string KeptRunRecord = "claude 2.1.281";

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
                    Claim(AgentAbilities.Steering, AgentSupport.Unsupported, "the CLI takes no steer: a line with priority now ends the running turn with its own result and runs the words as the next turn (a host's steer note, 2.1.322), which is a stop and a send; a run sends no priority and no word into a running turn"),
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
                    Recorded(AgentAbilities.KeptRun, "two user lines on one `-p --input-format stream-json` process, each its own turn ended by its own result, recorded by a host's run (its stream-json note); IAgentRunProvider opens it, and the open is the spawn"),
                    Claim(AgentAbilities.QueuedWords, AgentSupport.Unknown, "a line with no priority folds into a tool step (one result) but is the next turn after a streaming step (two results), recorded on 2.1.322 by a host's run (its steer note); without the replay echo, recorded for the tool step only, nothing tells which, so a run refuses a word on a busy run"),
                    Recorded(AgentAbilities.KeepingStop, "an interrupt control request, answered by a control_response; the turn ends error_during_execution with terminal_reason aborted_streaming or aborted_tools, read as cancelled, and the next user line is an ordinary turn (a host's stream-json note); StopAsync sends it"),
                    Claim(AgentAbilities.ModelReask, AgentSupport.Unknown, "the model is the launch's --model, and no run changed it between turns"),
                    Claim(AgentAbilities.ReplayTold, AgentSupport.Unknown, "--replay-user-messages echoes a folded word, recorded for a tool step only (a host's steer note); a run passes no such flag and replays nothing"),
                    Claim(AgentAbilities.RepeatableOpen, AgentSupport.Unsupported, "the CLI has no open to repeat: a run's open is its spawn, and a new open is a new process"),
                    Claim(AgentAbilities.ChildSessions, AgentSupport.Unsupported, "the stream carries a child's lines among the session's own, stamped parent_tool_use_id (ParentCallId); the spawning call stays in the parent's stream and no session of the child's own exists"),
                    Claim(AgentAbilities.SignIn,AgentSupport.Supported, "claude auth status --json and claude auth login, under the account's CLAUDE_CONFIG_DIR, as the inference provider drives them"),
                ],
            },
        ],
    };

    private static AgentAbility Claim(string name, AgentSupport support, string evidence) =>
        new(name, support, AgentAbilitySource.Declared) { Evidence = evidence };

    private static AgentAbility Recorded(string name, string evidence) =>
        new(name, AgentSupport.Supported, AgentAbilitySource.Declared) { Evidence = evidence, AppliesTo = KeptRunRecord };
}
