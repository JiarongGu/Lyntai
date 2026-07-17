namespace Lyntai.Llm;

/// <summary>The outcome of a non-streaming completion. <paramref name="Detail"/> carries the error
/// context (stderr tail, HTTP status, …) when the verdict is not <see cref="LlmVerdict.Ok"/>.</summary>
public sealed record LlmReply(string Text, LlmVerdict Verdict, LlmUsage? Usage = null, string? Detail = null);
