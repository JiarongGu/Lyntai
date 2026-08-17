namespace Lyntai.Storage;

/// <summary>An async lock PER KEY: acquiring a key serializes with other holders of the SAME key and with
/// nobody else. The table is bounded by keys in flight — an entry is created when its first holder arrives
/// and removed when its last one leaves, so an idle process holds nothing and there is nothing to cap or
/// expire.</summary>
/// <remarks>Both relational job stores key one of these by job id around the step-log read-modify-write:
/// the fenced UPDATE already makes cross-process interleaving safe, so only same-job reports within one
/// process need serializing — a single store-wide gate serialized EVERY concurrent job's step reporting
/// across two database round-trips. A BYO relational backend can reuse it the same way
/// <see cref="JobStoreSql"/> is reused.</remarks>
/// <typeparam name="TKey">The key type; compared with the default equality comparer.</typeparam>
public sealed class KeyedLock<TKey> where TKey : notnull
{
    private sealed class Entry
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public int Holders;
    }

    private readonly Dictionary<TKey, Entry> _entries = [];

    /// <summary>Entries currently alive — the bounded-table property, for the tests.</summary>
    internal int EntryCount { get { lock (_entries) return _entries.Count; } }

    /// <summary>Take <paramref name="key"/>'s gate; dispose the returned handle to release it.</summary>
    /// <param name="key">The key to serialize on.</param>
    /// <param name="ct">Cancels the WAIT; a cancelled waiter leaves no refcount behind.</param>
    public async ValueTask<IDisposable> AcquireAsync(TKey key, CancellationToken ct = default)
    {
        Entry entry;
        lock (_entries)
        {
            if (!_entries.TryGetValue(key, out entry!)) _entries[key] = entry = new Entry();
            entry.Holders++;
        }
        try
        {
            await entry.Gate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            Release(key, entry, held: false);
            throw;
        }
        return new Holder(this, key, entry);
    }

    private void Release(TKey key, Entry entry, bool held)
    {
        lock (_entries)
        {
            if (--entry.Holders == 0) _entries.Remove(key);
        }
        // outside the table lock: a same-entry waiter woken here re-enters nothing this lock protects
        if (held) entry.Gate.Release();
    }

    private sealed class Holder(KeyedLock<TKey> owner, TKey key, Entry entry) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Release(key, entry, held: true);
        }
    }
}
