using Lyntai.Lifecycle;

namespace Lyntai.Generation;

/// <summary>One piece of a streamed generation, or its terminal marker. Modelled ON the LLM side's chunk
/// contract, but NOT identical to it: <c>LlmChunk</c> carries a required <c>Kind</c> discriminator, whereas a
/// chunk here is terminal when <see cref="Final"/> is true or <see cref="Error"/> is set. Code ported from LLM
/// streaming has to switch on that pair rather than on a kind.</summary>
/// <param name="Data">Media bytes, for a content chunk.</param>
/// <param name="MediaType">MIME type of the stream, on the first chunk at least.</param>
/// <param name="Error">Set on a terminal FAILURE chunk.</param>
/// <param name="Detail">The backend's words about the failure.</param>
/// <param name="Final">True on the terminal SUCCESS chunk.</param>
/// <param name="Usage">Cost/duration, where the backend reports it on completion.</param>
public sealed record GenerationChunk(
    byte[]? Data = null,
    string? MediaType = null,
    ProviderVerdict? Error = null,
    string? Detail = null,
    bool Final = false,
    GenerationUsage? Usage = null)
{
    /// <summary>A content chunk.</summary>
    public static GenerationChunk Content(byte[] data, string? mediaType = null) => new(data, mediaType);

    /// <summary>The terminal success marker.</summary>
    public static GenerationChunk Completed(GenerationUsage? usage = null) => new(Final: true, Usage: usage);

    /// <summary>The terminal failure marker.</summary>
    public static GenerationChunk Failure(ProviderVerdict verdict, string? detail = null) =>
        new(Error: verdict, Detail: detail);
}
