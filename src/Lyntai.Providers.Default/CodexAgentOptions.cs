using Lyntai.Agents;

namespace Lyntai.Providers.CodexCli;

/// <summary>codex-CLI-specific session options. All the codex-only knobs live HERE, never in Core.</summary>
/// <remarks>Deliberately thin compared with <c>ClaudeAgentOptions</c>: a knob is added here only for a flag
/// this repository has MEASURED on the CLI. <c>codex exec</c> has no per-tool deny list and no
/// append-system-prompt flag, so neither is faked — see <see cref="CodexAgentSession"/> for what happens to
/// <see cref="AgentSessionOptions.DisallowedTools"/> and <see cref="AgentSessionOptions.SystemPrompt"/>.</remarks>
public sealed record CodexAgentOptions : AgentSessionOptions
{
    /// <summary><c>--sandbox</c>, set explicitly instead of derived from
    /// <see cref="AgentSessionOptions.ToolPolicy"/>. Null (the default) means the policy decides:
    /// <see cref="AgentToolPolicy.ReadOnly"/> → <c>read-only</c>,
    /// <see cref="AgentToolPolicy.Write"/> → <c>workspace-write</c>. Set it to escape that mapping — most
    /// usefully to <c>danger-full-access</c>, which lets the agent act outside its workspace and is exactly
    /// as dangerous as it sounds. The three values are the ones codex-cli 0.146.0's <c>--help</c> lists; an
    /// unknown value is passed through and the CLI rejects it.</summary>
    public string? SandboxMode { get; init; }
}
