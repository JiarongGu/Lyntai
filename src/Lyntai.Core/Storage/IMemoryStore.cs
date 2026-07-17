namespace Lyntai.Storage;

/// <summary>One learned fact, scoped by (taskKey, scope).</summary>
public sealed record MemoryEntry(long Id, string TaskKey, string Scope, string Content, DateTimeOffset CreatedAt);

/// <summary>
/// Task-scoped learned facts. Bounded: entries per (taskKey, scope) are capped (oldest trimmed).
/// Fail-open: recall never throws on an empty/short/unmatchable query — it degrades (FTS → LIKE →
/// most-recent) and at worst returns an empty list.
/// </summary>
public interface IMemoryStore
{
    Task RememberAsync(string taskKey, string scope, string content, CancellationToken ct = default);

    /// <summary>Recall entries for a task, optionally filtered by scope and ranked against a query
    /// (FTS trigram match with LIKE fallback); no query → most recent first.</summary>
    Task<IReadOnlyList<MemoryEntry>> RecallAsync(string taskKey, string? scope = null, string? query = null,
        int? limit = null, CancellationToken ct = default);

    Task ForgetAsync(string taskKey, string? scope = null, CancellationToken ct = default);
}
