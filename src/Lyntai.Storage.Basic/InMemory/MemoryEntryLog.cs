namespace Lyntai.Storage.InMemory;

/// <summary>One remembered fact as the in-process stores hold it. <see cref="RuneLength"/> is the code-point
/// count, cached at write, so the size budget measures what SQL <c>LENGTH()</c> does rather than UTF-16
/// units.</summary>
internal sealed record MemoryLogEntry(long Id, string TaskKey, string Scope, string Content,
    DateTimeOffset CreatedAt, DateTimeOffset LastAccessedAt, DateTimeOffset? ExpiresAt, int RuneLength)
{
    public MemoryEntry ToEntry() => new(Id, TaskKey, Scope, Content, CreatedAt);

    public MemoryEviction.Row ToEvictionRow() => new(Id, CreatedAt, LastAccessedAt, ExpiresAt, RuneLength);
}

/// <summary>
/// The remember/recall log BOTH in-process <see cref="IMemoryStore"/>s run, so their semantics are one copy —
/// the same plan-then-apply shape as <see cref="MemoryGraphState"/>: <see cref="PlanRemember"/> computes the
/// write and the evictions it causes without mutating, and the file store writes each before
/// <see cref="Apply"/>-ing it. Not thread-safe — each store holds its own lock around every call.
/// </summary>
internal sealed class MemoryEntryLog(LyntaiOptions options)
{
    private readonly List<MemoryLogEntry> _entries = [];

    public long NextId { get; private set; } = 1;

    /// <summary>Start from entries already stored, never handing out an id at or below
    /// <paramref name="highWater"/>.</summary>
    public void Load(IEnumerable<MemoryLogEntry> entries, long highWater)
    {
        _entries.AddRange(entries);
        NextId = Math.Max(_entries.Select(e => e.Id).DefaultIfEmpty(0).Max(), highWater) + 1;
    }

    /// <summary>What a remember writes — the identical fact refreshed (recency, access and TTL), or a new
    /// entry — and the ids the eviction policy then removes from its (task, scope).</summary>
    public (MemoryLogEntry Write, IReadOnlyList<long> Evicted) PlanRemember(string taskKey, string scope,
        string content, TimeSpan? ttl, DateTimeOffset now)
    {
        var policy = options.MemoryEviction;
        var effectiveTtl = ttl ?? policy.DefaultTtl; // a per-call ttl wins over the policy default
        var expiresAt = effectiveTtl is null ? (DateTimeOffset?)null : now + effectiveTtl.Value;
        var existing = _entries.Find(e => e.TaskKey == taskKey && e.Scope == scope && e.Content == content);
        var write = existing is not null
            ? existing with { CreatedAt = now, LastAccessedAt = now, ExpiresAt = expiresAt }
            : new MemoryLogEntry(NextId, taskKey, scope, content, now, now, expiresAt, content.EnumerateRunes().Count());

        if (!policy.HasSizeBound) return (write, []);
        // the shared MemoryEviction picks the survivors — the SQL backends run the same rule
        var scoped = _entries.Where(e => e.TaskKey == taskKey && e.Scope == scope && e.Id != write.Id).Append(write).ToList();
        var keep = MemoryEviction.Survivors(policy, scoped.Select(e => e.ToEvictionRow()), now);
        return (write, [.. scoped.Where(e => !keep.Contains(e.Id)).Select(e => e.Id)]);
    }

    /// <summary>Store a planned entry: replace the entry holding its id, or add it.</summary>
    public void Apply(MemoryLogEntry entry)
    {
        var i = _entries.FindIndex(e => e.Id == entry.Id);
        if (i >= 0) _entries[i] = entry;
        else _entries.Add(entry);
        NextId = Math.Max(NextId, entry.Id + 1);
    }

    public MemoryLogEntry? Get(long id) => _entries.Find(e => e.Id == id);

    public void Remove(long id) => _entries.RemoveAll(e => e.Id == id);

    /// <summary>The entries a recall returns, best first: unexpired, in the task (and scope), matching the
    /// query's terms — the MATCHED-TERM COUNT leads, then recency, as <see cref="IMemoryStore.RecallAsync"/>
    /// documents, so under a limit this returns the same entries as the SQL stores. With no query every count
    /// is 0 and it collapses to most recent first.</summary>
    public IReadOnlyList<MemoryLogEntry> Recall(string taskKey, string? scope, string? query, int? limit, DateTimeOffset now)
    {
        var asked = !string.IsNullOrWhiteSpace(query);
        var terms = asked ? SearchTerms.SubstringTerms(query) : [];
        return [.. _entries
            .Where(e => e.TaskKey == taskKey && (scope is null || e.Scope == scope)
                        && (e.ExpiresAt is null || e.ExpiresAt > now))
            .Select(e => (Entry: e, Count: asked ? SearchTerms.MatchCount(e.Content, terms, query) : 0))
            .Where(x => !asked || x.Count > 0)
            .OrderByDescending(x => x.Count).ThenByDescending(x => x.Entry.CreatedAt).ThenByDescending(x => x.Entry.Id)
            .Take(limit ?? options.MemoryRecallLimit)
            .Select(x => x.Entry)];
    }

    /// <summary>Whether a recall with this query counts as USE for LRU — only a queried one does, so a
    /// routine list-all never churns the working set (<see cref="MemoryEvictionPolicy.TracksAccess"/>).</summary>
    public bool Touches(string? query) => options.MemoryEviction.TracksAccess && !string.IsNullOrWhiteSpace(query);

    public IReadOnlyList<long> ForgetIds(string taskKey, string? scope) =>
        [.. _entries.Where(e => e.TaskKey == taskKey && (scope is null || e.Scope == scope)).Select(e => e.Id)];

    /// <summary>Expired entries, and those older than <paramref name="olderThan"/> — in one task, or all.</summary>
    public IReadOnlyList<long> PruneIds(string? taskKey, TimeSpan? olderThan, DateTimeOffset now)
    {
        var cutoff = olderThan is null ? (DateTimeOffset?)null : now - olderThan.Value;
        return [.. _entries
            .Where(e => (taskKey is null || e.TaskKey == taskKey)
                        && ((e.ExpiresAt is not null && e.ExpiresAt <= now) || (cutoff is not null && e.CreatedAt < cutoff)))
            .Select(e => e.Id)];
    }
}
