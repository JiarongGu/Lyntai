namespace Lyntai;

/// <summary>Client-side throttling for the opt-in rate limiter (<c>AddRateLimit</c>). A token bucket per
/// scope: <see cref="PermitsPerSecond"/> is the sustained rate, <see cref="Burst"/> the bucket capacity
/// (how many can go at once after idle). Over the rate, a call waits up to <see cref="MaxWait"/>, then is
/// refused (a <see cref="Lyntai.Llm.LlmVerdict.RateLimited"/> reply). Read at runtime, so
/// <c>LYNTAI_RATELIMIT_*</c> env overrides applied after configuration take effect.</summary>
public sealed class RateLimitOptions
{
    /// <summary>Sustained global permits per second across all consumers. 0 (default) = no global limit.</summary>
    public double PermitsPerSecond { get; set; }

    /// <summary>Global bucket capacity — the largest burst allowed after an idle period. Min 1.</summary>
    public int Burst { get; set; } = 1;

    /// <summary>How long a call may wait for a permit before it's refused. Default 10s; set to
    /// <see cref="System.TimeSpan.Zero"/> to fail fast (refuse immediately when over the rate).</summary>
    public TimeSpan MaxWait { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Per-consumer rates, checked instead of the global one for a listed consumer (the more
    /// specific limit wins). A consumer absent from the map uses the global rate.</summary>
    public Dictionary<string, ConsumerRate> PerConsumer { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>A per-consumer rate (see <see cref="RateLimitOptions.PerConsumer"/>).</summary>
public sealed record ConsumerRate(double PermitsPerSecond, int Burst = 1);
