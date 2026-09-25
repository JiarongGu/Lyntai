using Lyntai.Cortex;

namespace Lyntai.Storage.InMemory;

/// <summary>
/// The in-process curated catalog BOTH in-process <see cref="ICuratedMemoryStore"/>s run, so their semantics
/// are one copy — the same shape as <see cref="MemoryGraphState"/> (<c>docs/DECISIONS.md</c> D174). A
/// mutation is PLANNED without mutating (<see cref="PlanAdd"/>, <see cref="PlanUpdate"/>) and then
/// <see cref="Apply"/>-ed: the in-memory store applies at once, and the file store writes the entry first.
/// Not thread-safe — each store holds its own lock around every call.
/// </summary>
internal sealed class CuratedCatalog
{
    private readonly List<CuratedMemory> _entries = [];

    public long NextId { get; private set; } = 1;

    /// <summary>Start from entries already stored, never handing out an id at or below
    /// <paramref name="highWater"/>.</summary>
    public void Load(IEnumerable<CuratedMemory> entries, long highWater)
    {
        _entries.AddRange(entries);
        NextId = Math.Max(_entries.Select(e => e.Id).DefaultIfEmpty(0).Max(), highWater) + 1;
    }

    public CuratedMemory? Get(long id) => _entries.FirstOrDefault(e => e.Id == id);

    /// <summary>The entry an add writes — or, under <paramref name="dedup"/>, the existing one holding the
    /// same identity, which the caller returns without writing.</summary>
    public (CuratedMemory Entry, bool IsNew) PlanAdd(string kind, string content, bool enabled, string? taskKey,
        string? scope, bool dedup, IReadOnlyDictionary<string, string>? metadata, DateTimeOffset now)
    {
        // Metadata is display/payload: OUT of the identity, and the matched row is kept as it is.
        if (dedup && _entries.FirstOrDefault(e => e.Kind == kind && e.Content == content
                && e.TaskKey == taskKey && e.Scope == scope) is { } hit)
            return (hit, false);
        return (new CuratedMemory(NextId, kind, content, enabled, now, now, taskKey, scope, Copy(metadata)), true);
    }

    /// <summary>The entry an update writes, or null when there is no such id or the update would move the
    /// entry onto an identity ANOTHER entry holds — refused, writing nothing (the interface's contract).</summary>
    public CuratedMemory? PlanUpdate(long id, string? content, bool? enabled, string? kind, string? taskKey,
        string? scope, IReadOnlyDictionary<string, string>? metadata, DateTimeOffset now)
    {
        if (Get(id) is not { } e) return null;
        var newKind = kind ?? e.Kind;
        var newContent = content ?? e.Content;
        var newTask = CuratedMemoryUpdates.Rescope(taskKey, e.TaskKey);
        var newScope = CuratedMemoryUpdates.Rescope(scope, e.Scope);

        // only when the identity actually MOVES, so the duplicates dedup:false allows stay editable
        if ((newKind != e.Kind || newContent != e.Content || newTask != e.TaskKey || newScope != e.Scope)
            && _entries.Any(o => o.Id != id && o.Kind == newKind && o.Content == newContent
                                 && o.TaskKey == newTask && o.Scope == newScope))
            return null;

        return e with
        {
            Content = newContent,
            Enabled = enabled ?? e.Enabled,
            Kind = newKind,
            TaskKey = newTask,
            Scope = newScope,
            Metadata = metadata is null ? e.Metadata : Copy(metadata), // null = unchanged; non-null REPLACES
            UpdatedAt = now,
        };
    }

    /// <summary>Store a planned entry: replace the entry holding its id, or add it.</summary>
    public void Apply(CuratedMemory entry)
    {
        var i = _entries.FindIndex(e => e.Id == entry.Id);
        if (i >= 0) _entries[i] = entry;
        else _entries.Add(entry);
        NextId = Math.Max(NextId, entry.Id + 1);
    }

    public bool Remove(long id) => _entries.RemoveAll(e => e.Id == id) > 0;

    public IReadOnlyList<CuratedMemory> List(string? kind, bool enabledOnly, string? taskKey, string? scope,
        int? limit, IReadOnlyDictionary<string, string>? metadataMatch)
    {
        var q = Filter(_entries, kind, enabledOnly, taskKey, scope, metadataMatch)
            .OrderBy(e => e.Kind, StringComparer.Ordinal).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id);
        return [.. limit is { } n ? q.Take(n) : q];
    }

    public IReadOnlyList<CuratedMemory> Search(string query, string? kind, bool enabledOnly, string? taskKey,
        string? scope, int? limit, IReadOnlyDictionary<string, string>? metadataMatch)
    {
        var needle = query.Trim();
        var terms = SearchTerms.SubstringTerms(needle);
        // the matched-term count filters and then leads the order, as the SQL stores' LIKE count does
        var q = Filter(_entries, kind, enabledOnly, taskKey, scope, metadataMatch)
            .Select(e => (Entry: e, Count: SearchTerms.MatchCount(e.Content, terms, needle)))
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count).ThenByDescending(x => x.Entry.CreatedAt).ThenByDescending(x => x.Entry.Id)
            .Select(x => x.Entry);
        return [.. limit is { } n ? q.Take(n) : q];
    }

    public IReadOnlyList<CuratedMemory> ForComposition(string taskKey, IReadOnlyCollection<string> scopes, bool enabledOnly) =>
        [.. _entries
            .Where(e => !enabledOnly || e.Enabled)
            .Where(e => CuratedMemorySections.AppliesTo(e, taskKey, scopes))
            .OrderBy(e => e.Kind, StringComparer.Ordinal).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id)];

    // taskKey/scope are STRICT equality here — the admin filter, unlike ForComposition's applies-everywhere
    private static IEnumerable<CuratedMemory> Filter(IEnumerable<CuratedMemory> q, string? kind, bool enabledOnly,
        string? taskKey, string? scope, IReadOnlyDictionary<string, string>? metadataMatch) => q
        .Where(e => (kind is null || e.Kind == kind) && (taskKey is null || e.TaskKey == taskKey)
                    && (scope is null || e.Scope == scope) && (!enabledOnly || e.Enabled)
                    && Matches(e, metadataMatch));

    // an entry satisfies metadataMatch when it carries every requested pair exactly (AND); null/empty = no filter
    private static bool Matches(CuratedMemory e, IReadOnlyDictionary<string, string>? match) =>
        match is null || match.Count == 0
        || (e.Metadata is { } md && match.All(kv => md.TryGetValue(kv.Key, out var v) && v == kv.Value));

    // a defensive, ordinal copy, so a caller mutating its map cannot reach into the store; empty is null
    private static IReadOnlyDictionary<string, string>? Copy(IReadOnlyDictionary<string, string>? m) =>
        m is null || m.Count == 0 ? null : new Dictionary<string, string>(m, StringComparer.Ordinal);
}
