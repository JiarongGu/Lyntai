using Lyntai.Storage;

namespace Lyntai.Tests.Storage;

/// <summary>Backend-agnostic <see cref="ICuratedMemoryStore"/> contract — run by the InMemory, SQLite,
/// and Postgres test classes so the curated-catalog CRUD + filter semantics are pinned identically.</summary>
public static class CuratedMemoryStoreContract
{
    public static async Task Add_get_list_round_trips(ICuratedMemoryStore store)
    {
        var id = await store.AddAsync("persona", "You are terse.", source: "handbook");

        var got = await store.GetAsync(id);
        Assert.NotNull(got);
        Assert.Equal("persona", got!.Kind);
        Assert.Equal("You are terse.", got.Content);
        Assert.Equal("handbook", got.Source);
        Assert.True(got.Enabled);

        var all = await store.ListAsync();
        Assert.Contains(all, e => e.Id == id);
        Assert.Null(await store.GetAsync(id + 9999)); // missing → null
    }

    public static async Task Update_changes_only_the_provided_fields(ICuratedMemoryStore store)
    {
        var id = await store.AddAsync("style", "original", source: "src-a");

        // toggle enabled only — content + source untouched
        Assert.True(await store.UpdateAsync(id, enabled: false));
        var after = await store.GetAsync(id);
        Assert.False(after!.Enabled);
        Assert.Equal("original", after.Content);
        Assert.Equal("src-a", after.Source);

        // change just the content
        Assert.True(await store.UpdateAsync(id, content: "revised"));
        after = await store.GetAsync(id);
        Assert.Equal("revised", after!.Content);
        Assert.False(after.Enabled);      // still disabled (null = unchanged)
        Assert.Equal("src-a", after.Source);

        Assert.False(await store.UpdateAsync(id + 9999, content: "x")); // missing → false
    }

    public static async Task List_filters_by_kind_and_enabled(ICuratedMemoryStore store)
    {
        var a = await store.AddAsync("glossary", "term A", enabled: true);
        await store.AddAsync("glossary", "term B", enabled: false);
        await store.AddAsync("persona", "be kind", enabled: true);

        Assert.Equal(2, (await store.ListAsync(kind: "glossary")).Count);
        var enabledGlossary = await store.ListAsync(kind: "glossary", enabledOnly: true);
        Assert.Single(enabledGlossary);
        Assert.Equal(a, enabledGlossary[0].Id);

        Assert.Equal(2, (await store.ListAsync(enabledOnly: true)).Count); // term A + persona
    }

    /// <summary>CM1 — opt-in dedup on add: identical (kind, content, task, scope) returns the existing id
    /// (idempotent) instead of a second row; the default (dedup:false) keeps the deliberate-catalog behavior
    /// of always inserting. Parameterized task/scope so the Postgres shared container can run it isolated.</summary>
    public static async Task Dedup_add_is_idempotent(ICuratedMemoryStore store, string task = "source-study", string scope = "site:1")
    {
        var first = await store.AddAsync("confirmed", "the price selector is .price", taskKey: task, scope: scope, dedup: true);

        // identical (kind, content, task, scope) with dedup → the SAME id, no duplicate row
        var again = await store.AddAsync("confirmed", "the price selector is .price", taskKey: task, scope: scope, dedup: true);
        Assert.Equal(first, again);
        Assert.Single(await store.ListAsync(kind: "confirmed", taskKey: task, scope: scope));

        // dedup identity includes scope: a different scope is a NEW row even with dedup
        var otherScope = await store.AddAsync("confirmed", "the price selector is .price", taskKey: task, scope: scope + "-b", dedup: true);
        Assert.NotEqual(first, otherScope);

        // …and kind: a different kind is a NEW row even with identical content/task/scope
        var otherKind = await store.AddAsync("pitfall", "the price selector is .price", taskKey: task, scope: scope, dedup: true);
        Assert.NotEqual(first, otherKind);

        // without dedup (the default) an identical add always inserts — the catalog stays deliberate
        var dup = await store.AddAsync("confirmed", "the price selector is .price", taskKey: task, scope: scope);
        Assert.NotEqual(first, dup);
        Assert.Equal(2, (await store.ListAsync(kind: "confirmed", taskKey: task, scope: scope)).Count);
    }

    /// <summary>CM2 — <see cref="ICuratedMemoryStore.ListAsync"/> gains a strict-equality <c>scope</c> filter
    /// (the admin/optimize pass: "all notes for ONE scope, incl. disabled"). Null scope = no filter (unchanged).</summary>
    public static async Task List_filters_by_scope(ICuratedMemoryStore store, string task = "opt")
    {
        var zh      = await store.AddAsync("glossary", "zh term",  taskKey: task, scope: "site:zh");
        await store.AddAsync("glossary", "ja term",  taskKey: task, scope: "site:ja");
        await store.AddAsync("glossary", "any lang", taskKey: task);                                   // null scope
        var offZh   = await store.AddAsync("glossary", "off",      enabled: false, taskKey: task, scope: "site:zh");

        // scope filter is strict equality and independent of enabled: BOTH site:zh rows (incl. the disabled one)
        var zhIds = (await store.ListAsync(taskKey: task, scope: "site:zh")).Select(e => e.Id).ToHashSet();
        Assert.Equal(new HashSet<long> { zh, offZh }, zhIds);

        // strict equality: a specific scope filter does NOT pull in the null-scope ("applies everywhere") row
        var zhContents = (await store.ListAsync(taskKey: task, scope: "site:zh")).Select(e => e.Content).ToList();
        Assert.DoesNotContain("any lang", zhContents);
        Assert.DoesNotContain("ja term", zhContents);

        // enabledOnly composes with the scope filter
        Assert.Single(await store.ListAsync(taskKey: task, scope: "site:zh", enabledOnly: true));

        // null scope filter (default) → every scope of the task (4 rows)
        Assert.Equal(4, (await store.ListAsync(taskKey: task)).Count);
    }

    /// <summary>T10: the dedup identity is case-SENSITIVE on every backend (SQLite <c>IS</c>/BINARY,
    /// Postgres <c>IS NOT DISTINCT FROM</c>, InMemory ordinal <c>==</c>) — a casing variant of kind,
    /// content, or task is a DIFFERENT identity and inserts a new row even with dedup.</summary>
    public static async Task Dedup_identity_is_case_sensitive(ICuratedMemoryStore store, string task = "dedup-case")
    {
        var exact = await store.AddAsync("confirmed", "the selector is .price", taskKey: task, dedup: true);
        var contentCase = await store.AddAsync("confirmed", "The Selector is .price", taskKey: task, dedup: true);
        var kindCase = await store.AddAsync("Confirmed", "the selector is .price", taskKey: task, dedup: true);
        var taskCase = await store.AddAsync("confirmed", "the selector is .price", taskKey: task.ToUpperInvariant(), dedup: true);

        Assert.NotEqual(exact, contentCase); // content casing differs → new row
        Assert.NotEqual(exact, kindCase);    // kind casing differs → new row
        Assert.NotEqual(exact, taskCase);    // task casing differs → new row

        Assert.Equal(exact, await store.AddAsync("confirmed", "the selector is .price", taskKey: task, dedup: true));
    }

    /// <summary>T10: dedup under CONCURRENT writers of the same identity is BEST-EFFORT by contract (a
    /// rare racing duplicate row is benign) — pin what DOES hold: every racing add succeeds, the row count
    /// never exceeds the racer count, and dedup adds AFTER the race keep returning one stable id (the
    /// first row's, by the lowest-id tiebreak).</summary>
    public static async Task Dedup_add_race_settles_to_a_stable_id(ICuratedMemoryStore store, string task = "dedup-race")
    {
        var ids = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            store.AddAsync("confirmed", "raced fact", taskKey: task, dedup: true))));

        var a = await store.AddAsync("confirmed", "raced fact", taskKey: task, dedup: true);
        var b = await store.AddAsync("confirmed", "raced fact", taskKey: task, dedup: true);
        Assert.Equal(a, b);          // post-race dedup adds are stable...
        Assert.Equal(ids.Min(), a);  // ...and pinned to the FIRST row ever written

        var rows = await store.ListAsync(kind: "confirmed", taskKey: task);
        Assert.InRange(rows.Count, 1, ids.Length); // usually 1; the benign race bound is the racer count
    }

    /// <summary>CMEM3 — optional <c>Title</c>: a short label alongside the longer content (glossary term,
    /// persona trait, note title). Round-trips on add, updates with COALESCE semantics ("" clears, null
    /// leaves unchanged), and stays OUT of the dedup identity (display metadata, like source).</summary>
    public static async Task Title_round_trips_updates_and_clears(ICuratedMemoryStore store, string task = "titles")
    {
        var titled = await store.AddAsync("glossary", "data encryption key", title: "DEK", taskKey: task);
        var untitled = await store.AddAsync("glossary", "no label", taskKey: task);

        Assert.Equal("DEK", (await store.GetAsync(titled))!.Title);
        Assert.Null((await store.GetAsync(untitled))!.Title);   // default = untitled
        Assert.Equal("DEK", (await store.ListAsync(kind: "glossary", taskKey: task)).First(e => e.Id == titled).Title);

        // COALESCE update: title-only change leaves content untouched; content-only change keeps the title
        Assert.True(await store.UpdateAsync(titled, title: "DEK (key wrapping)"));
        var after = await store.GetAsync(titled);
        Assert.Equal("DEK (key wrapping)", after!.Title);
        Assert.Equal("data encryption key", after.Content);

        Assert.True(await store.UpdateAsync(titled, content: "data encryption key, wrapped by the KEK"));
        Assert.Equal("DEK (key wrapping)", (await store.GetAsync(titled))!.Title);

        // "" clears the title (null means "leave unchanged" — same convention as source)
        Assert.True(await store.UpdateAsync(titled, title: ""));
        Assert.Equal("", (await store.GetAsync(titled))!.Title);

        // dedup identity is (kind, content, task, scope) — a DIFFERENT title still dedups to the same row
        var first = await store.AddAsync("confirmed", "fact body", title: "label A", taskKey: task, dedup: true);
        var again = await store.AddAsync("confirmed", "fact body", title: "label B", taskKey: task, dedup: true);
        Assert.Equal(first, again);
        Assert.Equal("label A", (await store.GetAsync(first))!.Title); // dedup does not mutate the matched row
    }

    /// <summary>CMEM4 — keyword <see cref="ICuratedMemoryStore.SearchAsync"/>: matches CONTENT and TITLE,
    /// composes with the ListAsync-family strict filters (kind/taskKey/scope, enabledOnly default false),
    /// caps via limit, and returns empty on a whitespace or unmatched query. Sticks to single ≥3-char
    /// lowercase tokens — the portable cross-backend guarantee (FTS5-trigram / ILIKE / Contains).</summary>
    public static async Task Search_matches_content_and_title_with_filters(ICuratedMemoryStore store, string task = "search")
    {
        var byContent = await store.AddAsync("glossary", "the data encryption key wraps secrets", taskKey: task);
        var byTitle   = await store.AddAsync("notes", "rotate quarterly", title: "encryption schedule", taskKey: task);
        var disabled  = await store.AddAsync("glossary", "legacy encryption note", enabled: false, taskKey: task);
        var scoped    = await store.AddAsync("glossary", "scoped encryption fact", taskKey: task, scope: "site:a");
        await store.AddAsync("glossary", "unrelated parsing fact", taskKey: task);
        await store.AddAsync("glossary", "encryption fact of another task", taskKey: task + "-other");

        // matches content AND title; includes disabled by default (the admin/catalog family, like ListAsync);
        // strict task filter keeps other tasks out
        var hits = (await store.SearchAsync("encryption", taskKey: task)).Select(e => e.Id).ToHashSet();
        Assert.Equal(new HashSet<long> { byContent, byTitle, disabled, scoped }, hits);

        // enabledOnly composes
        var enabledHits = (await store.SearchAsync("encryption", taskKey: task, enabledOnly: true)).Select(e => e.Id).ToHashSet();
        Assert.Equal(new HashSet<long> { byContent, byTitle, scoped }, enabledHits);

        // kind narrows; scope is strict equality (does NOT pull in null-scope rows)
        var glossary = (await store.SearchAsync("encryption", kind: "glossary", taskKey: task)).Select(e => e.Id).ToHashSet();
        Assert.Equal(new HashSet<long> { byContent, disabled, scoped }, glossary);
        Assert.Equal([scoped], (await store.SearchAsync("encryption", taskKey: task, scope: "site:a")).Select(e => e.Id));

        // limit caps; whitespace query → empty (search NEEDS a query — ListAsync is the enumeration path);
        // unmatched query → empty
        Assert.Single(await store.SearchAsync("encryption", taskKey: task, limit: 1));
        Assert.Empty(await store.SearchAsync("   ", taskKey: task));
        Assert.Empty(await store.SearchAsync("zzz-not-there", taskKey: task));
    }

    /// <summary>CMEM4 — the CJK-substring recall the per-backend index machinery exists for: a ≥3-char
    /// token contained as a substring hits on every backend, and a 2-char CJK token still hits via each
    /// backend's substring fallback (a trigram index can't serve it; LIKE/ILIKE/Contains can).</summary>
    public static async Task Search_recalls_cjk_substrings(ICuratedMemoryStore store, string task = "search-cjk")
    {
        var id = await store.AddAsync("glossary", "灵台是数据加密密钥的管理平台", taskKey: task);

        Assert.Contains(id, (await store.SearchAsync("加密密钥", taskKey: task)).Select(e => e.Id));
        Assert.Contains(id, (await store.SearchAsync("平台", taskKey: task)).Select(e => e.Id));
    }

    public static async Task Update_with_empty_source_clears_it(ICuratedMemoryStore store)
    {
        var id = await store.AddAsync("k", "content", source: "original");
        Assert.Equal("original", (await store.GetAsync(id))!.Source);

        // "" clears the source (null would mean "leave unchanged")
        Assert.True(await store.UpdateAsync(id, source: ""));
        Assert.Equal("", (await store.GetAsync(id))!.Source);
    }

    public static async Task Remove_deletes(ICuratedMemoryStore store)
    {
        var id = await store.AddAsync("k", "gone soon");
        Assert.True(await store.RemoveAsync(id));
        Assert.Null(await store.GetAsync(id));
        Assert.False(await store.RemoveAsync(id)); // already gone → false
    }

    /// <summary>Assumes a fresh (or per-key-isolated) store — asserts absolute membership. The Postgres
    /// backend runs this over a uniquely-tasked variant instead (shared container).</summary>
    public static async Task ForComposition_filters_by_task_and_scope(ICuratedMemoryStore store, string t = "translation", string m = "metadata")
    {
        var zh        = await store.AddAsync("glossary", "zh term",  taskKey: t, scope: "lang:zh");
        var tGlobal   = await store.AddAsync("glossary", "any lang", taskKey: t);                    // null scope
        var meta      = await store.AddAsync("rules",    "meta rule", taskKey: m);
        var disabled  = await store.AddAsync("glossary", "off",       enabled: false, taskKey: t, scope: "lang:zh");
        var universal = await store.AddAsync("persona",  "be terse");                             // null task + null scope

        // task + matching scope → zh, tGlobal (null scope), universal (null task). Not metadata; not disabled.
        var forZh = (await store.ForCompositionAsync(t, ["lang:zh"])).Select(e => e.Id).ToHashSet();
        Assert.Contains(zh, forZh);
        Assert.Contains(tGlobal, forZh);
        Assert.Contains(universal, forZh);
        Assert.DoesNotContain(meta, forZh);
        Assert.DoesNotContain(disabled, forZh);       // disabled excluded by the default enabledOnly

        // empty scopes → scope filter disabled: every enabled row of the task + the universal row
        var forEmpty = (await store.ForCompositionAsync(t, [])).Select(e => e.Id).ToHashSet();
        Assert.Contains(zh, forEmpty);                // included via the empty-scopes rule despite its lang:zh scope
        Assert.Contains(tGlobal, forEmpty);
        Assert.Contains(universal, forEmpty);
        Assert.DoesNotContain(meta, forEmpty);

        // different scope → the lang:zh row drops out; null-scope + null-task rows remain
        var forJa = (await store.ForCompositionAsync(t, ["lang:ja"])).Select(e => e.Id).ToHashSet();
        Assert.DoesNotContain(zh, forJa);
        Assert.Contains(tGlobal, forJa);
        Assert.Contains(universal, forJa);

        // different task → only the universal (null-task) row crosses over
        var forMeta = (await store.ForCompositionAsync(m, [])).Select(e => e.Id).ToHashSet();
        Assert.Contains(meta, forMeta);
        Assert.Contains(universal, forMeta);
        Assert.DoesNotContain(zh, forMeta);
        Assert.DoesNotContain(tGlobal, forMeta);

        // enabledOnly:false surfaces the disabled row too
        var withDisabled = (await store.ForCompositionAsync(t, ["lang:zh"], enabledOnly: false)).Select(e => e.Id).ToHashSet();
        Assert.Contains(disabled, withDisabled);

        // ListAsync(taskKey:) is a strict-equality admin filter — the null-task-key universal row is NOT included
        var listed = (await store.ListAsync(taskKey: t)).Select(e => e.Id).ToHashSet();
        Assert.Contains(zh, listed);
        Assert.Contains(tGlobal, listed);
        Assert.Contains(disabled, listed);        // ListAsync doesn't drop disabled rows unless enabledOnly
        Assert.DoesNotContain(meta, listed);
        Assert.DoesNotContain(universal, listed);
    }
}
