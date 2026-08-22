namespace Lyntai.Llm.Routing;

/// <summary>Default <see cref="ILlmClient"/>: the router over a fallback list.</summary>
public sealed class LlmClient : ILlmClient
{
    private readonly ILlmRouter _router;
    private readonly LyntaiOptions _options;
    private readonly IReadOnlyList<LlmCandidate>? _candidates;

    /// <summary>Route over <see cref="LyntaiOptions.DefaultCandidates"/>, read at each call so an override
    /// applied after composition still takes.</summary>
    public LlmClient(ILlmRouter router, LyntaiOptions options)
    {
        _router = router;
        _options = options;
    }

    /// <summary>Route over <paramref name="candidates"/> instead of the configured defaults — what a NAMED
    /// client is composed with, so narrowing its backends narrows what it tries rather than leaving it
    /// pointed at a global list none of its own backends appear in.</summary>
    /// <param name="router">The routing engine, already over this client's provider set.</param>
    /// <param name="options">Platform options (timeouts, model defaults); its candidate list is unused here.</param>
    /// <param name="candidates">The fallback list, in order. Snapshotted: a composed client's list is
    /// settled by its wiring, unlike the mutable global one.</param>
    public LlmClient(ILlmRouter router, LyntaiOptions options, IReadOnlyList<LlmCandidate> candidates)
        : this(router, options)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        _candidates = [.. candidates];
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
