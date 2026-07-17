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

    /// <summary>Whether native tool-calling is available for <paramref name="req"/> under the configured
    /// default routing (the first live default candidate is a tool-capable provider). The
    /// <see cref="Agents.IToolLoop"/> reads this to choose the native path vs. its prompt-based fallback —
    /// without seeing candidate lists. Takes the request so the capability probe matches the model /
    /// cooldown key the completion will actually use.</summary>
    bool SupportsToolCalls(LlmRequest req) => false;
}
