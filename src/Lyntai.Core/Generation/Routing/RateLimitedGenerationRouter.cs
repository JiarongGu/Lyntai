using Lyntai.Diagnostics;
using Lyntai.Llm.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Generation.Routing;

/// <summary>
/// Wraps generation routing in client-side throttling: before a render (or a submission) it acquires a permit,
/// waiting up to the configured max wait; if none frees in time the call is refused with
/// <see cref="GenerationVerdict.RateLimited"/> rather than hitting a backend. Wired by
/// <c>AddGenerationRateLimit()</c>.
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
public sealed class RateLimitedGenerationRouter(
    IGenerationRouter inner,
    IRateLimiter limiter,
    ILogger<RateLimitedGenerationRouter>? logger = null) : IGenerationRouter
{
    private readonly ILogger _logger = logger ?? NullLogger<RateLimitedGenerationRouter>.Instance;
    private const string Reason = "client-side generation rate limit exceeded";

    /// <inheritdoc/>
    public async Task<GenerationResult> GenerateAsync(
        IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request, CancellationToken ct = default)
    {
        if (!await limiter.AcquireAsync(request.Consumer, ct).ConfigureAwait(false))
        {
            Throttled(request.Consumer);
            return GenerationResult.Failure(GenerationVerdict.RateLimited, Reason);
        }
        return await inner.GenerateAsync(candidates, request, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<GenerationSubmission> SubmitAsync(
        IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request, CancellationToken ct = default)
    {
        if (!await limiter.AcquireAsync(request.Consumer, ct).ConfigureAwait(false))
        {
            Throttled(request.Consumer);
            return new GenerationSubmission("",
                new GenerationOperation("", GenerationOperationStatus.Failed, Detail: Reason));
        }
        return await inner.SubmitAsync(candidates, request, ct).ConfigureAwait(false);
    }

    private void Throttled(string consumer)
    {
        _logger.LogInformation("{Reason} for consumer {Consumer}", Reason, consumer);
        LyntaiDiagnostics.RecordRateLimitRefusal(consumer);
    }
}
