using Lyntai.Inference;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Inference.RateLimiting;

/// <summary>
/// Decorates the front door with client-side throttling: before each call it acquires a permit from the
/// <see cref="IRateLimiter"/>, waiting up to the configured max wait; if no permit frees in time the call
/// is refused (a <see cref="ProviderVerdict.RateLimited"/> reply / an Error stream chunk) without hitting a
/// provider. Wired by <c>AddRateLimit()</c>. Sits inside the response cache, so a cached hit doesn't spend
/// a permit — only real provider calls are throttled.
/// </summary>
public sealed class RateLimitedTextClient(
    ITextClient inner, IRateLimiter limiter, ILogger<RateLimitedTextClient>? logger = null) : DelegatingTextClient(inner)
{
    private readonly ILogger _logger = logger ?? NullLogger<RateLimitedTextClient>.Instance;

    public override async Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default)
    {
        if (await RefuseAsync(req.Consumer, ct).ConfigureAwait(false) is { } reason)
            return new TextResponse("", ProviderVerdict.RateLimited, Detail: reason);
        return await Inner.CompleteAsync(req, ct).ConfigureAwait(false);
    }

    public override async IAsyncEnumerable<TextChunk> StreamAsync(
        TextRequest req, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (await RefuseAsync(req.Consumer, ct).ConfigureAwait(false) is { } reason)
        {
            yield return TextChunk.Error(ProviderVerdict.RateLimited, reason);
            yield break;
        }
        await foreach (var chunk in Inner.StreamAsync(req, ct).ConfigureAwait(false))
            yield return chunk;
    }

    // every door refuses through the one gate, which logs and counts — a hand-rolled refusal would miss
    // lyntai.ratelimit.refusals
    private ValueTask<string?> RefuseAsync(string consumer, CancellationToken ct) =>
        RateGate.RefuseAsync(limiter, consumer, RateGate.Exceeded, _logger, ct);
}
