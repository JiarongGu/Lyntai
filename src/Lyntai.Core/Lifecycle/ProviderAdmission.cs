namespace Lyntai.Lifecycle;

/// <summary>The in-process <see cref="IProviderAdmission"/>: how many calls may be in flight against one
/// CONFIGURATION at a time, bounded by a keyed table of semaphores. A host coordinating across PROCESSES
/// implements the interface instead — see it for why that seam exists.
///
/// <para>Limits are declared per SLOT because capacity is a property of the backend kind ("a local engine
/// renders one image at a time"), and enforced per KEY because the contended resource belongs to a
/// configuration. That split is what produces the behaviour worth having: two consumers pointing at the
/// same self-hosted engine share its capacity, while two tenants on different hosted endpoints never
/// throttle each other.</para>
///
/// <para><b>Why this is not a field on the provider.</b> A semaphore carried by an instance bounds nothing
/// once instances are per-call: under <see cref="TransientProviderPool{TProvider}"/> every call builds its
/// own limiter and every caller is admitted at once, so the engine the limit exists to protect thrashes
/// exactly as it would with no limit configured. Keyed and shared is the only shape that survives both
/// pooling strategies.</para>
///
/// <para>Distinct from rate limiting: a rate limiter bounds calls per unit TIME, this bounds calls at
/// once.</para>
///
/// <para><b>A gate exists only while something needs it.</b> Each entry in the internal table tracks a
/// count of callers currently holding OR waiting on it; the count is incremented before the wait begins
/// (so a merely-queued caller still keeps its gate alive) and decremented when a handle is disposed, or
/// when a wait is cancelled before a permit ever arrives — either way, the gate is removed the instant that
/// count reaches zero. The table's size is therefore bounded by calls in flight right now, never by the
/// number of configurations ever seen, which matters because this is registered as a process-lifetime
/// singleton: a store that rotates many tenants' credentials through <see cref="ProviderKey"/> would
/// otherwise accumulate one permanent <see cref="SemaphoreSlim"/> per configuration ever presented. An idle
/// process holds no gates at all, so — unlike <see cref="ProviderPoolOptions"/> — there is no cap or
/// timeout to configure here; there is nothing left to bound.</para></summary>
/// <param name="options">Limits. Null = unlimited everywhere.</param>
public sealed class ProviderAdmission(ProviderAdmissionOptions? options = null) : IProviderAdmission
{
    private static readonly IDisposable Unlimited = new NoopHandle();

    private readonly ProviderAdmissionOptions _options = options ?? new ProviderAdmissionOptions();
    private readonly Dictionary<ProviderKey, Gate> _gates = [];
    private readonly Lock _lock = new();

    /// <inheritdoc/>
    /// <remarks>When the slot's limit is 0 — or any negative value, which reads the same way: nothing to
    /// enforce — this completes synchronously and returns the shared no-op handle.</remarks>
    public ValueTask<IDisposable> EnterAsync(ProviderKey key, CancellationToken ct = default)
    {
        var limit = LimitFor(key.Slot);
        if (limit <= 0) return ValueTask.FromResult(Unlimited);

        var gate = Acquire(key, limit);
        var wait = gate.Semaphore.WaitAsync(ct);
        return wait.IsCompletedSuccessfully
            ? ValueTask.FromResult<IDisposable>(new Handle(this, key, gate))
            : Awaited(wait, this, key, gate);

        static async ValueTask<IDisposable> Awaited(
            Task wait, ProviderAdmission admission, ProviderKey key, Gate gate)
        {
            try
            {
                await wait.ConfigureAwait(false);
            }
            catch
            {
                // Never took the permit, so nothing to release on the semaphore — but Acquire already
                // counted this wait as a holder, and that count is the only thing keeping the gate in the
                // table. Skipping this on cancellation would pin the gate forever for a caller that never
                // arrived, which is exactly the leak this whole scheme exists to avoid.
                admission.Discard(key, gate);
                throw;
            }

            return new Handle(admission, key, gate);
        }
    }

    private int LimitFor(string slot) =>
        _options.BySlot.TryGetValue(slot, out var limit) ? limit : _options.Default;

    /// <summary>Get (or create) the gate for a key and count this call as a holder of it, BEFORE waiting —
    /// so a caller that only ever gets as far as queueing still keeps the gate from being collected out
    /// from under it.</summary>
    private Gate Acquire(ProviderKey key, int limit)
    {
        lock (_lock)
        {
            if (!_gates.TryGetValue(key, out var gate))
            {
                // Reads LimitFor's result fresh each time a gate is (re)created — see
                // ProviderAdmissionOptions for what that means for a limit changed at runtime.
                gate = new Gate(new SemaphoreSlim(limit, limit));
                _gates[key] = gate;
            }
            gate.Holders++;
            return gate;
        }
    }

    /// <summary>A caller that actually held the permit is done with it: release it on the semaphore, then
    /// account for one fewer holder.</summary>
    private void Release(ProviderKey key, Gate gate)
    {
        gate.Semaphore.Release();
        Discard(key, gate);
    }

    /// <summary>One fewer holder — whether it released a permit or never got one. Removes the gate from the
    /// table once nobody is left holding or waiting on it, which is what keeps the table bounded by calls
    /// in flight rather than by every key ever seen.</summary>
    private void Discard(ProviderKey key, Gate gate)
    {
        lock (_lock)
        {
            if (--gate.Holders > 0) return;
            // The lock makes this unreachable with a MISMATCHED gate (a key is only ever rebound after
            // this branch removes it) — checked anyway because a stale remove would silently orphan a
            // fresh gate a concurrent caller just created for the same key.
            if (_gates.TryGetValue(key, out var current) && ReferenceEquals(current, gate)) _gates.Remove(key);
        }
    }

    /// <summary>Live gate count. Internal: exists so tests can pin "bounded by calls in flight" directly
    /// instead of inferring it from timing.</summary>
    internal int GateCount { get { lock (_lock) return _gates.Count; } }

    private sealed class Gate(SemaphoreSlim semaphore)
    {
        public SemaphoreSlim Semaphore { get; } = semaphore;
        public int Holders;
    }

    /// <summary>Releases exactly once, however many times it is disposed. Without this guard a double
    /// dispose does not fail safely: releasing a held permit typically returns the semaphore to its max
    /// count, so the extra release throws <see cref="SemaphoreFullException"/> immediately — but whenever
    /// OTHER callers are concurrently holding the remaining permits, the count is below max at that moment
    /// and the extra release does not throw at all. It silently raises the effective ceiling by one until
    /// the count catches up to max, which is a caller admitted past the configured limit for as long as
    /// that lasts.</summary>
    private sealed class Handle(ProviderAdmission admission, ProviderKey key, Gate gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) admission.Release(key, gate);
        }
    }

    private sealed class NoopHandle : IDisposable
    {
        public void Dispose() { }
    }
}

/// <summary>Concurrency limits per backend. See <see cref="ProviderAdmission"/> for the slot/key split.
///
/// <para>A configuration's limit is read once, at the moment its gate is first created — a
/// <see cref="SemaphoreSlim"/> has a fixed max count, so there is no live gate to resize when a value here
/// changes afterward. This matters less than it sounds: a gate is discarded the instant its last holder
/// disposes (see <see cref="ProviderAdmission"/>), so an idle configuration's very next call already reads
/// the new limit — only a configuration under contention right now keeps the old one, and only until it
/// goes idle.</para></summary>
public sealed class ProviderAdmissionOptions
{
    /// <summary>Concurrent calls allowed per configuration when the slot has no entry in
    /// <see cref="BySlot"/>. 0 — or any negative value, which says the same thing — = unlimited, which is the
    /// default: the HTTP backends are stateless and a limit would only ever slow them down.</summary>
    public int Default { get; set; }

    /// <summary>Per-slot limits — the one that matters in practice is a locally-run engine, where several
    /// simultaneous renders contend for one CPU or GPU.</summary>
    public IDictionary<string, int> BySlot { get; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}
