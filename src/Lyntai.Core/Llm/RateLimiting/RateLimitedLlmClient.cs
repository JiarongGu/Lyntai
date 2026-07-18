using System.Runtime.CompilerServices;
using Lyntai.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Llm.RateLimiting;

/// <summary>
/// Decorates the front door with client-side throttling: before each call it acquires a permit from the
/// <see cref="IRateLimiter"/>, waiting up to the configured max wait; if no permit frees in time the call
/// is refused (a <see cref="LlmVerdict.RateLimited"/> reply / an Error stream chunk) without hitting a
/// provider. Wired by <c>AddRateLimit()</c>. Sits inside the response cache, so a cached hit doesn't spend
/// a permit — only real provider calls are throttled.
/// </summary>
public sealed class RateLimitedLlmClient(
    ILlmClient inner, IRateLimiter limiter, ILogger<RateLimitedLlmClient>? logger = null) : ILlmClient
{
    private readonly ILogger _logger = logger ?? NullLogger<RateLimitedLlmClient>.Instance;

    public async Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default)
    {
        if (!await limiter.AcquireAsync(req.Consumer, ct).ConfigureAwait(false))
            return Throttled(req.Consumer);
        return await inner.CompleteAsync(req, ct).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<LlmChunk> StreamAsync(
        LlmRequest req, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!await limiter.AcquireAsync(req.Consumer, ct).ConfigureAwait(false))
        {
            yield return LlmChunk.Error(LlmVerdict.RateLimited, "client-side rate limit exceeded");
            yield break;
        }
        await foreach (var chunk in inner.StreamAsync(req, ct).ConfigureAwait(false))
            yield return chunk;
    }

    public bool SupportsToolCalls(LlmRequest req) => inner.SupportsToolCalls(req);

    private LlmReply Throttled(string consumer)
    {
        _logger.LogInformation("client-side rate limit exceeded for consumer {Consumer}", consumer);
        LyntaiDiagnostics.RecordRateLimitRefusal(consumer);
        return new LlmReply("", LlmVerdict.RateLimited, Detail: "client-side rate limit exceeded");
    }
}
