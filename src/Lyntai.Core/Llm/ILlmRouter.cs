namespace Lyntai.Llm;

/// <summary>Routes a request across an ordered candidate list with fallback (design §6, amended
/// 2026-07-17): dedup candidates, try in order; Failed/Timeout advances; RateLimited/AuthFailed cool
/// the host and advance (a different candidate has a different quota/key); ContextWindowExceeded
/// advances with no host penalty; Refused surfaces with no fallback. Streaming never falls back after
/// the first content token; dead hosts are skipped for a cooldown window.</summary>
public interface ILlmRouter
{
    Task<LlmReply> CompleteAsync(IReadOnlyList<LlmCandidate> candidates, LlmRequest req, CancellationToken ct = default);

    IAsyncEnumerable<LlmChunk> StreamAsync(IReadOnlyList<LlmCandidate> candidates, LlmRequest req, CancellationToken ct = default);

    /// <summary>Whether native tool-calling is available for <paramref name="candidates"/> serving
    /// <paramref name="req"/> — true iff the first live (registered + available + not on cooldown)
    /// candidate is a tool-capable provider. Takes the request so it resolves the SAME effective model /
    /// cooldown key that <see cref="CompleteAsync"/> will, avoiding a probe-vs-serve mismatch. Default false.</summary>
    bool SupportsToolCalls(IReadOnlyList<LlmCandidate> candidates, LlmRequest req) => false;
}
