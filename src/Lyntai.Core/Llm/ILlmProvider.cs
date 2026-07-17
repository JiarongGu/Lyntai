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

    /// <summary>Whether this provider supports native (structured) tool-calling — i.e. it sends
    /// <see cref="LlmRequest.Tools"/> to the model and surfaces the model's calls on
    /// <see cref="LlmReply.ToolCalls"/>. Default false; the <see cref="Agents.IToolLoop"/> uses its
    /// prompt-based fallback for providers that don't. Coarse (provider-level, not per-model): a model
    /// that ignores tools just answers in prose, which the loop treats as a final answer.</summary>
    bool SupportsToolCalls => false;

    Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default);

    IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, CancellationToken ct = default);
}
