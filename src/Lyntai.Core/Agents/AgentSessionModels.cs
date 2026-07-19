using Lyntai.Llm;

namespace Lyntai.Agents;

/// <summary>Read-only (plan gate) vs write (execute gate) tool policy for a gated agent session.</summary>
public enum AgentToolPolicy { ReadOnly, Write }

/// <summary>Neutral per-call inputs for an <see cref="IAgentSession"/>. Adapters subtype this to add
/// provider-specific options (e.g. ClaudeAgentOptions). Inheritable — do NOT seal.</summary>
public record AgentSessionOptions
{
    /// <summary>The user turn. Adapters send this over stdin, never argv.</summary>
    public required string Prompt { get; init; }
    public string? SystemPrompt { get; init; }
    public AgentToolPolicy ToolPolicy { get; init; } = AgentToolPolicy.ReadOnly;
    /// <summary>Opaque resume handle (a prior run's session id); null = a fresh session.</summary>
    public string? ResumeToken { get; init; }
    public string? Model { get; init; }
    /// <summary>Per-call timeout; null = the global default.</summary>
    public int? TimeoutSeconds { get; init; }
    public IReadOnlyList<string> DisallowedTools { get; init; } = [];
    /// <summary>Where a CLI-agent adapter runs the loop (its cwd). Adapters without a filesystem context ignore it.</summary>
    public string? WorkingDirectory { get; init; }
}

/// <summary>The caller-facing outcome of a session (the fold of the event stream).</summary>
public sealed record AgentSessionResult(
    string? SessionId, string FinalText, LlmVerdict Verdict, bool IsError,
    string? Subtype, string? Diagnostic, UsageFinal? Usage);
