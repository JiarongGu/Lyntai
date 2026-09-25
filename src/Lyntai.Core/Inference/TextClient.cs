
namespace Lyntai.Inference;

/// <summary>Default <see cref="ITextClient"/>: the router over a fallback list.</summary>
public sealed class TextClient : ITextClient, IRoutedCandidates
{
    private readonly ITextRouter _router;
    private readonly LyntaiOptions _options;
    private readonly IReadOnlyList<ProviderCandidate>? _candidates;

    /// <summary>Route over <paramref name="candidates"/>, or over
    /// <see cref="LyntaiOptions.DefaultCandidates"/> when none are given.</summary>
    /// <param name="router">The routing engine, already over this client's provider set.</param>
    /// <param name="options">Platform options (timeouts, model defaults).</param>
    /// <param name="candidates">The fallback list, in order — what a NAMED client is composed with, so
    /// narrowing its backends narrows what it TRIES rather than leaving it pointed at a global list none of
    /// its own backends appear in. Snapshotted, because a composed client's list is settled by its wiring.
    /// <para><c>null</c> (the default) reads <see cref="LyntaiOptions.DefaultCandidates"/> at each call, so
    /// an override applied after composition still takes.</para></param>
    public TextClient(ITextRouter router, LyntaiOptions options,
        IReadOnlyList<ProviderCandidate>? candidates = null)
    {
        _router = router;
        _options = options;
        _candidates = candidates is null ? null : [.. candidates];
    }

    private IReadOnlyList<ProviderCandidate> Candidates => _candidates ?? _options.DefaultCandidates;

    IReadOnlyList<ProviderCandidate>? IRoutedCandidates.RoutedCandidates => Candidates;

    public Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default) =>
        _router.CompleteAsync(Candidates, req, ct);

    public IAsyncEnumerable<TextChunk> StreamAsync(TextRequest req, CancellationToken ct = default) =>
        _router.StreamAsync(Candidates, req, ct);

    public ValueTask<ProviderCapabilities?> GetCapabilitiesAsync(TextRequest req, CancellationToken ct = default) =>
        _router.GetCapabilitiesAsync(Candidates, req, ct);
}

/// <summary>A front door that can say which candidates its calls route over — read by the response cache, which
/// must key on a candidate's PINNED model (<see cref="Caching.CachingTextClient"/>). A decorator forwards its
/// inner client's answer; null means it cannot say.</summary>
internal interface IRoutedCandidates
{
    /// <summary>The candidates the next call routes over, or null when unknown.</summary>
    IReadOnlyList<ProviderCandidate>? RoutedCandidates { get; }
}
