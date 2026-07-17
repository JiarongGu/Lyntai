namespace Lyntai.Llm.Routing;

/// <summary>Default <see cref="ILlmClient"/>: the router over the configured default candidates.</summary>
public sealed class LlmClient(ILlmRouter router, LyntaiOptions options) : ILlmClient
{
    public Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default) =>
        router.CompleteAsync(options.DefaultCandidates, req, ct);

    public IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, CancellationToken ct = default) =>
        router.StreamAsync(options.DefaultCandidates, req, ct);

    public bool SupportsToolCalls(LlmRequest req) => router.SupportsToolCalls(options.DefaultCandidates, req);
}
