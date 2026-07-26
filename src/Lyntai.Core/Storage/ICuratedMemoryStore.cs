namespace Lyntai.Storage;

/// <summary>One entry in a curated memory CATALOG — a deliberately managed fact (vs the automatic
/// remember/recall log of <see cref="IMemoryStore"/>). <paramref name="Kind"/> groups entries into
/// prompt sections; <paramref name="Enabled"/> toggles an entry in/out of composition without deleting it;
/// <paramref name="Source"/> notes where it came from (a doc, a user, an import).
/// <paramref name="TaskKey"/> (optional) scopes the entry to a consumer/purpose (e.g. "translation");
/// <paramref name="Scope"/> (optional) scopes it to a variant (e.g. "lang:zh"). A null <see cref="TaskKey"/>
/// or <see cref="Scope"/> means "applies everywhere" — see <see cref="ICuratedMemoryStore.ForCompositionAsync"/>.
/// <paramref name="Title"/> (optional) is a short display label for the longer <paramref name="Content"/>
/// (a glossary term, a persona trait, a note title); null/empty = untitled. <c>CuratedMemorySections.Compose</c>
/// renders it as the entry's lead.</summary>
public sealed record CuratedMemory(
    long Id, string Kind, string Content, string? Source, bool Enabled,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? TaskKey = null, string? Scope = null,
    string? Title = null);

/// <summary>
/// A curated memory catalog: hand-managed entries grouped by <c>Kind</c>, each individually
/// enable/disable-able and editable — as opposed to <see cref="IMemoryStore"/>'s automatic, bounded,
/// dedup/TTL remember-recall LOG. Use it for durable, operator-curated context (persona facts, house
/// style, domain glossaries) composed into a prompt per kind (see <c>CuratedMemorySections</c>). No
/// capping/TTL — the catalog is small and deliberate; <see cref="SearchAsync"/> is an added READ path
/// (keyword lookup for a catalog UI / agent recall), not lifecycle management.
/// </summary>
public interface ICuratedMemoryStore
{
    /// <summary>Add a catalog entry; returns its id. <paramref name="taskKey"/>/<paramref name="scope"/> are
    /// optional per-consumer/per-variant filters (null = applies everywhere; see <see cref="ForCompositionAsync"/>).
    /// <para>When <paramref name="dedup"/> is true, the add is IDEMPOTENT on the identity
    /// (<paramref name="kind"/>, <paramref name="content"/>, <paramref name="taskKey"/>, <paramref name="scope"/>):
    /// if a row with that exact identity already exists its id is returned and no second row is written (mirroring
    /// <see cref="IMemoryStore.RememberAsync"/>'s dedup) — so a consumer can write a fact idempotently without a
    /// pre-<see cref="ListAsync"/>+compare. The default (false) always inserts, keeping the "deliberate catalog"
    /// behavior. Dedup ignores <paramref name="enabled"/>/<paramref name="source"/>/<paramref name="title"/>
    /// — only the identity matters —
    /// and does not mutate the matched row. On the SQL backends idempotence is a pre-insert identity check,
    /// not a unique index (<c>dedup: false</c> legitimately allows duplicates), so it is BEST-EFFORT under
    /// CONCURRENT writers of the same identity — the catalog is a low-write, deliberately-managed set, and a
    /// rare racing duplicate is benign (the next dedup add keeps returning the first row's id).</para>
    /// <para><paramref name="title"/> is the optional short display label (see <see cref="CuratedMemory.Title"/>);
    /// it sits after <paramref name="dedup"/> so no pre-existing positional call site silently re-binds.</para></summary>
    Task<long> AddAsync(string kind, string content, string? source = null, bool enabled = true,
        string? taskKey = null, string? scope = null, bool dedup = false, string? title = null,
        CancellationToken ct = default);

    /// <summary>Update an entry in place — only the non-null arguments change (COALESCE semantics), so
    /// passing just <paramref name="enabled"/> toggles it without touching the content. To CLEAR the
    /// source or title, pass an empty string (null means "leave unchanged"). Returns whether a row was updated.</summary>
    Task<bool> UpdateAsync(long id, string? content = null, bool? enabled = null, string? source = null,
        string? title = null, CancellationToken ct = default);

    /// <summary>Delete an entry. Returns whether one was removed.</summary>
    Task<bool> RemoveAsync(long id, CancellationToken ct = default);

    Task<CuratedMemory?> GetAsync(long id, CancellationToken ct = default);

    /// <summary>List entries, optionally filtered by <paramref name="kind"/>, <paramref name="taskKey"/> and
    /// <paramref name="scope"/> (both STRICT equality — a null-task-key/null-scope row is NOT included; this is the
    /// admin/management filter, distinct from <see cref="ForCompositionAsync"/>'s applies-everywhere read
    /// semantics — the optimize/admin pass uses <paramref name="scope"/> to pull "all notes for ONE scope, incl.
    /// disabled"), and to <paramref name="enabledOnly"/>. Ordered by kind (byte-ordinal on every backend —
    /// Postgres orders <c>COLLATE "C"</c> to match SQLite/InMemory) then creation.</summary>
    Task<IReadOnlyList<CuratedMemory>> ListAsync(string? kind = null, bool enabledOnly = false,
        string? taskKey = null, string? scope = null, int? limit = null, CancellationToken ct = default);

    /// <summary>Keyword search over the catalog — the relevance READ path for a searchable curated UI or an
    /// agent recalling from the curated set (<see cref="ListAsync"/> is the enumeration path; this one NEEDS a
    /// <paramref name="query"/> — null/whitespace returns empty). Matches <see cref="CuratedMemory.Content"/>
    /// AND <see cref="CuratedMemory.Title"/>. The filters mirror <see cref="ListAsync"/>: strict-equality
    /// <paramref name="kind"/>/<paramref name="taskKey"/>/<paramref name="scope"/>, <paramref name="enabledOnly"/>
    /// default false (admin/catalog view — pass true for the recall path); null <paramref name="limit"/> = no cap.
    /// <para>Backend DIVERGENCE (by design, same as <see cref="IMemoryStore.RecallAsync"/> — the three backends
    /// use three index engines): SQLite matches ANY ≥3-char query token via the FTS5-trigram index ranked by
    /// bm25 relevance (falling back to LIKE-substring when no token is indexable); Postgres (pg_trgm-accelerated
    /// ILIKE) and InMemory match the query as one contiguous substring, ranked by recency. The portable
    /// guarantee: an entry whose content or title contains a ≥3-char query token as an (ASCII-case-insensitive)
    /// substring is found on every backend. Fail-open like recall: storage faults degrade to an empty result,
    /// never a throw (only cancellation propagates).</para></summary>
    Task<IReadOnlyList<CuratedMemory>> SearchAsync(string query, string? kind = null, string? taskKey = null,
        string? scope = null, bool enabledOnly = false, int? limit = null, CancellationToken ct = default);

    /// <summary>The READ-for-prompt filter: enabled entries whose <see cref="CuratedMemory.TaskKey"/> matches
    /// <paramref name="taskKey"/> (or is null — a null-task-key row applies to every task) AND whose
    /// <see cref="CuratedMemory.Scope"/> is null/empty OR is in <paramref name="scopes"/>. Passing an EMPTY
    /// <paramref name="scopes"/> disables the scope filter (every scope of the task is returned). Ordered like
    /// <see cref="ListAsync"/> so <c>CuratedMemorySections.Compose</c> renders stable per-kind sections.
    /// Set <paramref name="enabledOnly"/> false to include disabled rows (admin preview).</summary>
    Task<IReadOnlyList<CuratedMemory>> ForCompositionAsync(string taskKey, IEnumerable<string> scopes,
        bool enabledOnly = true, CancellationToken ct = default);
}
