namespace Lyntai.Llm;

public enum LlmChunkKind
{
    /// <summary>A piece of assistant text. The first Content chunk "commits" a stream: the router
    /// never falls back to another candidate after one has been yielded.</summary>
    Content,

    /// <summary>Successful end of stream; carries usage/cost when the provider reports them.</summary>
    Final,

    /// <summary>The stream ended in an error; <see cref="LlmChunk.Verdict"/> says how.</summary>
    Error,

    /// <summary>The model asked for a tool. <see cref="LlmChunk.ToolCall"/> carries it, COMPLETE — a
    /// provider assembles a vendor's incremental fragments before yielding, so partial JSON never reaches a
    /// consumer. Like <see cref="Content"/>, it COMMITS the stream: once a call has been announced the
    /// router cannot fall over, because the caller may already have executed it.</summary>
    ToolCall,
}

/// <summary>One streaming event. Providers yield Content* then exactly one Final or Error.</summary>
public sealed record LlmChunk
{
    public required LlmChunkKind Kind { get; init; }

    public string Text { get; init; } = "";

    /// <summary>Meaningful on <see cref="LlmChunkKind.Error"/> (and Ok on Final).</summary>
    public LlmVerdict Verdict { get; init; } = LlmVerdict.Ok;

    public LlmUsage? Usage { get; init; }

    public string? Detail { get; init; }

    /// <summary>The requested tool call, on a <see cref="LlmChunkKind.ToolCall"/> chunk. Always COMPLETE:
    /// vendors stream tool calls as fragments (an id here, a name there, arguments in pieces) and assembling
    /// them is the PROVIDER's job, so this is never partial JSON a consumer has to accumulate.</summary>
    /// <remarks>Added in 3.0. Before it, the streaming contract carried no tool-call payload at all — which
    /// made a native tool-calling turn unstreamable (<c>ToolLoop</c> had to buffer the whole turn through
    /// <c>CompleteAsync</c>, losing time-to-first-token for every agentic answer) and, worse, silently DROPPED
    /// a call from any turn that streamed prose alongside one.</remarks>
    public LlmToolCall? ToolCall { get; init; }

    public static LlmChunk Content(string text) => new() { Kind = LlmChunkKind.Content, Text = text };

    /// <summary>A requested tool call. Yield one per call, each complete.</summary>
    /// <remarks><b>Named <c>Tool</c>, not <c>ToolCall</c>, by CONSTRAINT</b>: every sibling factory matches
    /// its kind, but a type cannot carry a method and a property of the same name, and
    /// <see cref="ToolCall"/> is the better name for the PAYLOAD.</remarks>
    public static LlmChunk Tool(LlmToolCall call) => new() { Kind = LlmChunkKind.ToolCall, ToolCall = call };

    public static LlmChunk Final(LlmUsage? usage = null, string? detail = null) =>
        new() { Kind = LlmChunkKind.Final, Usage = usage, Detail = detail };

    public static LlmChunk Error(LlmVerdict verdict, string? detail = null) =>
        new() { Kind = LlmChunkKind.Error, Verdict = verdict, Detail = detail };
}
