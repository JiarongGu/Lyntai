namespace Lyntai.Llm.Routing;

/// <summary>Default <see cref="ILlmClient"/>: the router over a fallback list.</summary>
public sealed class LlmClient : ILlmClient
{
    private readonly ILlmRouter _router;
    private readonly LyntaiOptions _options;
    private readonly IReadOnlyList<LlmCandidate>? _candidates;

    /// <summary>Route over <paramref name="candidates"/>, or over
    /// <see cref="LyntaiOptions.DefaultCandidates"/> when none are given.</summary>
    /// <param name="router">The routing engine, already over this client's provider set.</param>
    /// <param name="options">Platform options (timeouts, model defaults).</param>
    /// <param name="candidates">The fallback list, in order — what a NAMED client is composed with, so
    /// narrowing its backends narrows what it TRIES rather than leaving it pointed at a global list none of
    /// its own backends appear in. Snapshotted, because a composed client's list is settled by its wiring.
    /// <para><c>null</c> (the default) reads <see cref="LyntaiOptions.DefaultCandidates"/> at each call, so
    /// an override applied after composition still takes.</para></param>
    public LlmClient(ILlmRouter router, LyntaiOptions options,
        IReadOnlyList<LlmCandidate>? candidates = null)
    {
        _router = router;
        _options = options;
        _candidates = candidates is null ? null : [.. candidates];
    }

    private IReadOnlyList<LlmCandidate> Candidates => _candidates ?? _options.DefaultCandidates;

    public Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default) =>
        _router.CompleteAsync(Candidates, req, ct);

    public IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, CancellationToken ct = default) =>
        _router.StreamAsync(Candidates, req, ct);

    public bool SupportsToolCalls(LlmRequest req) => _router.SupportsToolCalls(Candidates, req);

    public bool SupportsStreamingToolCalls(LlmRequest req) =>
        _router.SupportsStreamingToolCalls(Candidates, req);
}
