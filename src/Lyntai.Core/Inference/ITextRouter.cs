
namespace Lyntai.Inference;

/// <summary>Routes a request across an ordered candidate list with fallback (design §6, amended
/// 2026-07-17): dedup candidates, try in order; Failed/Timeout advances; RateLimited/AuthFailed cool
/// the host and advance (a different candidate has a different quota/key); ContextWindowExceeded and
/// NotConfigured advance with no host penalty; Refused surfaces with no fallback, and so does
/// Unsupported — a capability/transport gap, surfaced with no host penalty since another candidate has
/// the same limitation, but kept a distinct verdict so telemetry doesn't read it as a policy refusal.
/// Streaming never falls back after the first content token; dead hosts are skipped for a cooldown
/// window.</summary>
public interface ITextRouter
{
    Task<TextResponse> CompleteAsync(IReadOnlyList<ProviderCandidate> candidates, TextRequest req, CancellationToken ct = default);

    IAsyncEnumerable<TextChunk> StreamAsync(IReadOnlyList<ProviderCandidate> candidates, TextRequest req, CancellationToken ct = default);

    /// <summary>The capabilities of the backend that would serve <paramref name="req"/> over
    /// <paramref name="candidates"/> now: the first live (registered + available + not on cooldown) candidate,
    /// selected exactly as <see cref="CompleteAsync"/> selects it — the same effective model and cooldown key,
    /// and the consumer's live <c>IModelRoutingStore</c> route in place of <paramref name="candidates"/> when one
    /// is set. A fallback the call would never reach does not change the answer.
    /// <para>Null means UNKNOWN (no live candidate); read it as no native tool calls. A router that cannot say
    /// returns null.</para></summary>
    ValueTask<ProviderCapabilities?> GetCapabilitiesAsync(IReadOnlyList<ProviderCandidate> candidates, TextRequest req,
        CancellationToken ct = default);
}
