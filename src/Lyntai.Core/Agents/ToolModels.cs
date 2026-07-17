using Lyntai.Llm;

namespace Lyntai.Agents;

/// <summary>One tool round-trip inside a loop: the tool the model chose, the arguments it passed, and
/// the observation returned (or an <c>error: …</c> string when the tool was unknown or threw).</summary>
public sealed record ToolStep(string Tool, string ArgumentsJson, string Result);

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
}
