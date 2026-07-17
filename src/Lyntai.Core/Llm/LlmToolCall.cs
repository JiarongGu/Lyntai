namespace Lyntai.Llm;

/// <summary>A model's request to call a tool, surfaced on <see cref="LlmReply.ToolCalls"/> by providers
/// that support native (structured) function-calling. <paramref name="ArgumentsJson"/> is the raw JSON
/// arguments object the model produced — the same shape <see cref="Lyntai.Agents.ITool.InvokeAsync"/>
/// consumes. <paramref name="Id"/> correlates the call with the tool result fed back
/// (<see cref="LlmMessage.ToolResult"/>).</summary>
public sealed record LlmToolCall(string Id, string Name, string ArgumentsJson);
