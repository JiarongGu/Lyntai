namespace Lyntai.Inference;

/// <summary>A backend built from a FUNCTION rather than a class — the general shape of bridging something
/// that can already answer into this library: "here is a function that answers; route it like any other
/// backend". It needs no third-party type, so it belongs in Core, where a dependency-bearing adapter never could
/// (<c>docs/DECISIONS.md</c> D146, D147).
///
/// <para><b>Bridging any client is a lambda.</b> Whatever the SDK — an <c>IChatClient</c>, a vendor's own
/// client, an in-house HTTP service — the consumer owns the mapping they actually need and the library owns
/// routing, fallback, cooldown, admission and the ops layer around it.</para>
///
/// <para><b>Nothing is inferred.</b> It declares exactly the operations it was given a function for, so a
/// bridge with no stream delegate reports no <see cref="ProviderOperation.Stream"/> and a router never asks it
/// to stream — the same rule every other backend follows. A caller wanting tool calls, embeddings or scores
/// declares them the same way, by supplying the delegate and the capability that matches.</para></summary>
internal sealed class BridgeProvider(
    string id,
    ProviderCapabilities capabilities,
    Func<TextRequest, CancellationToken, Task<TextResponse>> complete,
    Func<TextRequest, CancellationToken, IAsyncEnumerable<TextChunk>>? stream) : IModelProvider
{
    public string Id => id;

    public ProviderCapabilities Capabilities => capabilities;

    public Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default) =>
        complete(req, ct);

    /// <summary>With no stream delegate, answers <see cref="ProviderVerdict.Unsupported"/> — the backstop for a
    /// direct caller; a router never reaches it, since the declared capabilities carry no Stream.</summary>
    public IAsyncEnumerable<TextChunk> StreamAsync(TextRequest req, CancellationToken ct = default) =>
        stream is null
            ? ProviderDefaults.One(TextChunk.Error(ProviderVerdict.Unsupported,
                $"{id} does not serve StreamAsync — no stream delegate was supplied to AddBridgeProvider."))
            : stream(req, ct);
}
