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
}
