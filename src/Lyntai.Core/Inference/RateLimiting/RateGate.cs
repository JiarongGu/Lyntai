using Lyntai.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Lyntai.Inference.RateLimiting;

/// <summary>The one client-side rate check, beside <see cref="Budgeting.BudgetGate"/>: take a permit or refuse,
/// logging and counting the refusal the same way on every door — the text front door, the media router and the
/// generic router's governance.</summary>
internal static class RateGate
{
    /// <summary>The reason the text doors refuse with.</summary>
    internal const string Exceeded = "client-side rate limit exceeded";

    /// <summary><paramref name="reason"/> when no permit frees within the limiter's wait, else null. A cancelled
    /// wait propagates: a caller that gave up is not a throttled caller.</summary>
    internal static async ValueTask<string?> RefuseAsync(
        IRateLimiter limiter, string consumer, string reason, ILogger logger, CancellationToken ct)
    {
        if (await limiter.AcquireAsync(consumer, ct).ConfigureAwait(false)) return null;
        logger.LogInformation("{Reason} for consumer {Consumer}", reason, consumer);
        LyntaiDiagnostics.RecordRateLimitRefusal(consumer);
        return reason;
    }
}
