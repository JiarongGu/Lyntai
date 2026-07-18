namespace Lyntai.Llm;

/// <summary>A canonical completion request; providers translate it to their native schema.</summary>
public sealed record LlmRequest
{
    public required IReadOnlyList<LlmMessage> Messages { get; init; }

    /// <summary>Model id; a provider resolves null to its own default.</summary>
    public string? Model { get; init; }

    public int? MaxTokens { get; init; }

    public double? Temperature { get; init; }

    /// <summary>Structured output: a JSON schema the reply must conform to (optional).</summary>
    public string? JsonSchema { get; init; }

    public IReadOnlyList<LlmTool>? Tools { get; init; }

    /// <summary>Per-feature routing/telemetry tag (e.g. "scoring", "chat").</summary>
    public string Consumer { get; init; } = "default";

    /// <summary>An optional per-request timeout override (seconds) for the provider call — for a call that
    /// legitimately runs far longer than the global <see cref="LyntaiOptions.ProviderTimeout"/> (e.g. a
    /// CLI-agent run driving many steps) without inflating the timeout of every short call. Null = the
    /// resolved default (per-consumer, then global). Clamped to <see cref="LyntaiOptions.MaxProviderTimeout"/>.
    /// See <see cref="LyntaiOptions.ResolveTimeout"/>.</summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>An optional per-request refusal regex (case-insensitive). If an otherwise-<c>Ok</c> reply's
    /// text matches it, the reply is surfaced as <see cref="LlmVerdict.Refused"/> (no fallback) — a caller-
    /// supplied check on top of the central patterns, e.g. a per-language "I can't help with that" phrasing
    /// the provider returns as a normal completion. Applied at the front door, so it also re-screens a
    /// cached hit. A malformed pattern is logged and ignored (the reply passes through). Completion-path
    /// only — streamed replies aren't screened.</summary>
    public string? RefusalPattern { get; init; }
}
