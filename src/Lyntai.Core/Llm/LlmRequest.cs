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

    /// <summary>Whether this call wants the model's intermediate reasoning. <b>Advisory</b> — a provider
    /// that cannot express it ignores it, and a model that reasons anyway is not a defect. See
    /// <see cref="LlmReasoning"/> for why the option is neutral rather than a per-family prompt token, and
    /// for the 15× latency measurement that motivated it.</summary>
    public LlmReasoning Reasoning { get; init; } = LlmReasoning.Default;

    public IReadOnlyList<LlmTool>? Tools { get; init; }

    /// <summary>Per-feature routing/telemetry tag — what <see cref="LyntaiOptions.DefaultModelByConsumer"/>
    /// resolves a model by, and what <c>Budget.PerConsumer</c> caps and reports against. The tags this
    /// library itself uses are <see cref="LlmConsumers"/>; a consumer's own are free-form.</summary>
    public string Consumer { get; init; } = LlmConsumers.Default;

    /// <summary>An optional per-request timeout override (seconds) for the provider call — for a call that
    /// legitimately runs far longer than the global <see cref="LyntaiOptions.ProviderTimeout"/> (e.g. a
    /// CLI-agent run driving many steps) without inflating the timeout of every short call. Null = the
    /// resolved default (per-consumer, then global). Clamped to <see cref="LyntaiOptions.MaxProviderTimeout"/>.
    /// See <see cref="LyntaiOptions.ResolveTimeout(LlmRequest)"/>.</summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>An optional per-request refusal regex (case-insensitive). If an otherwise-<c>Ok</c> reply's
    /// text matches it, the reply is surfaced as <see cref="LlmVerdict.Refused"/> (no fallback) — a caller-
    /// supplied check on top of the central patterns, e.g. a per-language "I can't help with that" phrasing
    /// the provider returns as a normal completion. Applied at the front door, so it also re-screens a
    /// cached hit. A malformed pattern is logged and ignored (the reply passes through). Completion-path
    /// only — streamed replies aren't screened.</summary>
    public string? RefusalPattern { get; init; }
}

/// <summary>The per-feature <see cref="LlmRequest.Consumer"/> tags this library itself sends.
/// <para>Declared once rather than spelled at each call site, because a tag is a KEY a host writes its
/// <see cref="LyntaiOptions.DefaultModelByConsumer"/> and <c>Budget.PerConsumer</c> against — so a typo
/// does not fail, it silently opens a second bucket that no cap covers and no report names.</para>
/// <para>A consumer's own tags are free-form and need no entry here; these exist so the library's own
/// traffic is separable from the application's.</para></summary>
public static class LlmConsumers
{
    /// <summary>Anything untagged, and the fallback every unlisted tag resolves through.</summary>
    public const string Default = "default";

    /// <summary>An LLM judge or comparer in the cortex layer.</summary>
    public const string Scoring = "scoring";

    /// <summary>The memory subsystem's own model calls — annotation and verification.
    /// <para>Verification fires on EVERY recall, so this is the tag an operator most often wants to cap or
    /// watch on its own. Both seams billed to <see cref="Default"/> until 3.0, which made memory spend
    /// inseparable from the application's.</para></summary>
    public const string Memory = "memory";

    /// <summary>A tool the model drives itself, including the generation tools.</summary>
    public const string Agent = "agent";
}
