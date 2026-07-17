using Lyntai.Llm;

namespace Lyntai.Agents;

/// <summary>One turn to run through the orchestration: the user <paramref name="Message"/>, an optional
/// <paramref name="System"/> prompt, and memory scoping. When <see cref="TaskKey"/> is set, prior memory
/// is recalled into the prompt and (if <see cref="Remember"/>) the exchange is written back.</summary>
public sealed record ChatTurn
{
    public required string Message { get; init; }

    public string? System { get; init; }

    /// <summary>Memory scope key — set it to recall/persist task-scoped memory; null = stateless.</summary>
    public string? TaskKey { get; init; }

    public string MemoryScope { get; init; } = "chat";

    public string Consumer { get; init; } = "chat";

    /// <summary>Route through the tool loop when tools are registered (the model can call them). If false,
    /// a plain completion.</summary>
    public bool UseTools { get; init; } = true;

    /// <summary>Write the exchange to memory afterward (needs <see cref="TaskKey"/>).</summary>
    public bool Remember { get; init; } = true;
}

/// <summary>The result of a chat turn: the <paramref name="Answer"/> (empty when blocked/failed), whether
/// a guard <paramref name="Blocked"/> it (input or output gate) with the reason, and any tool steps.</summary>
public sealed record ChatResult(
    string Answer,
    LlmVerdict Verdict,
    bool Blocked,
    string? BlockReason,
    IReadOnlyList<ToolStep> ToolSteps)
{
    public bool Ok => Verdict == LlmVerdict.Ok && !Blocked;
}
