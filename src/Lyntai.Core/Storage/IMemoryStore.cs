namespace Lyntai.Storage;

/// <summary>One learned fact, scoped by (taskKey, scope).</summary>
public sealed record MemoryEntry(long Id, string TaskKey, string Scope, string Content, DateTimeOffset CreatedAt);

/// <summary>
/// Task-scoped learned facts. Bounded: entries per (taskKey, scope) are capped (oldest trimmed).
/// Fail-open: recall never throws on an empty/short/unmatchable query — it degrades (FTS → LIKE →
/// most-recent) and at worst returns an empty list. Lifecycle: remembering an identical fact refreshes
/// it rather than duplicating; an optional TTL expires it from recall; <see cref="PruneAsync"/> reaps.
/// </summary>
public interface IMemoryStore
{
    /// <summary>Remember a fact. Remembering an identical <paramref name="content"/> in the same
    /// (taskKey, scope) refreshes the existing entry's recency + TTL instead of duplicating it. An
    /// optional <paramref name="ttl"/> makes the entry expire (dropped from recall, reaped by prune).</summary>
    Task RememberAsync(string taskKey, string scope, string content, TimeSpan? ttl = null, CancellationToken ct = default);

    /// <summary>Recall entries for a task, optionally filtered by scope and ranked against a query
    /// (FTS trigram match with LIKE fallback); no query → most recent first. Expired entries are never
    /// returned.</summary>
    Task<IReadOnlyList<MemoryEntry>> RecallAsync(string taskKey, string? scope = null, string? query = null,
        int? limit = null, CancellationToken ct = default);

    Task ForgetAsync(string taskKey, string? scope = null, CancellationToken ct = default);

    /// <summary>Reap entries that are expired, and (when <paramref name="olderThan"/> is given) those
    /// older than that age — optionally scoped to one task. Returns the number removed.</summary>
    Task<int> PruneAsync(string? taskKey = null, TimeSpan? olderThan = null, CancellationToken ct = default);
}
