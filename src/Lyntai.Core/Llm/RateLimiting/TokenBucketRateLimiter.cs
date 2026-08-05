using System.Collections.Concurrent;

namespace Lyntai.Llm.RateLimiting;

/// <summary>
/// A process-local token-bucket <see cref="IRateLimiter"/> (the default for <c>AddRateLimit()</c>). One
/// bucket per scope — the global one, or a per-consumer one when configured (the more specific wins).
/// Buckets refill continuously at their rate up to their burst capacity; an acquire reserves a permit and,
/// when the bucket is empty, computes the wait until its permit frees. If that wait exceeds the configured
/// <see cref="RateLimitOptions.MaxWait"/> the reservation is refunded and the acquire is refused. The clock
/// is injectable so the reservation math is deterministic under test (the async wait aside).
/// <para>Options are read LIVE on every acquire — rate/burst/<c>MaxWait</c> retunes (env overrides, an
/// admin) apply to existing buckets on their next acquire; a bucket holds only its token state.</para>
/// </summary>
public sealed class TokenBucketRateLimiter : IRateLimiter
{
    private readonly RateLimitOptions _limits;
    private readonly Func<DateTimeOffset> _clock;
    // MUST match RateLimitOptions.PerConsumer's comparer (OrdinalIgnoreCase): the options map admits
    // "Chat" and "chat" as the same limit, so they must share ONE bucket — a case-sensitive map here
    // would hand each casing its own full rate (~2x overshoot).
    private readonly ConcurrentDictionary<string, Bucket> _consumerBuckets = new(StringComparer.OrdinalIgnoreCase);
    // lazy: created on the first acquire that finds a positive global rate, so a limit enabled AFTER
    // construction (env override / admin retune) still gets its bucket — nothing is frozen at ctor time
    private Bucket? _global;

    /// <summary>Throttle using the LLM front door's rate (<see cref="LyntaiOptions.RateLimit"/>).</summary>
    public TokenBucketRateLimiter(LyntaiOptions options, Func<DateTimeOffset>? clock = null)
        : this(options.RateLimit, clock) { }

    /// <summary>Throttle using a rate config the caller owns — how the generation domain gets its OWN rate
    /// instead of sharing the chat one (different vendors, often different accounts; one shared bucket would
    /// have a render starve the chat that asked for it). <see cref="LyntaiOptions.RateLimit"/> is a fixed
    /// instance, so both constructors read live: a retune applies to existing buckets on their next acquire.</summary>
    public TokenBucketRateLimiter(RateLimitOptions limits, Func<DateTimeOffset>? clock = null)
    {
        _limits = limits;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Whether the current options resolve to any real throttle — a positive global rate, or any
    /// per-consumer entry at all. False means nothing is configured to throttle anything, so every acquire
    /// clears immediately (a pure passthrough); the <c>AddRateLimit</c> wiring warns in that case. Note a
    /// per-consumer entry with a ZERO rate is not a passthrough — its bucket hands out the burst and then
    /// refuses forever, which is the configured intent — so it counts as an effective limit.
    /// Read live, so it honors <c>LYNTAI_RATELIMIT_*</c> env overrides applied after construction (as does
    /// every acquire).</summary>
    internal bool HasEffectiveLimit => _limits.PermitsPerSecond > 0 || _limits.PerConsumer.Count > 0;

    public async Task<bool> AcquireAsync(string consumer, CancellationToken ct = default)
    {
        var wait = TryReserve(consumer, _clock());
        if (wait is null) return false;                 // over the limit beyond MaxWait → refuse
        if (wait.Value > TimeSpan.Zero)
        {
            try { await Task.Delay(wait.Value, ct).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                // the caller bailed before its slot — hand the reserved permit back, else a burst of
                // cancelled waits would throttle legitimate callers for a slot no request ever used.
                // The cancellation then PROPAGATES (matching the router: caller cancel is not a rate
                // refusal — a cancelled caller must not receive a fabricated RateLimited reply or
                // pollute the refusal metric).
                if (Resolve(consumer) is { } scope) scope.Bucket.Refund(scope.Burst);
                throw;
            }
        }
        return true;
    }

    /// <summary>The wait a permit for <paramref name="consumer"/> needs at <paramref name="now"/>
    /// (<see cref="TimeSpan.Zero"/> = go now), or null to refuse. Time is a parameter, so the reservation
    /// state transition is fully deterministic for tests.</summary>
    internal TimeSpan? TryReserve(string consumer, DateTimeOffset now)
    {
        if (Resolve(consumer) is not { } scope) return TimeSpan.Zero; // no limit configured → unlimited
        return scope.Bucket.Reserve(now, scope.Rate, scope.Burst, _limits.MaxWait);
    }

    /// <summary>The bucket + its CURRENT rate/burst for <paramref name="consumer"/> (per-consumer wins over
    /// global), or null when nothing limits it. Rate/burst come from live options on every call — the
    /// bucket is a state holder only, so a retune applies to existing buckets immediately.</summary>
    private (Bucket Bucket, double Rate, int Burst)? Resolve(string consumer)
    {
        var rl = _limits;
        if (rl.PerConsumer.TryGetValue(consumer, out var rate))
        {
            var burst = Math.Max(1, rate.Burst);
            return (_consumerBuckets.GetOrAdd(consumer, _ => new Bucket(_clock(), burst)), rate.PermitsPerSecond, burst);
        }
        if (rl.PermitsPerSecond > 0)
        {
            var burst = Math.Max(1, rl.Burst);
            var bucket = _global ?? LazyInitializer.EnsureInitialized(ref _global, () => new Bucket(_clock(), burst));
            return (bucket, rl.PermitsPerSecond, burst);
        }
        return null; // no global limit configured → unlimited
    }

    private sealed class Bucket(DateTimeOffset start, int initialTokens)
    {
        private readonly object _gate = new();
        private double _tokens = initialTokens;
        private DateTimeOffset _last = start;

        // rate/burst are PARAMETERS (resolved from live options per acquire), not construction state —
        // the bucket owns only its token count and last-refill instant
        public TimeSpan? Reserve(DateTimeOffset now, double ratePerSecond, int burst, TimeSpan maxWait)
        {
            lock (_gate)
            {
                var elapsed = (now - _last).TotalSeconds;
                if (elapsed > 0)
                {
                    _tokens = Math.Min(burst, _tokens + elapsed * ratePerSecond);
                    _last = now;
                }
                _tokens -= 1;                            // reserve a permit (may go negative = queued)
                if (_tokens >= 0) return TimeSpan.Zero;
                // an explicit zero/negative rate never refills — refuse rather than divide by it
                // (TimeSpan.FromSeconds(Infinity) would throw); "burst then block" is the configured intent
                if (ratePerSecond <= 0) { _tokens += 1; return null; }
                var wait = TimeSpan.FromSeconds(-_tokens / ratePerSecond);
                if (wait > maxWait) { _tokens += 1; return null; } // too long → refund + refuse
                return wait;
            }
        }

        /// <summary>Return a previously-reserved permit (the caller cancelled before using it).</summary>
        public void Refund(int burst)
        {
            lock (_gate) _tokens = Math.Min(burst, _tokens + 1);
        }
    }
}
