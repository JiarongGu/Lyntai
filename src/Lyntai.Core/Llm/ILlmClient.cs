namespace Lyntai.Llm;

/// <summary>
/// The library's front door. To a consuming application, Lyntai behaves like ONE LLM provider:
/// this interface deliberately mirrors <see cref="ILlmProvider"/>'s shape (complete/stream over an
/// <see cref="LlmRequest"/>), while candidate order, fallback, dead-host cooldown, and per-consumer
/// model routing all happen behind it (the configured <c>DefaultCandidates</c>). Prefer injecting
/// this over <see cref="ILlmRouter"/> unless a call site genuinely needs its own candidate list.
/// </summary>
public interface ILlmClient
{
    Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default);

    IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, CancellationToken ct = default);
}
