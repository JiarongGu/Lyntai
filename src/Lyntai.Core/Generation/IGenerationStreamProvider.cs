namespace Lyntai.Generation;

/// <summary>
/// OPTIONAL capability of an <see cref="IGenerationProvider"/> that emits media AS IT IS PRODUCED — the shape
/// text-to-speech backends are built around (playback starts in well under a second, long before generation
/// finishes). Buffering that into a single result would throw away the only property that makes it useful.
/// </summary>
/// <remarks><b>Reachable through the platform since 3.0.</b>
/// <see cref="Lyntai.Generation.Routing.IGenerationRouter.StreamAsync"/> is the third door, so a backend
/// advertising <see cref="GenerationDelivery.Stream"/> is selected, fallen over, governed and throttled on the
/// same terms as an inline one. Until then the capability pre-filter was only ever asked about
/// <see cref="GenerationDelivery.Inline"/> and <see cref="GenerationDelivery.Job"/>, and this seam could only
/// be driven by hand — which is how it stayed unexercised long enough to be about to freeze that way.
/// <para><b>Two of the three rules below are the router's, not a backend's.</b> A backend need not be careful
/// about them: fallback stops at the first chunk carrying real data, and the router closes any stream that
/// ends without a terminal chunk. What a backend owes the platform is chunks in order and, ideally, its own
/// terminal marker.</para>
/// <para><b>What is still INFERRED, stated precisely rather than as a blanket caveat.</b> The chunk SHAPE —
/// data-then-terminal, with the terminal carrying usage — is modelled on the LLM streaming contract, not
/// measured against a TTS wire format, because no such backend exists here yet (<c>TASKS.md</c> GEN6 needs a
/// vendor and a key). The router's handling of that shape is measured, by
/// <c>GenerationRouterStreamTests</c>. So the risk that remains is not "does this work" but "is this the
/// decomposition a real TTS stream wants" — and reshaping it is a major-version act, as it is for every
/// package since 3.0 withdrew the generation carve-out.</para></remarks>
public interface IGenerationStreamProvider
{
    /// <summary>Stream the generation. Yields data chunks in order, then exactly ONE terminal chunk:
    /// <see cref="GenerationChunk.Final"/> when the stream completed, or a chunk carrying
    /// <see cref="GenerationChunk.Error"/> when it did not.</summary>
    IAsyncEnumerable<GenerationChunk> StreamAsync(GenerationRequest request, CancellationToken ct = default);
}

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
