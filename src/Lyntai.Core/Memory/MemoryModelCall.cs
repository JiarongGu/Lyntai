using Lyntai.Inference;

namespace Lyntai.Memory;

/// <summary>The one request shape both model-backed memory policies send — annotation on every write,
/// verification on every recall.</summary>
internal static class MemoryModelCall
{
    /// <summary>Ask one memory-side question: a system instruction and a user message, tagged
    /// <see cref="ProviderConsumers.Memory"/> so memory's spend is separable in the ledger, with reasoning
    /// suppressed because the answer is a short structured label in the latency path of a write or a recall — a
    /// thinking model measured ~25 s per judgement against ~1.5 s for one answering directly
    /// (<c>docs/DECISIONS.md</c> D59). Advisory: a backend that cannot express it ignores it, and both parsers
    /// tolerate a reply that reasons anyway.</summary>
    /// <param name="clients">Resolves the client.</param>
    /// <param name="clientName">The named client, or null for the default one.</param>
    /// <param name="model">The requested model, or null for the backend's own.</param>
    /// <param name="system">The instruction.</param>
    /// <param name="user">The material to judge.</param>
    /// <param name="ct">Cancellation.</param>
    internal static Task<TextResponse> AskAsync(ITextClientFactory clients, string? clientName, string? model,
        string system, string user, CancellationToken ct)
    {
        var client = clientName is { } name ? clients.Get(name) : clients.Get();
        return client.CompleteAsync(new TextRequest
        {
            Messages = [new TextMessage("system", system), new TextMessage("user", user)],
            Model = model,
            Consumer = ProviderConsumers.Memory,
            Reasoning = TextReasoning.Suppress,
        }, ct);
    }
}
