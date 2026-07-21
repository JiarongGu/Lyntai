namespace Lyntai.Storage;

/// <summary>One entry in a curated memory CATALOG — a deliberately managed fact (vs the automatic
/// remember/recall log of <see cref="IMemoryStore"/>). <paramref name="Kind"/> groups entries into
/// prompt sections; <paramref name="Enabled"/> toggles an entry in/out of composition without deleting it;
/// <paramref name="Source"/> notes where it came from (a doc, a user, an import).
/// <paramref name="Task"/> (optional) scopes the entry to a consumer/purpose (e.g. "translation");
/// <paramref name="Scope"/> (optional) scopes it to a variant (e.g. "lang:zh"). A null <see cref="Task"/>
/// or <see cref="Scope"/> means "applies everywhere" — see <see cref="ICuratedMemoryStore.ForCompositionAsync"/>.</summary>
public sealed record CuratedMemory(
    long Id, string Kind, string Content, string? Source, bool Enabled,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? Task = null, string? Scope = null);

/// <summary>
/// A curated memory catalog: hand-managed entries grouped by <c>Kind</c>, each individually
/// enable/disable-able and editable — as opposed to <see cref="IMemoryStore"/>'s automatic, bounded,
/// dedup/TTL remember-recall LOG. Use it for durable, operator-curated context (persona facts, house
/// style, domain glossaries) composed into a prompt per kind (see <c>CuratedMemorySections</c>). No
/// capping/TTL/relevance search — the catalog is small and deliberate.
/// </summary>
public interface ICuratedMemoryStore
{
    /// <summary>Add a catalog entry; returns its id. <paramref name="task"/>/<paramref name="scope"/> are
    /// optional per-consumer/per-variant filters (null = applies everywhere; see <see cref="ForCompositionAsync"/>).</summary>
    Task<long> AddAsync(string kind, string content, string? source = null, bool enabled = true,
        string? task = null, string? scope = null, CancellationToken ct = default);

    /// <summary>Update an entry in place — only the non-null arguments change (COALESCE semantics), so
    /// passing just <paramref name="enabled"/> toggles it without touching the content. To CLEAR the
    /// source, pass an empty string (null means "leave unchanged"). Returns whether a row was updated.</summary>
    Task<bool> UpdateAsync(long id, string? content = null, bool? enabled = null, string? source = null, CancellationToken ct = default);

    /// <summary>Delete an entry. Returns whether one was removed.</summary>
    Task<bool> RemoveAsync(long id, CancellationToken ct = default);

    Task<CuratedMemory?> GetAsync(long id, CancellationToken ct = default);

    /// <summary>List entries, optionally filtered by <paramref name="kind"/>, <paramref name="task"/>
    /// (strict equality — null-task rows are NOT included; this is the admin/management filter, distinct from
    /// <see cref="ForCompositionAsync"/>'s applies-everywhere read semantics), and to
    /// <paramref name="enabledOnly"/>. Ordered by kind then creation. NOTE: the kind ordering is NOT
    /// guaranteed ordinal-stable across backends — Postgres orders by its DB collation while InMemory/SQLite
    /// order ordinally; if exact ordering matters, sort in-app (<c>CuratedMemorySections.Compose</c> re-sorts
    /// ordinal, so the composed prompt is stable regardless).</summary>
    Task<IReadOnlyList<CuratedMemory>> ListAsync(string? kind = null, bool enabledOnly = false,
        string? task = null, int? limit = null, CancellationToken ct = default);

    /// <summary>The READ-for-prompt filter: enabled entries whose <see cref="CuratedMemory.Task"/> matches
    /// <paramref name="task"/> (or is null — a null-task row applies to every task) AND whose
    /// <see cref="CuratedMemory.Scope"/> is null/empty OR is in <paramref name="scopes"/>. Passing an EMPTY
    /// <paramref name="scopes"/> disables the scope filter (every scope of the task is returned). Ordered like
    /// <see cref="ListAsync"/> so <c>CuratedMemorySections.Compose</c> renders stable per-kind sections.
    /// Set <paramref name="enabledOnly"/> false to include disabled rows (admin preview).</summary>
    Task<IReadOnlyList<CuratedMemory>> ForCompositionAsync(string task, IEnumerable<string> scopes,
        bool enabledOnly = true, CancellationToken ct = default);
}
