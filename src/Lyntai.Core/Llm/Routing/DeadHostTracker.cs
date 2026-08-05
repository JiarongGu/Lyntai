using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Llm.Routing;

/// <summary>
/// Dead-host cooldown (a FIXED cooldown after N consecutive failures, not exponential backoff): after
/// <see cref="_threshold"/> consecutive failures a key (provider/host) is dead for
/// <see cref="_cooldown"/>; any success resets. One log line per state change.
/// The clock is injected so tests are deterministic — no DateTime.Now in logic.
/// <para>State is allocated ONLY by <see cref="RecordFailure"/>/<see cref="MarkDead"/> — a key that only
/// ever succeeds never gets an entry, and <see cref="RecordSuccess"/> drops the one it had. What is retained
/// for the process lifetime is a key that failed and never recovered: considered and accepted, because an
/// entry is two fields and pruning on a clock would need the background timer this type deliberately does
/// without. Worth knowing because the key space is unbounded once cooldown keys on a CONFIGURATION
/// (<c>ProviderKey</c> changes with every credential rotation) rather than on a provider id.</para>
/// </summary>
public sealed class DeadHostTracker(
    int threshold = 3,
    TimeSpan? cooldown = null,
    Func<DateTimeOffset>? clock = null,
    ILogger<DeadHostTracker>? logger = null)
{
    private readonly int _threshold = threshold > 0 ? threshold : 3;
    private readonly TimeSpan _cooldown = cooldown ?? TimeSpan.FromSeconds(30);
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly ILogger _logger = logger ?? NullLogger<DeadHostTracker>.Instance;

    private readonly Dictionary<string, State> _states = [];
    private readonly Lock _lock = new();

    private sealed class State
    {
        public int ConsecutiveFailures;
        public DateTimeOffset? DeadUntil;
    }

    public bool IsDead(string key)
    {
        lock (_lock)
        {
            if (!_states.TryGetValue(key, out var s) || s.DeadUntil is null) return false;
            if (_clock() < s.DeadUntil.Value) return true;
            // cooldown expired — give the host another chance (failures keep counting from the threshold,
            // so a single new failure re-kills it rather than requiring a full fresh run of N)
            s.DeadUntil = null;
            s.ConsecutiveFailures = _threshold - 1;
            _logger.LogInformation("dead-host cooldown expired for {Key}; back in rotation", key);
            return false;
        }
    }

    public void RecordFailure(string key)
    {
        lock (_lock)
        {
            if (!_states.TryGetValue(key, out var s)) _states[key] = s = new State();
            s.ConsecutiveFailures++;
            if (s.DeadUntil is null && s.ConsecutiveFailures >= _threshold)
            {
                s.DeadUntil = _clock() + _cooldown;
                _logger.LogWarning("{Key} marked dead after {Fails} consecutive failures (cooldown {Cooldown})",
                    key, s.ConsecutiveFailures, _cooldown);
            }
        }
    }

    /// <summary>Immediate cooldown regardless of the failure count — for signals where the host
    /// explicitly told us to back off (rate limits): re-asking within the window is always wrong.</summary>
    public void MarkDead(string key)
    {
        lock (_lock)
        {
            if (!_states.TryGetValue(key, out var s)) _states[key] = s = new State();
            s.ConsecutiveFailures = Math.Max(s.ConsecutiveFailures, _threshold);
            if (s.DeadUntil is null)
            {
                s.DeadUntil = _clock() + _cooldown;
                _logger.LogWarning("{Key} marked dead immediately (backoff signal); cooldown {Cooldown}", key, _cooldown);
            }
        }
    }

    public void RecordSuccess(string key)
    {
        lock (_lock)
        {
            if (!_states.TryGetValue(key, out var s)) return;
            var wasDead = s.DeadUntil is not null;
            // remove rather than zero: a missing entry and a zeroed one are read identically everywhere
            // (IsDead calls both alive; RecordFailure/MarkDead build a fresh State), so dropping it keeps a
            // long-lived process from retaining one entry per configuration that ever recovered.
            _states.Remove(key);
            if (wasDead) _logger.LogInformation("{Key} recovered; dead-host state cleared", key);
        }
    }
}
