using Lyntai.Inference;

namespace Lyntai.Inference;

/// <summary>A canonical completion request; providers translate it to their native schema.</summary>
public sealed record TextRequest
{
    public required IReadOnlyList<TextMessage> Messages { get; init; }

    /// <summary>Model id; a provider resolves null to its own default.</summary>
    public string? Model { get; init; }

    public int? MaxTokens { get; init; }

    public double? Temperature { get; init; }

    /// <summary>Structured output: a JSON schema the reply must conform to (optional).</summary>
    public string? JsonSchema { get; init; }

    /// <summary>Whether this call wants the model's intermediate reasoning. <b>Advisory</b> — a provider
    /// that cannot express it ignores it, and a model that reasons anyway is not a defect. See
    /// <see cref="TextReasoning"/> for why the option is neutral rather than a per-family prompt token, and
    /// for the 15× latency measurement that motivated it.</summary>
    public TextReasoning Reasoning { get; init; } = TextReasoning.Default;

    public IReadOnlyList<TextTool>? Tools { get; init; }

    /// <summary>Per-feature routing/telemetry tag — what <see cref="LyntaiOptions.DefaultModelByConsumer"/>
    /// resolves a model by, and what <c>Budget.PerConsumer</c> caps and reports against. The tags this
    /// library itself uses are <see cref="ProviderConsumers"/>; a consumer's own are free-form.</summary>
    public string Consumer { get; init; } = ProviderConsumers.Default;

    /// <summary>An optional per-request timeout override (seconds) for the provider call — for a call that
    /// legitimately runs far longer than the global <see cref="LyntaiOptions.ProviderTimeout"/> (e.g. a
    /// CLI-agent run driving many steps) without inflating the timeout of every short call. Null = the
    /// resolved default (per-consumer, then global). Clamped to <see cref="LyntaiOptions.MaxProviderTimeout"/>.
    /// See <see cref="LyntaiOptions.ResolveTimeout(TextRequest)"/>.</summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>An optional per-request refusal regex (case-insensitive). If an otherwise-<c>Ok</c> reply's
    /// text matches it, the reply is surfaced as <see cref="ProviderVerdict.Refused"/> (no fallback) — a caller-
    /// supplied check on top of the central patterns, e.g. a per-language "I can't help with that" phrasing
    /// the provider returns as a normal completion. Applied at the front door, so it also re-screens a
    /// cached hit. A malformed pattern is logged and ignored (the reply passes through). Completion-path
    /// only — streamed replies aren't screened.</summary>
    public string? RefusalPattern { get; init; }
}
