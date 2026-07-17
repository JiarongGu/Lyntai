namespace Lyntai.Llm;

/// <summary>Routes a request across an ordered candidate list with fallback (design §6):
/// dedup candidates, try in order, Failed/Timeout advances, RateLimited circuit-breaks,
/// Refused surfaces; streaming never falls back after the first content token; dead hosts
/// are skipped for a cooldown window.</summary>
public interface ILlmRouter
{
    Task<LlmReply> CompleteAsync(IReadOnlyList<LlmCandidate> candidates, LlmRequest req, CancellationToken ct = default);

    IAsyncEnumerable<LlmChunk> StreamAsync(IReadOnlyList<LlmCandidate> candidates, LlmRequest req, CancellationToken ct = default);
}
