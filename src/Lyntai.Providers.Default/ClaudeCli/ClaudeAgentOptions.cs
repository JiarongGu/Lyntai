using Lyntai.Agents;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>Claude-CLI-specific session options. All the claude-only flags live HERE, never in Core.</summary>
public sealed record ClaudeAgentOptions : AgentSessionOptions
{
    /// <summary>--settings: the scope-guard hooks file (PreToolUse jail). The adopter's security boundary.</summary>
    public string? SettingsPath { get; init; }
    /// <summary>--mcp-config: an app-hosted, out-of-process MCP server (distinct from the in-proc ICliToolProvisioner).</summary>
    public string? McpConfigPath { get; init; }
    /// <summary>--allowedTools for that MCP server.</summary>
    public IReadOnlyList<string> AllowedTools { get; init; } = [];

    /// <summary>--dangerously-skip-permissions: bypass ALL tool-permission prompts. OPT-IN and DANGEROUS —
    /// intended for an agent running on the user's OWN machine against the user's OWN resources, where a
    /// headless <c>-p</c> run has no responder and ANY prompt (Read/Grep/Bash, every <c>mcp__*</c> tool)
    /// would hang the turn. When set, <see cref="ClaudeAgentArgs.Build"/> emits
    /// <c>--dangerously-skip-permissions</c> and SUPPRESSES the conflicting <c>--permission-mode</c> /
    /// <c>--allowedTools</c> (the CLI rejects combining them). The always-denied flow tools
    /// (AskUserQuestion/ExitPlanMode/EnterPlanMode) and the caller's
    /// <see cref="AgentSessionOptions.DisallowedTools"/> (and the ReadOnly write-tool denial) are STILL honored
    /// — bypassing prompts is not the same as un-denying an explicitly denied tool. Leave false unless you
    /// truly want no gate.</summary>
    public bool SkipAllPermissions { get; init; }
}
