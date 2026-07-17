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

    public static LlmChunk Content(string text) => new() { Kind = LlmChunkKind.Content, Text = text };

    public static LlmChunk Final(LlmUsage? usage = null, string? detail = null) =>
        new() { Kind = LlmChunkKind.Final, Usage = usage, Detail = detail };

    public static LlmChunk Error(LlmVerdict verdict, string? detail = null) =>
        new() { Kind = LlmChunkKind.Error, Verdict = verdict, Detail = detail };
}
