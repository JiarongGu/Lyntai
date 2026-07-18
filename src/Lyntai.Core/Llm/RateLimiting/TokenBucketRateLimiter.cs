using System.Collections.Concurrent;

namespace Lyntai.Llm.RateLimiting;

/// <summary>
/// A process-local token-bucket <see cref="IRateLimiter"/> (the default for <c>AddRateLimit()</c>). One
/// bucket per scope — the global one, or a per-consumer one when configured (the more specific wins).
/// Buckets refill continuously at their rate up to their burst capacity; an acquire reserves a permit and,
/// when the bucket is empty, computes the wait until its permit frees. If that wait exceeds the configured
/// <see cref="RateLimitOptions.MaxWait"/> the reservation is refunded and the acquire is refused. The clock
/// is injectable so the reservation math is deterministic under test (the async wait aside).
/// </summary>
public sealed class TokenBucketRateLimiter : IRateLimiter
{
    private readonly LyntaiOptions _options;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ConcurrentDictionary<string, Bucket> _consumerBuckets = new();
    private readonly Bucket? _global;

    public TokenBucketRateLimiter(LyntaiOptions options, Func<DateTimeOffset>? clock = null)
    {
        _options = options;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        if (options.RateLimit.PermitsPerSecond > 0)
            _global = new Bucket(options.RateLimit.PermitsPerSecond, Math.Max(1, options.RateLimit.Burst), _clock());
    }

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
                // cancelled waits would throttle legitimate callers for a slot no request ever used
                BucketFor(consumer)?.Refund();
                return false;
            }
        }
        return true;
    }

    /// <summary>The wait a permit for <paramref name="consumer"/> needs at <paramref name="now"/>
    /// (<see cref="TimeSpan.Zero"/> = go now), or null to refuse. Time is a parameter, so the reservation
    /// state transition is fully deterministic for tests.</summary>
    internal TimeSpan? TryReserve(string consumer, DateTimeOffset now)
    {
        var bucket = BucketFor(consumer);
        return bucket is null ? TimeSpan.Zero : bucket.Reserve(now, _options.RateLimit.MaxWait);
    }

    private Bucket? BucketFor(string consumer)
    {
        if (_options.RateLimit.PerConsumer.TryGetValue(consumer, out var rate))
            return _consumerBuckets.GetOrAdd(consumer, _ => new Bucket(rate.PermitsPerSecond, Math.Max(1, rate.Burst), _clock()));
        return _global; // null when no global limit is configured → unlimited
    }

    private sealed class Bucket(double ratePerSecond, int burst, DateTimeOffset start)
    {
        private readonly object _gate = new();
        private double _tokens = burst;
        private DateTimeOffset _last = start;

        public TimeSpan? Reserve(DateTimeOffset now, TimeSpan maxWait)
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
                var wait = TimeSpan.FromSeconds(-_tokens / ratePerSecond);
                if (wait > maxWait) { _tokens += 1; return null; } // too long → refund + refuse
                return wait;
            }
        }

        /// <summary>Return a previously-reserved permit (the caller cancelled before using it).</summary>
        public void Refund()
        {
            lock (_gate) _tokens = Math.Min(burst, _tokens + 1);
        }
    }
}
