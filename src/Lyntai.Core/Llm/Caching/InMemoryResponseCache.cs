using System.Collections.Concurrent;

namespace Lyntai.Llm.Caching;

/// <summary>
/// A process-local, size-bounded <see cref="IResponseCache"/> with per-entry TTL — the default the
/// <c>AddResponseCache()</c> wiring registers. For a persistent or cross-process shared cache, register
/// your own <see cref="IResponseCache"/> instead (the seam is the extension point). Eviction is a soft
/// cap: expired entries go first, then the oldest-inserted — good enough for a warm local cache, not a
/// strict LRU. The clock is injectable so TTL/eviction are deterministic under test.
/// </summary>
public sealed class InMemoryResponseCache(LyntaiOptions options, Func<DateTimeOffset>? clock = null) : IResponseCache
{
    private readonly record struct Entry(LlmReply Reply, DateTimeOffset ExpiresAt, long Seq);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private long _seq;

    public Task<LlmReply?> TryGetAsync(string key, CancellationToken ct = default)
    {
        if (_entries.TryGetValue(key, out var e))
        {
            if (e.ExpiresAt > _clock()) return Task.FromResult<LlmReply?>(e.Reply);
            _entries.TryRemove(key, out _); // lazily drop the expired entry on the way past
        }
        return Task.FromResult<LlmReply?>(null);
    }

    public Task SetAsync(string key, LlmReply reply, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var window = ttl ?? options.Cache.Ttl;
        if (window <= TimeSpan.Zero) return Task.CompletedTask; // non-positive TTL disables caching
        _entries[key] = new Entry(reply, _clock() + window, Interlocked.Increment(ref _seq));
        if (_entries.Count > options.Cache.MaxEntries) Evict();
        return Task.CompletedTask;
    }

    private void Evict()
    {
        var now = _clock();
        foreach (var kv in _entries)
            if (kv.Value.ExpiresAt <= now) _entries.TryRemove(kv.Key, out _);
        var overflow = _entries.Count - options.Cache.MaxEntries;
        if (overflow <= 0) return;
        // still over cap after dropping expired → shed the oldest-inserted. Eviction is off the hot path
        // (only when over the soft cap), so an ordered pass here is acceptable for a local cache default.
        foreach (var kv in _entries.OrderBy(e => e.Value.Seq).Take(overflow))
            _entries.TryRemove(kv.Key, out _);
    }
}
