namespace Lyntai.Generation;

/// <summary>
/// OPTIONAL capability of an <see cref="IGenerationProvider"/> that emits media AS IT IS PRODUCED — the shape
/// text-to-speech backends are built around (playback starts in well under a second, long before generation
/// finishes). Buffering that into a single result would throw away the only property that makes it useful.
/// </summary>
public interface IGenerationStreamProvider
{
    /// <summary>Stream the generation. Yields data chunks in order, then exactly ONE terminal chunk:
    /// <see cref="GenerationChunk.Final"/> when the stream completed, or a chunk carrying
    /// <see cref="GenerationChunk.Error"/> when it did not.</summary>
    IAsyncEnumerable<GenerationChunk> StreamAsync(GenerationRequest request, CancellationToken ct = default);
}

/// <summary>One piece of a streamed generation, or its terminal marker. Mirrors the LLM side's chunk
/// contract so the two streams behave the same way for a consumer.</summary>
/// <param name="Data">Media bytes, for a content chunk.</param>
/// <param name="MediaType">MIME type of the stream, on the first chunk at least.</param>
/// <param name="Error">Set on a terminal FAILURE chunk.</param>
/// <param name="Detail">The backend's words about the failure.</param>
/// <param name="Final">True on the terminal SUCCESS chunk.</param>
/// <param name="Usage">Cost/duration, where the backend reports it on completion.</param>
public sealed record GenerationChunk(
    byte[]? Data = null,
    string? MediaType = null,
    GenerationVerdict? Error = null,
    string? Detail = null,
    bool Final = false,
    GenerationUsage? Usage = null)
{
    /// <summary>A content chunk.</summary>
    public static GenerationChunk Content(byte[] data, string? mediaType = null) => new(data, mediaType);

    /// <summary>The terminal success marker.</summary>
    public static GenerationChunk Completed(GenerationUsage? usage = null) => new(Final: true, Usage: usage);

    /// <summary>The terminal failure marker.</summary>
    public static GenerationChunk Failure(GenerationVerdict verdict, string? detail = null) =>
        new(Error: verdict, Detail: detail);
}
