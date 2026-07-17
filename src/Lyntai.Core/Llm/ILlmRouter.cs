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
}
