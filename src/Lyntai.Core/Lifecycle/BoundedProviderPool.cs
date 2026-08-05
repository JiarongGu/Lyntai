using System.Runtime.CompilerServices;

namespace Lyntai.Lifecycle;

/// <summary>The default strategy: REUSE while a configuration is unchanged, within declared bounds.
///
/// <para>Reuse is what stops a per-call configuration read from turning into a per-call backend
/// construction. The bounds are what stop a store with many tenants from growing the pool without limit —
/// the same two bounds, for the same two reasons, that a database connection pool carries.</para>
///
/// <para><b>Eviction is evaluated on access</b>, not by a timer: a library should not start a background
/// thread, and a clock read inside <see cref="GetOrAdd"/> keeps tests deterministic. The consequence, worth
/// knowing, is that a pool nobody calls stops evicting — acceptable, because an idle process is not holding
/// anything contended and the next call cleans up before doing anything else.</para>
///
/// <para><b>Nothing is ever disposed</b> — see <see cref="IProviderPool{TProvider}.Retire"/> for why that
/// follows from refusing to break in-flight work.</para></summary>
/// <typeparam name="TProvider">The provider seam being pooled.</typeparam>
/// <param name="options">Bounds. Null = the <see cref="ProviderPoolOptions"/> defaults.</param>
/// <param name="clock">Time source, injected so idle eviction is testable. Null = UTC now.</param>
public sealed class BoundedProviderPool<TProvider>(
    ProviderPoolOptions? options = null, Func<DateTimeOffset>? clock = null) : IProviderPool<TProvider>
    where TProvider : class, IProviderIdentity
{
    private readonly ProviderPoolOptions _options = options ?? new ProviderPoolOptions();
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    private readonly Dictionary<ProviderKey, Entry> _entries = [];
    private readonly ConditionalWeakTable<TProvider, StrongBox<ProviderKey>> _keys = [];
    private readonly Lock _lock = new();

    private long _created;
    private long _reused;
    private long _retired;
    private long _ticket;   // monotonic recency stamp: cheaper and clearer than reordering a list

    private sealed class Entry(TProvider instance, DateTimeOffset lastUsed, long ticket)
    {
        public TProvider Instance { get; } = instance;
        public DateTimeOffset LastUsed { get; set; } = lastUsed;
        public long Ticket { get; set; } = ticket;
    }

    /// <inheritdoc/>
    /// <remarks><para><paramref name="factory"/> runs while this pool's internal lock is held — construction
    /// inside the lock is what makes concurrent calls for one key build exactly once instead of racing (see
    /// <c>Concurrent_calls_on_one_key_build_exactly_once</c>). It must not block, await, or call back into
    /// this pool: a factory that does I/O (reads a credential from a vault, probes an endpoint, blocks on an
    /// async build) serializes EVERY tenant and EVERY key behind it, not just its own; it also blocks
    /// <see cref="Statistics"/>, so a health endpoint stalls behind one slow construction; and a factory that
    /// blocks on another thread which itself calls into this pool deadlocks outright.</para>
    /// <para><see cref="System.Threading.Lock"/> is reentrant, so a factory that synchronously calls
    /// <see cref="GetOrAdd"/> again for the SAME key is not rejected — it builds and stores its own entry,
    /// and the outer frame then overwrites <c>_entries[key]</c> with its own on return. <c>Created</c> counts
    /// both, <c>Retired</c> counts neither, and the inner instance is now held by its caller with no entry
    /// pointing at it. This is the one path where "build exactly once" is not actually enforced — the fix is
    /// simply never re-entering, not a code change.</para></remarks>
    public TProvider GetOrAdd(ProviderKey key, Func<TProvider> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        // A single lock covers the idle sweep, the reuse check, construction and the overflow eviction.
        // Building inside the lock is deliberate: it is what makes "concurrent calls on one key build
        // exactly once" true. Construction is cheap (a wrapper over options), so serializing it per pool
        // is the right trade against a double-checked release/reacquire that only exists to undo itself.
        lock (_lock)
        {
            EvictIdle();

            if (_entries.TryGetValue(key, out var existing))
            {
                Touch(existing);
                _reused++;
                return existing.Instance;
            }

            var instance = factory() ?? throw new InvalidOperationException(
                $"the factory for '{key}' returned null");
            ProviderPoolGuard.EnsureIdMatchesSlot(instance, key);

            _entries[key] = new Entry(instance, _clock(), ++_ticket);
            _keys.AddOrUpdate(instance, new StrongBox<ProviderKey>(key));
            _created++;

            EvictOverflow();
            return instance;
        }
    }

    /// <inheritdoc/>
    public bool TryGetKey(TProvider instance, out ProviderKey key)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (_keys.TryGetValue(instance, out var box)) { key = box.Value; return true; }
        key = default;
        return false;
    }

    /// <inheritdoc/>
    public bool Retire(ProviderKey key)
    {
        lock (_lock)
        {
            if (!_entries.Remove(key)) return false;
            _retired++;
            return true;
        }
    }

    /// <inheritdoc/>
    public int RetireSlot(string slot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        lock (_lock)
        {
            var doomed = _entries.Keys
                .Where(k => string.Equals(k.Slot, slot, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var key in doomed) _entries.Remove(key);
            _retired += doomed.Count;
            return doomed.Count;
        }
    }

    /// <inheritdoc/>
    public ProviderPoolStatistics Statistics
    {
        get { lock (_lock) return new(_entries.Count, _created, _reused, _retired); }
    }

    private void Touch(Entry entry)
    {
        entry.LastUsed = _clock();
        entry.Ticket = ++_ticket;
    }

    /// <summary>Retire entries unused past the timeout. Caller holds the lock.
    ///
    /// <para>Scanned in two passes with an early return, rather than a LINQ chain, because this runs on EVERY
    /// <see cref="GetOrAdd"/> whenever <see cref="ProviderPoolOptions.IdleTimeout"/> is set — and it is set by
    /// default. A <c>foreach</c> over the dictionary uses its STRUCT enumerator and allocates nothing, so the
    /// overwhelmingly common outcome ("nothing is idle") now costs one scan and no garbage; the materialised
    /// list is paid for only when there is actually something to evict. The second pass exists because the
    /// keys have to be collected before they are removed.</para></summary>
    private void EvictIdle()
    {
        if (_options.IdleTimeout is not { } timeout) return;

        var cutoff = _clock() - timeout;

        var anyIdle = false;
        foreach (var entry in _entries)
        {
            if (entry.Value.LastUsed > cutoff) continue;
            anyIdle = true;
            break;
        }

        if (!anyIdle) return;

        var doomed = new List<ProviderKey>();
        foreach (var entry in _entries)
            if (entry.Value.LastUsed <= cutoff) doomed.Add(entry.Key);

        foreach (var key in doomed) _entries.Remove(key);
        _retired += doomed.Count;
    }

    /// <summary>Retire least-recently-used entries down to the cap. Caller holds the lock.
    ///
    /// <para>The victim is found by a linear scan for the lowest ticket rather than by sorting: an
    /// <c>OrderBy(…).First()</c> is O(n log n) and materialises an array on every single eviction, where
    /// finding a minimum is O(n) over the dictionary's struct enumerator and allocates nothing. Tickets are
    /// unique (a monotonic counter) and the scan keeps the FIRST minimum it meets, so it picks exactly the
    /// entry a stable <c>OrderBy</c> would.</para></summary>
    private void EvictOverflow()
    {
        if (_options.MaxEntries is not { } max || max <= 0) return;

        while (_entries.Count > max)
        {
            // `found` rather than a long.MaxValue sentinel, deliberately: an entry whose Ticket happened to BE
            // long.MaxValue could never beat that sentinel, so no victim would be selected, Remove would
            // return false, and this loop would spin forever. It takes 2^63 GetOrAdd calls to reach and the
            // OrderBy version this replaced could not fail that way at all — but an unreachable hang is still
            // the worst shape a library bug can take, and the flag costs one predictable branch.
            ProviderKey oldest = default;
            var oldestTicket = 0L;
            var found = false;
            foreach (var entry in _entries)
            {
                if (found && entry.Value.Ticket >= oldestTicket) continue;
                oldestTicket = entry.Value.Ticket;
                oldest = entry.Key;
                found = true;
            }

            // the loop condition guarantees at least one entry, so `found` is always true here
            if (!found) break;

            _entries.Remove(oldest);
            _retired++;
        }
    }
}
