using Lyntai.Inference;

namespace Lyntai.Agents;

/// <summary>One tool round-trip inside a loop: the tool the model chose, the arguments it passed, and
/// the observation returned (or an <c>error: …</c> string when the tool was unknown or threw).</summary>
public sealed record ToolStep(string Tool, string ArgumentsJson, string Result);

/// <summary>The error-observation marker shared by the loop's producer (unknown tool / a tool that threw)
/// and both stream doors' <c>ToolResult.IsError</c> flags — one prefix, so producer and readers can't drift.</summary>
internal static class ToolObservations
{
    public const string ErrorPrefix = "error:";
    public static bool IsError(string observation) => observation.StartsWith(ErrorPrefix, StringComparison.Ordinal);
}

/// <summary>Which transport carried a tool loop's calls.
/// <para>Worth reporting because the fallback is not a degradation of degree. Measured on one model both
/// ways (<c>docs/memory-measurements.md</c> §5): the prompt protocol invokes a tool on <b>90-100%</b> of
/// requests nothing on the roster serves against native's 20-30%, converges on 11.3-24.4% of runs against
/// 99.4-100%, and bills an extra repair round. A deployment on a model whose chat template carries no tool
/// section gets that column, and until this existed nothing said so at runtime.</para></summary>
public enum ToolTransport
{
    /// <summary>No tools were registered, so the loop made one plain completion and chose no transport.</summary>
    None,

    /// <summary>The provider's own function-calling, selected because
    /// <see cref="ITextClient.GetCapabilitiesAsync"/> said the serving backend declares it.</summary>
    Native,

    /// <summary>The loop's own prompt protocol, used because the serving backend declared no native support
    /// or its capabilities are unknown (<see cref="ITextClient.GetCapabilitiesAsync"/> answered null) — the
    /// fallback this enum exists to make visible.</summary>
    Prompt,
}

/// <summary>The outcome of an <see cref="IToolLoop"/> run: the final <paramref name="Answer"/>, the
/// <paramref name="Verdict"/> (Ok on a clean finish; a non-Ok LLM verdict is surfaced as-is; Failed
/// with a <paramref name="Detail"/> when the loop didn't converge), and every <see cref="ToolStep"/>
/// taken along the way (for tracing/debugging).</summary>
public sealed record ToolLoopResult(
    string Answer,
    ProviderVerdict Verdict,
    IReadOnlyList<ToolStep> Steps,
    string? Detail = null)
{
    public bool Ok => Verdict == ProviderVerdict.Ok;

    /// <summary>Aggregate token/cost usage across EVERY front-door call the loop made (summed
    /// input/output/cache-read tokens; <see cref="TextUsage.CostUsd"/> summed when any call reported one, else
    /// null). Null when no provider reported usage at all (e.g. a CLI provider that doesn't surface tokens).
    /// Gives a tool-loop consumer a per-run token/cost figure without wrapping <see cref="ITextClient"/> in its
    /// own front-door decorator.</summary>
    public TextUsage? Usage { get; init; }

    /// <summary>Which transport carried this run, or <c>null</c> when the loop did not report one.
    /// <para><b>Null and <see cref="ToolTransport.None"/> are not the same</b> and must not be collapsed:
    /// <c>None</c> is a positive claim that no tools were registered, while null is a BYO
    /// <see cref="IToolLoop"/> that never said. The built-in <see cref="ToolLoop"/> always reports one.</para>
    /// <para>An init-only property rather than a record parameter on purpose — widening the primary
    /// constructor would be a BINARY break for every caller that constructs this positionally.</para></summary>
    public ToolTransport? Transport { get; init; }
}
