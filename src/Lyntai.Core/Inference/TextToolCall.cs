namespace Lyntai.Inference;

/// <summary>A model's request to call a tool, surfaced on <see cref="TextResponse.ToolCalls"/> by providers
/// that support native (structured) function-calling. <paramref name="ArgumentsJson"/> is the raw JSON
/// arguments object the model produced — the same shape <see cref="Lyntai.Agents.ITool.InvokeAsync"/>
/// consumes. <paramref name="Id"/> correlates the call with the tool result fed back
/// (<see cref="TextMessage.ToolResult"/>).</summary>
public sealed record TextToolCall(string Id, string Name, string ArgumentsJson);
