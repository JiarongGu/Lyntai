using Lyntai.Llm;

namespace Lyntai.Agents;

/// <summary>One turn to run through the orchestration: the user <see cref="Message"/>, an optional
/// <see cref="ChatTurn.System"/> prompt, and memory scoping. When <see cref="TaskKey"/> is set, prior
/// memory is recalled into the prompt and (if <see cref="Remember"/>) the exchange is written back.</summary>
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
/// a guard <paramref name="Blocked"/> it (input or output gate), and any tool steps.
/// <paramref name="Detail"/> carries the guard's block reason when <see cref="Blocked"/>, else the LLM
/// failure detail on a non-Ok verdict (timeout text, provider error); null on a clean success.
/// (Renamed from <c>BlockReason</c>, which lied for every non-Ok, non-blocked outcome.)</summary>
public sealed record ChatResult(
    string Answer,
    LlmVerdict Verdict,
    bool Blocked,
    string? Detail,
    IReadOnlyList<ToolStep> ToolSteps)
{
    public bool Ok => Verdict == LlmVerdict.Ok && !Blocked;

    /// <summary>Aggregate token/cost usage for the turn — <see cref="ToolLoopResult.Usage"/> when the turn
    /// went through the tool loop (already summed across every call the loop made), else the single
    /// completion's <see cref="LlmReply.Usage"/>. Shaped like the loop's: null when no provider reported
    /// usage at all (e.g. a CLI provider that doesn't surface tokens), never a misleading all-zero figure.
    /// <para>It is what the turn COST, not what it returned: a non-Ok verdict and an output-gate block both
    /// carry the tokens they spent. Null when the turn never reached a provider — an input gate that blocked
    /// before the model. Gives a chat consumer a per-turn token/cost figure without wrapping
    /// <see cref="Lyntai.Llm.ILlmClient"/> in its own front-door decorator.</para></summary>
    public LlmUsage? Usage { get; init; }
}
