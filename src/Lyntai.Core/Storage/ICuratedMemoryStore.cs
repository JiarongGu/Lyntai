namespace Lyntai.Storage;

/// <summary>One entry in a curated memory CATALOG — a deliberately managed fact (vs the automatic
/// remember/recall log of <see cref="IMemoryStore"/>). <paramref name="Kind"/> groups entries into
/// prompt sections; <paramref name="Enabled"/> toggles an entry in/out of composition without deleting it.
/// <paramref name="TaskKey"/> (optional) scopes the entry to a consumer/purpose (e.g. "translation");
/// <paramref name="Scope"/> (optional) scopes it to a variant (e.g. "lang:zh"). A null <see cref="TaskKey"/>
/// or <see cref="Scope"/> means "applies everywhere" — see <see cref="ICuratedMemoryStore.ForCompositionAsync"/>.
/// <paramref name="Metadata"/> (optional) is arbitrary app-owned <c>string→string</c> extra data (e.g.
/// <c>title</c>, <c>source</c>, <c>author</c>, <c>category</c>) — stored as one opaque JSON field and
/// filterable via <see cref="ICuratedMemoryStore.ListAsync"/>/<see cref="ICuratedMemoryStore.SearchAsync"/>'s
/// <c>metadataMatch</c>. Null/empty = no metadata.</summary>
public sealed record CuratedMemory(
    long Id, string Kind, string Content, bool Enabled,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? TaskKey = null, string? Scope = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>
/// A curated memory catalog: hand-managed entries grouped by <c>Kind</c>, each individually
/// enable/disable-able and editable — as opposed to <see cref="IMemoryStore"/>'s automatic, bounded,
/// dedup/TTL remember-recall LOG. Use it for durable, operator-curated context (persona facts, house
/// style, domain glossaries) composed into a prompt per kind (see <c>CuratedMemorySections</c>). No
/// capping/TTL — the catalog is small and deliberate; <see cref="SearchAsync"/> is an added READ path
/// (keyword lookup for a catalog UI / agent recall), not lifecycle management.
/// <para>Arbitrary extra data lives in <see cref="CuratedMemory.Metadata"/> (a <c>string→string</c> map,
/// stored as one opaque JSON field per <see cref="CuratedMetadataJson"/> and made queryable by a plain
/// relational index — so a new payload field needs no schema/API change). It is filterable by exact
/// key/value via the <c>metadataMatch</c> argument on <see cref="ListAsync"/>/<see cref="SearchAsync"/>.</para>
/// </summary>
public interface ICuratedMemoryStore
{
    /// <summary>Add a catalog entry; returns its id. <paramref name="taskKey"/>/<paramref name="scope"/> are
    /// optional per-consumer/per-variant filters (null = applies everywhere; see <see cref="ForCompositionAsync"/>).
    /// <paramref name="metadata"/> is arbitrary app-owned <c>string→string</c> extra data (see
    /// <see cref="CuratedMemory.Metadata"/>).
    /// <para>When <paramref name="dedup"/> is true, the add is IDEMPOTENT on the identity
    /// (<paramref name="kind"/>, <paramref name="content"/>, <paramref name="taskKey"/>, <paramref name="scope"/>):
    /// if a row with that exact identity already exists its id is returned and no second row is written (mirroring
    /// <see cref="IMemoryStore.RememberAsync"/>'s dedup) — so a consumer can write a fact idempotently without a
    /// pre-<see cref="ListAsync"/>+compare. The default (false) always inserts, keeping the "deliberate catalog"
    /// behavior. Dedup ignores <paramref name="enabled"/>/<paramref name="metadata"/> — only the identity matters —
    /// and does not mutate the matched row. On the SQL backends idempotence is a pre-insert identity check,
    /// not a unique index (<c>dedup: false</c> legitimately allows duplicates), so it is BEST-EFFORT under
    /// CONCURRENT writers of the same identity — the catalog is a low-write, deliberately-managed set, and a
    /// rare racing duplicate is benign (the next dedup add keeps returning the first row's id).</para></summary>
    Task<long> AddAsync(string kind, string content, bool enabled = true,
        string? taskKey = null, string? scope = null, bool dedup = false,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default);

    /// <summary>Update an entry in place — only the non-null arguments change, so passing just
    /// <paramref name="enabled"/> toggles it without touching the content. <paramref name="content"/>,
    /// <paramref name="enabled"/> and <paramref name="kind"/> use COALESCE semantics (null = leave unchanged);
    /// <paramref name="kind"/> RE-CATEGORISES in place (keeps the id + <c>created_at</c>). <paramref name="metadata"/>
    /// is null = leave unchanged, or a non-null map REPLACES the whole metadata set (an empty map clears it).
    /// <para><paramref name="taskKey"/>/<paramref name="scope"/> RE-SCOPE in place the same way <paramref name="kind"/>
    /// re-categorises (same id, same <c>created_at</c>) — so retargeting an entry at another consumer or variant
    /// is an edit, not delete+re-add. Null = leave unchanged, as everywhere else here; the EMPTY STRING is the
    /// CLEAR sentinel, resetting the field to null ("applies everywhere" — see <see cref="ForCompositionAsync"/>),
    /// because a plain null is already spoken for. That is this interface's established convention for a
    /// nullable-string field (the retired <c>source</c>/<c>title</c> params cleared on <c>""</c> too) and it
    /// mirrors the empty <paramref name="metadata"/> map; it destroys no legitimate value either, since an
    /// empty scope ALREADY reads as "every scope" in composition and an entry keyed to a task literally named
    /// <c>""</c> could never be composed. The sentinel is UPDATE-only — <see cref="AddAsync"/> stores what it
    /// is given, since there a plain null already means "everywhere".</para>
    /// <para>An identity-mutating update REFUSES a collision rather than creating one.
    /// <c>(kind, content, taskKey, scope)</c> is the dedup identity of <see cref="AddAsync"/>, so moving an entry
    /// onto an identity a DIFFERENT entry already holds would mint exactly the duplicate <c>dedup: true</c>
    /// promises not to create — silently. Such an update is refused: NOTHING is written (not even the
    /// non-identity fields carried on the same call) and it returns false. Merging the two entries was rejected —
    /// it would silently destroy one of them. The check runs only when the resulting identity actually DIFFERS
    /// from the entry's current one, so an <paramref name="enabled"/>/<paramref name="metadata"/>-only edit never
    /// refuses, a same-value re-set is a no-op rather than a self-collision, and the duplicates
    /// <c>dedup: false</c> legitimately allows stay editable. Like <see cref="AddAsync"/>'s dedup it is a
    /// pre-write identity check rather than a unique index, so it is BEST-EFFORT under CONCURRENT writers.</para>
    /// <para>Returns whether a row was updated — so false means "nothing changed" for EITHER reason: no such id,
    /// or a refused collision. <see cref="GetAsync"/> tells them apart.</para></summary>
    Task<bool> UpdateAsync(long id, string? content = null, bool? enabled = null, string? kind = null,
        string? taskKey = null, string? scope = null,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default);

    /// <summary>Delete an entry. Returns whether one was removed.</summary>
    Task<bool> RemoveAsync(long id, CancellationToken ct = default);

    Task<CuratedMemory?> GetAsync(long id, CancellationToken ct = default);

    /// <summary>List entries, optionally filtered by <paramref name="kind"/>, <paramref name="taskKey"/> and
    /// <paramref name="scope"/> (both STRICT equality — a null-task-key/null-scope row is NOT included; this is the
    /// admin/management filter, distinct from <see cref="ForCompositionAsync"/>'s applies-everywhere read
    /// semantics — the optimize/admin pass uses <paramref name="scope"/> to pull "all notes for ONE scope, incl.
    /// disabled"), <paramref name="enabledOnly"/>, and <paramref name="metadataMatch"/> (an entry matches when it
    /// has EVERY given key/value pair exactly — AND; null/empty = no metadata filter). Ordered by kind
    /// (byte-ordinal on every backend — Postgres orders <c>COLLATE "C"</c> to match SQLite/InMemory) then creation.</summary>
    Task<IReadOnlyList<CuratedMemory>> ListAsync(string? kind = null, bool enabledOnly = false,
        string? taskKey = null, string? scope = null, int? limit = null,
        IReadOnlyDictionary<string, string>? metadataMatch = null, CancellationToken ct = default);

    /// <summary>Keyword search over the catalog — the relevance READ path for a searchable curated UI or an
    /// agent recalling from the curated set (<see cref="ListAsync"/> is the enumeration path; this one NEEDS a
    /// <paramref name="query"/> — null/whitespace returns empty). Matches <see cref="CuratedMemory.Content"/>.
    /// The filters mirror <see cref="ListAsync"/>: strict-equality
    /// <paramref name="kind"/>/<paramref name="taskKey"/>/<paramref name="scope"/>, <paramref name="enabledOnly"/>
    /// default false (admin/catalog view — pass true for the recall path), the AND-of-pairs
    /// <paramref name="metadataMatch"/>; null <paramref name="limit"/> = no cap.
    /// <para>Matching is the same as <see cref="IMemoryStore.RecallAsync"/>: an entry whose content contains
    /// ANY ≥3-char term of the query (<see cref="SearchTerms"/> — words for a space-separated script,
    /// character trigrams for one written without spaces) as an ASCII-case-insensitive substring is found on
    /// every backend. Backend DIVERGENCE is RANKING only — SQLite ranks by bm25 through its FTS5-trigram
    /// index, Postgres (pg_trgm-accelerated ILIKE) and InMemory by matched-term count then recency. Fail-open
    /// like recall: storage faults degrade to an empty result, never a throw (only cancellation
    /// propagates).</para></summary>
    Task<IReadOnlyList<CuratedMemory>> SearchAsync(string query, string? kind = null, string? taskKey = null,
        string? scope = null, bool enabledOnly = false, int? limit = null,
        IReadOnlyDictionary<string, string>? metadataMatch = null, CancellationToken ct = default);

    /// <summary>The READ-for-prompt filter: enabled entries whose <see cref="CuratedMemory.TaskKey"/> matches
    /// <paramref name="taskKey"/> (or is null — a null-task-key row applies to every task) AND whose
    /// <see cref="CuratedMemory.Scope"/> is null/empty OR is in <paramref name="scopes"/>. Passing an EMPTY
    /// <paramref name="scopes"/> disables the scope filter (every scope of the task is returned). Ordered like
    /// <see cref="ListAsync"/> so <c>CuratedMemorySections.Compose</c> renders stable per-kind sections.
    /// Set <paramref name="enabledOnly"/> false to include disabled rows (admin preview).</summary>
    Task<IReadOnlyList<CuratedMemory>> ForCompositionAsync(string taskKey, IEnumerable<string> scopes,
        bool enabledOnly = true, CancellationToken ct = default);
}
