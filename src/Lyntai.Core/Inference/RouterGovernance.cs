using Lyntai.Inference.Budgeting;
using Lyntai.Inference.RateLimiting;

namespace Lyntai.Inference;

/// <summary>The one wallet, handed to a <see cref="ProviderRouter{TRequest,TResponse}"/>: budget caps,
/// the spend ledger and the client-side rate limiter, applied to any request that can be ATTRIBUTED
/// (<see cref="IConsumerTagged"/>) before a backend spends anything (<c>docs/DECISIONS.md</c> <b>D163</b>).
///
/// <para><b><see cref="IProviderRouterFactory"/> builds this for you</b> from whatever the container holds —
/// a deployment that never called <c>AddUsageBudget()</c> or <c>AddRateLimit()</c> has no tracker and no
/// limiter, and its routers behave exactly as before. The type is public for a caller composing a router by
/// hand who wants the same governance the factory-built ones get.</para></summary>
/// <param name="options">Where the caps live (<see cref="LyntaiOptions.Budget"/>).</param>
/// <param name="tracker">The shared spend ledger — the same one the text and media doors record into, so
/// "what has this app spent" stays one number. Null records nothing and caps nothing.</param>
/// <param name="limiter">Client-side throttling per consumer. Null throttles nothing.</param>
public sealed class RouterGovernance(
    LyntaiOptions options,
    IUsageTracker? tracker = null,
    IRateLimiter? limiter = null)
{
    /// <summary>Where the caps live.</summary>
    public LyntaiOptions Options { get; } = options;

    /// <summary>The shared spend ledger, or null to record and cap nothing.</summary>
    public IUsageTracker? Tracker { get; } = tracker;

    /// <summary>Client-side throttling, or null to throttle nothing.</summary>
    public IRateLimiter? Limiter { get; } = limiter;
}
