
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

    /// <summary>Whether native tool-calling is available for <paramref name="candidates"/> serving
    /// <paramref name="req"/> — true iff the first live (registered + available + not on cooldown)
    /// candidate is a tool-capable provider. Takes the request so it resolves the same CONFIGURED
    /// effective model / cooldown key that <see cref="CompleteAsync"/> will. Caveat: being a sync probe,
    /// it does NOT read a live <c>IModelRoutingStore</c> route — it decides over the candidates it is given,
    /// so under a live route the completion can be served by a different candidate than the one probed.
    /// Default false.</summary>
    bool SupportsToolCalls(IReadOnlyList<ProviderCandidate> candidates, TextRequest req) => false;

    /// <summary>Whether the first live candidate's STREAM delivers native tool calls. Same selection rule
    /// and same caveats as <see cref="SupportsToolCalls"/>; default false.</summary>
    bool SupportsStreamingToolCalls(IReadOnlyList<ProviderCandidate> candidates, TextRequest req) => false;
}
