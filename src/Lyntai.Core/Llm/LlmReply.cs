namespace Lyntai.Llm;

/// <summary>The outcome of a non-streaming completion. <paramref name="Detail"/> carries the error
/// context (stderr tail, HTTP status, …) when the verdict is not <see cref="LlmVerdict.Ok"/>.</summary>
public sealed record LlmReply(string Text, LlmVerdict Verdict, LlmUsage? Usage = null, string? Detail = null)
{
    /// <summary>Native tool calls the model requested (function-calling). Non-null + non-empty on an Ok
    /// reply means the model wants tools run before it can answer — the <see cref="Agents.IToolLoop"/>
    /// executes them and feeds the results back. Providers without native tool support leave this null.</summary>
    public IReadOnlyList<LlmToolCall>? ToolCalls { get; init; }
}
