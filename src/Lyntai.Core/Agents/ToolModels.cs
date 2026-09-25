using Lyntai.Inference;

namespace Lyntai.Agents;

/// <summary>One tool round-trip inside a loop: the tool the model chose, the arguments it passed, and
/// the observation returned (or an <c>error: …</c> string when the tool was unknown or threw).</summary>
public sealed record ToolStep(string Tool, string ArgumentsJson, string Result);

/// <summary>The error-observation convention: an observation that starts with <see cref="ErrorPrefix"/> tells
/// the model its tool call failed — unknown, thrown, refused or reported as an error by the tool's own
/// server — and sets <see cref="ToolResult.IsError"/> on a streamed result. Build one with
/// <see cref="Error"/>, so every producer writes the prefix the readers test for.</summary>
public static class ToolObservations
{
    /// <summary>The prefix every error observation starts with.</summary>
    public const string ErrorPrefix = "error:";

    /// <summary>An error observation carrying <paramref name="message"/>.</summary>
    public static string Error(string message) => $"{ErrorPrefix} {message}";

    /// <summary>Whether <paramref name="observation"/> reports a failed call.</summary>
    public static bool IsError(string observation) => observation.StartsWith(ErrorPrefix, StringComparison.Ordinal);
}

/// <summary>Which transport carried a tool loop's calls.
/// <para>Worth reporting because the fallback is not a degradation of degree: on the same model, the prompt
/// protocol calls a tool far more often when nothing on the roster serves, converges far less often, and
/// bills an extra repair round (<c>docs/memory-measurements.md</c> §5, <c>affordance-native-false-calls</c>).
/// A deployment on a model whose chat template carries no tool section gets that behaviour, and this is
/// what says so at runtime.</para></summary>
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
    /// <see cref="IToolLoop"/> that never said. The built-in <see cref="ToolLoop"/> always reports one.</para></summary>
    public ToolTransport? Transport { get; init; }
}
