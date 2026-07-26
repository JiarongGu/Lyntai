using Lyntai.Llm;

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

/// <summary>The outcome of an <see cref="IToolLoop"/> run: the final <paramref name="Answer"/>, the
/// <paramref name="Verdict"/> (Ok on a clean finish; a non-Ok LLM verdict is surfaced as-is; Failed
/// with a <paramref name="Detail"/> when the loop didn't converge), and every <see cref="ToolStep"/>
/// taken along the way (for tracing/debugging).</summary>
public sealed record ToolLoopResult(
    string Answer,
    LlmVerdict Verdict,
    IReadOnlyList<ToolStep> Steps,
    string? Detail = null)
{
    public bool Ok => Verdict == LlmVerdict.Ok;

    /// <summary>Aggregate token/cost usage across EVERY front-door call the loop made (summed
    /// input/output/cache-read tokens; <see cref="LlmUsage.CostUsd"/> summed when any call reported one, else
    /// null). Null when no provider reported usage at all (e.g. a CLI provider that doesn't surface tokens).
    /// Gives a tool-loop consumer a per-run token/cost figure without wrapping <see cref="ILlmClient"/> in its
    /// own front-door decorator.</summary>
    public LlmUsage? Usage { get; init; }
}
