using System.Runtime.CompilerServices;
using Lyntai.Diagnostics;
using Lyntai.Inference.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Inference;

/// <summary>
/// Wraps generation routing in client-side throttling: before a render (or a submission) it acquires a permit,
/// waiting up to the configured max wait; if none frees in time the call is refused with
/// <see cref="ProviderVerdict.RateLimited"/> rather than hitting a backend. Wired by
/// <c>AddMediaRateLimit()</c>.
///
/// <para>It uses its OWN limiter with its own rate — NOT the LLM front door's. A render and a chat turn hit
/// different vendors' limits (often different accounts), and one shared bucket would have an image render
/// starve the chat that requested it. The machinery is the same <see cref="IRateLimiter"/>
/// (<see cref="TokenBucketRateLimiter"/> by default); only the configuration is separate.</para>
///
/// <para>A cancelled wait propagates rather than surfacing as a fabricated rate-limit verdict — a caller that
/// gave up is not a throttled caller, and the token bucket refunds the permit it had reserved.</para>
/// </summary>
/// <param name="inner">The router being throttled.</param>
/// <param name="limiter">The generation limiter (its own instance and rate — see above).</param>
/// <param name="logger">Optional; one line per refusal.</param>
public sealed class RateLimitedMediaRouter(
    IMediaRouter inner,
    IRateLimiter limiter,
    ILogger<RateLimitedMediaRouter>? logger = null) : IMediaRouter
{
    private readonly ILogger _logger = logger ?? NullLogger<RateLimitedMediaRouter>.Instance;
    private const string Reason = "client-side generation rate limit exceeded";

    /// <inheritdoc/>
    public async Task<MediaResponse> GenerateAsync(
        IReadOnlyList<ProviderCandidate> candidates, MediaRequest request, CancellationToken ct = default)
    {
        if (!await limiter.AcquireAsync(request.Consumer, ct).ConfigureAwait(false))
        {
            Throttled(request.Consumer);
            return MediaResponse.Failure(ProviderVerdict.RateLimited, Reason);
        }
        return await inner.GenerateAsync(candidates, request, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<MediaSubmission> SubmitAsync(
        IReadOnlyList<ProviderCandidate> candidates, MediaRequest request, CancellationToken ct = default)
    {
        if (!await limiter.AcquireAsync(request.Consumer, ct).ConfigureAwait(false))
        {
            Throttled(request.Consumer);
            return new MediaSubmission("",   // the inline door's verdict, so both doors refuse alike
                new QueuedOperation("", QueuedOperationStatus.Failed, Detail: Reason)
                {
                    Verdict = ProviderVerdict.RateLimited,
                });
        }
        return await inner.SubmitAsync(candidates, request, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>Throttled on the SAME terms as the other two doors — see
    /// <c>BudgetedMediaRouter.StreamAsync</c> for why a decorator may not pass one door straight
    /// through. ONE permit per stream, taken before the first chunk: a stream is one call to one backend, so
    /// charging it per chunk would let the length of the media decide the rate rather than the rate deciding
    /// it.</remarks>
    public async IAsyncEnumerable<MediaChunk> StreamAsync(
        IReadOnlyList<ProviderCandidate> candidates, MediaRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!await limiter.AcquireAsync(request.Consumer, ct).ConfigureAwait(false))
        {
            Throttled(request.Consumer);
            yield return MediaChunk.Failure(ProviderVerdict.RateLimited, Reason);
            yield break;
        }

        await foreach (var chunk in inner.StreamAsync(candidates, request, ct).ConfigureAwait(false))
            yield return chunk;
    }

    private void Throttled(string consumer)
    {
        _logger.LogInformation("{Reason} for consumer {Consumer}", Reason, consumer);
        LyntaiDiagnostics.RecordRateLimitRefusal(consumer);
    }
}
