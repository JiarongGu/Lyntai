namespace Lyntai.Llm.RateLimiting;

/// <summary>Gates the rate of front-door calls. The built-in <see cref="TokenBucketRateLimiter"/> is what
/// <c>AddRateLimit()</c> registers; register your own <see cref="IRateLimiter"/> first (e.g. a distributed
/// limiter shared across processes) to override it.</summary>
public interface IRateLimiter
{
    /// <summary>Acquire permission to proceed for <paramref name="consumer"/>. Returns true when cleared
    /// (possibly after waiting for a permit to free up), or false if the limit can't be met within the
    /// configured max wait — the caller should then refuse the call. A cancelled wait PROPAGATES the
    /// <see cref="OperationCanceledException"/> (caller cancel is not a rate refusal — it must not surface
    /// as a fabricated RateLimited outcome).</summary>
    Task<bool> AcquireAsync(string consumer, CancellationToken ct = default);
}
