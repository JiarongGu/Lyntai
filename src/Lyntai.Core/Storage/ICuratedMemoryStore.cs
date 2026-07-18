namespace Lyntai.Storage;

/// <summary>One entry in a curated memory CATALOG — a deliberately managed fact (vs the automatic
/// remember/recall log of <see cref="IMemoryStore"/>). <paramref name="Kind"/> groups entries into
/// prompt sections; <paramref name="Enabled"/> toggles an entry in/out of composition without deleting it;
/// <paramref name="Source"/> notes where it came from (a doc, a user, an import).</summary>
public sealed record CuratedMemory(
    long Id, string Kind, string Content, string? Source, bool Enabled,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>
/// A curated memory catalog: hand-managed entries grouped by <c>Kind</c>, each individually
/// enable/disable-able and editable — as opposed to <see cref="IMemoryStore"/>'s automatic, bounded,
/// dedup/TTL remember-recall LOG. Use it for durable, operator-curated context (persona facts, house
/// style, domain glossaries) composed into a prompt per kind (see <c>CuratedMemorySections</c>). No
/// capping/TTL/relevance search — the catalog is small and deliberate.
/// </summary>
public interface ICuratedMemoryStore
{
    /// <summary>Add a catalog entry; returns its id.</summary>
    Task<long> AddAsync(string kind, string content, string? source = null, bool enabled = true, CancellationToken ct = default);

    /// <summary>Update an entry in place — only the non-null arguments change (COALESCE semantics), so
    /// passing just <paramref name="enabled"/> toggles it without touching the content. To CLEAR the
    /// source, pass an empty string (null means "leave unchanged"). Returns whether a row was updated.</summary>
    Task<bool> UpdateAsync(long id, string? content = null, bool? enabled = null, string? source = null, CancellationToken ct = default);

    /// <summary>Delete an entry. Returns whether one was removed.</summary>
    Task<bool> RemoveAsync(long id, CancellationToken ct = default);

    Task<CuratedMemory?> GetAsync(long id, CancellationToken ct = default);

    /// <summary>List entries, optionally filtered by <paramref name="kind"/> and to
    /// <paramref name="enabledOnly"/>. Ordered by kind then creation (stable for prompt composition).</summary>
    Task<IReadOnlyList<CuratedMemory>> ListAsync(string? kind = null, bool enabledOnly = false, int? limit = null, CancellationToken ct = default);
}
