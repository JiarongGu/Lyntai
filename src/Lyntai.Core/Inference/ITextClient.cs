namespace Lyntai.Inference;

/// <summary>
/// The library's front door. To a consuming application, Lyntai behaves like ONE LLM provider:
/// this interface deliberately mirrors <see cref="IModelProvider"/>'s shape (complete/stream over an
/// <see cref="TextRequest"/>), while candidate order, fallback, dead-host cooldown, and per-consumer
/// model routing all happen behind it (the configured <c>DefaultCandidates</c>). Prefer injecting
/// this over <see cref="ITextRouter"/> unless a call site genuinely needs its own candidate list.
/// </summary>
public interface ITextClient
{
    Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default);

    IAsyncEnumerable<TextChunk> StreamAsync(TextRequest req, CancellationToken ct = default);

    /// <summary>The capabilities of the backend that would serve <paramref name="req"/> now — the first live
    /// candidate of the route the call itself would take, a live <c>IModelRoutingStore</c> route included (see
    /// <see cref="ITextRouter.GetCapabilitiesAsync"/>). <see cref="Agents.IToolLoop"/> asks it once per run to
    /// choose between native tool calls (<see cref="ProviderCapabilities.SupportsToolCalls"/>, and
    /// <see cref="ProviderCapabilities.SupportsStreamingToolCalls"/> for its streaming half) and its prompt
    /// protocol.
    /// <para>Null means UNKNOWN — no live candidate, or a client that cannot say — and a caller must read it as
    /// the safe answer: no native tool calls. That is the default body.</para></summary>
    ValueTask<ProviderCapabilities?> GetCapabilitiesAsync(TextRequest req, CancellationToken ct = default) =>
        ValueTask.FromResult<ProviderCapabilities?>(null);
}
