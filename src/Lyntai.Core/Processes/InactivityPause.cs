namespace Lyntai.Processes;

/// <summary>A signal a caller hands a process runner to say that the child is silent because the CALLER is waiting, not
/// because it is dead: while any <see cref="Hold"/> is open the runner does not read silence as inactivity, and when the
/// last is released the window starts again from that moment. A runner that cannot honour it says so
/// (<see cref="IProcessRunner.CanPauseInactivity"/>), and one that can subscribes (<see cref="Subscribe"/>) and re-arms its
/// clock when the state changes.
/// <para>It pauses the inactivity window ONLY: a maximum duration is a wall clock and keeps running, and a held pause
/// never keeps a turn alive past the caller's own cancellation.</para></summary>
public sealed class InactivityPause
{
    private readonly object _gate = new();
    private readonly List<Action> _subscribers = [];
    private int _holds;

    /// <summary>True while at least one <see cref="Hold"/> is open.</summary>
    public bool IsPaused
    {
        get
        {
            lock (_gate) return _holds > 0;
        }
    }

    /// <summary>Open a hold. Dispose the returned handle to release it; releasing twice releases once.</summary>
    public IDisposable Hold()
    {
        lock (_gate) _holds++;
        Notify();
        return new Release(this);
    }

    /// <summary>For a runner: be told whenever <see cref="IsPaused"/> may have changed, and read it then. The callback
    /// runs on the thread that changed it, outside this object's lock, so it may be called concurrently with itself and
    /// with the runner's own reads, and must converge on the current state rather than trust an order. A throw from
    /// it is swallowed: it never fails the hold or release that told it, nor stops the subscribers after it.</summary>
    /// <param name="changed">Called after each hold opens or closes.</param>
    /// <returns>Dispose to stop being told.</returns>
    public IDisposable Subscribe(Action changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        lock (_gate) _subscribers.Add(changed);
        return new Unsubscribe(this, changed);
    }

    // Each subscriber is isolated and its fault swallowed (there is no logger here): a throw out of one would fail
    // a Hold whose count is already taken, with no handle returned to release it, and skip the subscribers after.
    private void Notify()
    {
        Action[] snapshot;
        lock (_gate) snapshot = [.. _subscribers];
        foreach (var subscriber in snapshot)
        {
            try { subscriber(); }
            catch (Exception) { /* a runner re-arming a timeout that is already disposed, say */ }
        }
    }

    private sealed class Release(InactivityPause owner) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            lock (owner._gate) owner._holds--;
            owner.Notify();
        }
    }

    private sealed class Unsubscribe(InactivityPause owner, Action changed) : IDisposable
    {
        public void Dispose()
        {
            lock (owner._gate) owner._subscribers.Remove(changed);
        }
    }
}
