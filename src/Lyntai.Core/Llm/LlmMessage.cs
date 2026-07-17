namespace Lyntai.Llm;

/// <summary>One chat message. Roles follow the common convention: "system" | "user" | "assistant" |
/// "tool". <see cref="Content"/> is never null (an assistant tool-call turn carries "" and the tool
/// calls travel on <see cref="ToolCalls"/>); providers serialize that turn's content as null.</summary>
public sealed record LlmMessage(string Role, string Content)
{
    /// <summary>On an assistant turn, the tool calls the model made (function-calling). Null for a plain
    /// text turn.</summary>
    public IReadOnlyList<LlmToolCall>? ToolCalls { get; init; }

    /// <summary>On a tool-result turn (role "tool"), the id of the <see cref="LlmToolCall"/> this result
    /// answers. Null otherwise.</summary>
    public string? ToolCallId { get; init; }

    public static LlmMessage System(string content) => new("system", content);
    public static LlmMessage User(string content) => new("user", content);
    public static LlmMessage Assistant(string content) => new("assistant", content);

    /// <summary>An assistant turn that made <paramref name="toolCalls"/> (no text content).</summary>
    public static LlmMessage AssistantToolCalls(IReadOnlyList<LlmToolCall> toolCalls) =>
        new("assistant", "") { ToolCalls = toolCalls };

    /// <summary>A tool-result turn answering the call <paramref name="toolCallId"/> with
    /// <paramref name="content"/> (the observation the tool returned).</summary>
    public static LlmMessage ToolResult(string toolCallId, string content) =>
        new("tool", content) { ToolCallId = toolCallId };
}
