namespace Lyntai.Tests.Fakes;

/// <summary>A <see cref="TimeProvider"/> that only moves when a test says so: timers fire inside
/// <see cref="Advance"/> and nowhere else, so a wait built on <c>Task.Delay(delay, provider, ct)</c> is held until
/// the test lets it go and costs no real time. <see cref="Requested"/> records every wait asked for.</summary>
public sealed class ManualTimeProvider(DateTimeOffset? start = null) : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = start ?? new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The due time of every timer ever created, in order.</summary>
    public List<TimeSpan> Requested { get; } = [];

    /// <summary>Timers created and not yet fired or disposed.</summary>
    public int Pending { get { lock (_gate) return _timers.Count(t => t.DueAt is not null); } }

    public override DateTimeOffset GetUtcNow() { lock (_gate) return _now; }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_gate)
        {
            _timers.Add(timer);
            Requested.Add(dueTime);
        }
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>The wait of the earliest pending timer, or null when none is pending.</summary>
    public TimeSpan? NextDue()
    {
        lock (_gate)
        {
            var due = _timers.Where(t => t.DueAt is not null).Select(t => t.DueAt!.Value).DefaultIfEmpty().Min();
            return due == default ? null : due - _now;
        }
    }

    /// <summary>Move the clock, firing each timer that falls due on the way, earliest first.</summary>
    public void Advance(TimeSpan by)
    {
        var target = GetUtcNow() + by;
        while (true)
        {
            ManualTimer? due;
            lock (_gate)
            {
                due = _timers.Where(t => t.DueAt is { } at && at <= target).OrderBy(t => t.DueAt).FirstOrDefault();
                if (due is null) { _now = target; return; }
                _now = due.DueAt!.Value > _now ? due.DueAt.Value : _now;
            }
            due.Fire();
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period = Timeout.InfiniteTimeSpan;
        public DateTimeOffset? DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
                _period = period;
            }
            return true;
        }

        public void Fire()
        {
            lock (owner._gate)
                DueAt = _period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero ? null : owner._now + _period;
            callback(state);
        }

        public void Dispose()
        {
            lock (owner._gate) { DueAt = null; owner._timers.Remove(this); }
        }

        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
