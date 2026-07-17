namespace Lyntai.Llm;

/// <summary>One LLM backend (CLI spawn, HTTP endpoint, MEAI bridge, …). Implementations are
/// registered into DI as an <see cref="IEnumerable{ILlmProvider}"/> keyed by <see cref="Id"/>;
/// the router selects among them by candidate id — never an if/else over provider kinds.</summary>
public interface ILlmProvider
{
    /// <summary>Stable id the router matches candidates against ("claude-cli", "openai", "ollama", …).</summary>
    string Id { get; }

    /// <summary>Cheap availability probe (binary on PATH, endpoint configured, …).</summary>
    bool IsAvailable { get; }

    Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default);

    IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, CancellationToken ct = default);
}
