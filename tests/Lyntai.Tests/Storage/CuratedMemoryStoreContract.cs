using Lyntai.Storage;

namespace Lyntai.Tests.Storage;

/// <summary>Backend-agnostic <see cref="ICuratedMemoryStore"/> contract — run by the InMemory, SQLite,
/// and Postgres test classes so the curated-catalog CRUD + filter semantics are pinned identically.</summary>
public static class CuratedMemoryStoreContract
{
    private static Dictionary<string, string> Meta(params (string k, string v)[] pairs)
        => pairs.ToDictionary(p => p.k, p => p.v, StringComparer.Ordinal);

    /// <summary>Kind-parameterized so the Postgres leg runs on the shared container (a unique kind
    /// isolates it); the fresh InMemory/SQLite backends pass the default.</summary>
    public static async Task Add_get_list_round_trips(ICuratedMemoryStore store, string kind = "persona")
    {
        var id = await store.AddAsync(kind, "You are terse.", metadata: Meta(("source", "handbook")));

        var got = await store.GetAsync(id);
        Assert.NotNull(got);
        Assert.Equal(kind, got!.Kind);
        Assert.Equal("You are terse.", got.Content);
        Assert.Equal("handbook", got.Metadata?["source"]);
        Assert.True(got.Enabled);

        var all = await store.ListAsync();
        Assert.Contains(all, e => e.Id == id);
        Assert.Null(await store.GetAsync(-1)); // missing → null (ids are positive on every backend)
    }

    public static async Task Update_changes_only_the_provided_fields(ICuratedMemoryStore store)
    {
        var id = await store.AddAsync("style", "original");

        // toggle enabled only — content untouched
        Assert.True(await store.UpdateAsync(id, enabled: false));
        var after = await store.GetAsync(id);
        Assert.False(after!.Enabled);
        Assert.Equal("original", after.Content);

        // change just the content
        Assert.True(await store.UpdateAsync(id, content: "revised"));
        after = await store.GetAsync(id);
        Assert.Equal("revised", after!.Content);
        Assert.False(after.Enabled);      // still disabled (null = unchanged)

        Assert.False(await store.UpdateAsync(-1, content: "x")); // missing → false (ids are positive)
    }

    /// <summary>CMEM5 — <see cref="ICuratedMemoryStore.UpdateAsync"/>'s optional <c>kind</c> RE-CATEGORISES an
    /// entry between kinds IN PLACE — keeping its id, created_at, and metadata — instead of remove+re-add.
    /// Kind-parameterized so the Postgres shared container can run it isolated.</summary>
    public static async Task Update_can_recategorise_kind_in_place(ICuratedMemoryStore store,
        string fromKind = "inbox", string toKind = "glossary")
    {
        var id = await store.AddAsync(fromKind, "a fact worth keeping", metadata: Meta(("source", "import"), ("title", "the label")));
        var created = (await store.GetAsync(id))!.CreatedAt;

        // move it between kinds in place — id + created_at + the OTHER fields all survive
        Assert.True(await store.UpdateAsync(id, kind: toKind));
        var moved = await store.GetAsync(id);
        Assert.Equal(toKind, moved!.Kind);
        Assert.Equal("a fact worth keeping", moved.Content);   // untouched
        Assert.Equal("import", moved.Metadata?["source"]);     // metadata untouched (kind-only update)
        Assert.Equal("the label", moved.Metadata?["title"]);
        Assert.Equal(created, moved.CreatedAt);                // same row, not a new one

        // null kind = leave unchanged (COALESCE): a content-only edit keeps the new kind
        Assert.True(await store.UpdateAsync(id, content: "revised fact"));
        Assert.Equal(toKind, (await store.GetAsync(id))!.Kind);

        // and it's now discoverable under the new kind, not the old one
        Assert.Contains(id, (await store.ListAsync(kind: toKind)).Select(e => e.Id));
        Assert.DoesNotContain(id, (await store.ListAsync(kind: fromKind)).Select(e => e.Id));
    }

    /// <summary>CMEM7 — <see cref="ICuratedMemoryStore.UpdateAsync"/>'s optional <c>taskKey</c>/<c>scope</c>
    /// RE-SCOPE an entry IN PLACE (same id, same created_at), the half of "move it" that <c>kind</c> already
    /// had: null leaves the field alone, the EMPTY STRING clears it back to null ("applies everywhere").
    /// Task-parameterized so the Postgres shared container can run it isolated.</summary>
    public static async Task Update_can_rescope_task_and_scope_in_place(ICuratedMemoryStore store,
        string fromTask = "rescope-from", string toTask = "rescope-to")
    {
        // content is part of the dedup identity, so keep it unique per run too — this entry ends up at
        // (glossary, body, null, null), which a second run on the shared container would otherwise collide with
        var body = $"a term worth moving [{fromTask}]";
        var id = await store.AddAsync("glossary", body,
            taskKey: fromTask, scope: "lang:zh", metadata: Meta(("source", "import")));
        var created = (await store.GetAsync(id))!.CreatedAt;

        // retarget the task in place — id, created_at, content, metadata and the untouched scope all survive
        Assert.True(await store.UpdateAsync(id, taskKey: toTask));
        var moved = (await store.GetAsync(id))!;
        Assert.Equal(toTask, moved.TaskKey);
        Assert.Equal("lang:zh", moved.Scope);                  // null scope argument = unchanged
        Assert.Equal(body, moved.Content);
        Assert.Equal("import", moved.Metadata?["source"]);
        Assert.Equal(created, moved.CreatedAt);                // same row, not a new one

        // …and the strict-equality admin filter follows it
        Assert.Contains(id, (await store.ListAsync(taskKey: toTask)).Select(e => e.Id));
        Assert.DoesNotContain(id, (await store.ListAsync(taskKey: fromTask)).Select(e => e.Id));

        // the scope moves the same way, leaving the task alone
        Assert.True(await store.UpdateAsync(id, scope: "lang:ja"));
        moved = (await store.GetAsync(id))!;
        Assert.Equal("lang:ja", moved.Scope);
        Assert.Equal(toTask, moved.TaskKey);

        // null = leave unchanged (COALESCE), exactly like kind: a content-only edit keeps both
        Assert.True(await store.UpdateAsync(id, content: body + " (edited)"));
        moved = (await store.GetAsync(id))!;
        Assert.Equal(toTask, moved.TaskKey);
        Assert.Equal("lang:ja", moved.Scope);

        // the EMPTY STRING is the clear sentinel (null already means "leave unchanged"): scope → every scope
        Assert.True(await store.UpdateAsync(id, scope: ""));
        Assert.Null((await store.GetAsync(id))!.Scope);
        Assert.Contains(id, (await store.ForCompositionAsync(toTask, ["lang:de"])).Select(e => e.Id));

        // …and taskKey → every task, which ForCompositionAsync now honours from the OLD task too
        Assert.True(await store.UpdateAsync(id, taskKey: ""));
        Assert.Null((await store.GetAsync(id))!.TaskKey);
        Assert.Contains(id, (await store.ForCompositionAsync(fromTask, [])).Select(e => e.Id));
        Assert.DoesNotContain(id, (await store.ListAsync(taskKey: toTask)).Select(e => e.Id)); // strict admin filter
    }

    /// <summary>CMEM7 — the semantics that made re-scoping wait: <c>(kind, content, taskKey, scope)</c> is the
    /// dedup identity, so an update that would move an entry ONTO an identity another entry already holds is
    /// REFUSED (returns false, writes nothing) instead of silently minting the duplicate
    /// <c>AddAsync(dedup: true)</c> promises not to create. All FOUR identity fields are pinned — <c>kind</c>
    /// had the hole before <c>taskKey</c>/<c>scope</c> existed. Parameterized for the shared container.</summary>
    public static async Task Update_refuses_an_identity_collision(ICuratedMemoryStore store,
        string task = "collide", string kind = "confirmed", string otherKind = "pitfall")
    {
        var atZh = await store.AddAsync(kind, "the selector is .price", taskKey: task, scope: "site:zh");
        var atJa = await store.AddAsync(kind, "the selector is .price", taskKey: task, scope: "site:ja");

        // moving atJa onto atZh's identity would duplicate it → refused, and atJa is untouched
        Assert.False(await store.UpdateAsync(atJa, scope: "site:zh"));
        Assert.Equal("site:ja", (await store.GetAsync(atJa))!.Scope);
        Assert.Equal(2, (await store.ListAsync(kind: kind, taskKey: task)).Count);

        // the same for each of the other three identity fields
        var otherTask = task + "-b";
        var elsewhere = await store.AddAsync(kind, "the selector is .price", taskKey: otherTask, scope: "site:zh");
        Assert.False(await store.UpdateAsync(elsewhere, taskKey: task));           // taskKey collision
        Assert.Equal(otherTask, (await store.GetAsync(elsewhere))!.TaskKey);

        var miscategorised = await store.AddAsync(otherKind, "the selector is .price", taskKey: task, scope: "site:zh");
        Assert.False(await store.UpdateAsync(miscategorised, kind: kind));         // kind collision (the older hole)
        Assert.Equal(otherKind, (await store.GetAsync(miscategorised))!.Kind);

        var otherBody = await store.AddAsync(kind, "the selector is .sku", taskKey: task, scope: "site:zh");
        Assert.False(await store.UpdateAsync(otherBody, content: "the selector is .price")); // content collision
        Assert.Equal("the selector is .sku", (await store.GetAsync(otherBody))!.Content);

        // a refusal writes NOTHING — not even the non-identity fields carried on the same call
        Assert.False(await store.UpdateAsync(atJa, scope: "site:zh", enabled: false, metadata: Meta(("x", "1"))));
        var untouched = (await store.GetAsync(atJa))!;
        Assert.True(untouched.Enabled);
        Assert.Null(untouched.Metadata);

        // the promise the refusal protects: the identity still resolves to exactly ONE row under dedup
        Assert.Equal(atZh, await store.AddAsync(kind, "the selector is .price", taskKey: task, scope: "site:zh", dedup: true));

        // a move to a FREE identity is allowed — refusing is about collisions, not about re-scoping
        Assert.True(await store.UpdateAsync(atJa, scope: "site:de"));
        Assert.Equal("site:de", (await store.GetAsync(atJa))!.Scope);

        // the check fires only when the identity MOVES: a duplicate that dedup:false legitimately created stays
        // editable, and re-setting a field to the value it already has is a no-op, not a self-collision
        var twin = await store.AddAsync(kind, "the selector is .price", taskKey: task, scope: "site:zh");
        Assert.NotEqual(atZh, twin);
        Assert.True(await store.UpdateAsync(twin, enabled: false));
        Assert.False((await store.GetAsync(twin))!.Enabled);
        Assert.True(await store.UpdateAsync(twin, scope: "site:zh", content: "the selector is .price"));
    }

    /// <summary>Kind-parameterized for the shared Postgres container; the cross-kind enabled filter is
    /// asserted by CONTAINMENT (a table-wide count would see other tests' rows there).</summary>
    public static async Task List_filters_by_kind_and_enabled(ICuratedMemoryStore store,
        string kindA = "glossary", string kindB = "persona")
    {
        var a = await store.AddAsync(kindA, "term A", enabled: true);
        await store.AddAsync(kindA, "term B", enabled: false);
        var b = await store.AddAsync(kindB, "be kind", enabled: true);

        Assert.Equal(2, (await store.ListAsync(kind: kindA)).Count);
        var enabledA = await store.ListAsync(kind: kindA, enabledOnly: true);
        Assert.Single(enabledA);
        Assert.Equal(a, enabledA[0].Id);

        var enabledAll = await store.ListAsync(enabledOnly: true);
        var enabledIds = enabledAll.Select(e => e.Id).ToHashSet();
        Assert.Contains(a, enabledIds);  // the enabled filter spans kinds…
        Assert.Contains(b, enabledIds);
        Assert.DoesNotContain("term B", enabledAll.Where(e => e.Kind == kindA).Select(e => e.Content)); // …and drops disabled rows
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

    /// <summary>CMEM6 — arbitrary <c>string→string</c> <see cref="CuratedMemory.Metadata"/>: round-trips on add
    /// (incl. a value with quotes + CJK, exercising the JSON codec); a null map on update leaves it unchanged,
    /// a non-null map REPLACES the whole set, an empty map clears it; and it stays OUT of the dedup identity.</summary>
    public static async Task Metadata_round_trips_updates_and_clears(ICuratedMemoryStore store, string kind = "meta")
    {
        var withMeta = await store.AddAsync(kind, "body one", metadata: Meta(("author", "jo"), ("lang", "zh")));
        var noMeta   = await store.AddAsync(kind, "body two");

        var got = await store.GetAsync(withMeta);
        Assert.Equal("jo", got!.Metadata?["author"]);
        Assert.Equal("zh", got.Metadata?["lang"]);
        Assert.Null((await store.GetAsync(noMeta))!.Metadata);           // no metadata = null

        // a value with quotes + unicode round-trips through the JSON codec intact
        var tricky = await store.AddAsync(kind, "body three", metadata: Meta(("note", "say \"灵台\" — ok")));
        Assert.Equal("say \"灵台\" — ok", (await store.GetAsync(tricky))!.Metadata?["note"]);

        // a non-null map REPLACES the whole set (author dropped, lang changed, region added)
        Assert.True(await store.UpdateAsync(withMeta, metadata: Meta(("lang", "ja"), ("region", "jp"))));
        var replaced = await store.GetAsync(withMeta);
        Assert.False(replaced!.Metadata!.ContainsKey("author"));
        Assert.Equal("ja", replaced.Metadata["lang"]);
        Assert.Equal("jp", replaced.Metadata["region"]);
        Assert.Equal("body one", replaced.Content);                      // content untouched (metadata-only update)

        // null metadata = leave unchanged
        Assert.True(await store.UpdateAsync(withMeta, content: "body one edited"));
        Assert.Equal("ja", (await store.GetAsync(withMeta))!.Metadata?["lang"]);

        // an empty map clears metadata to null
        Assert.True(await store.UpdateAsync(withMeta, metadata: new Dictionary<string, string>()));
        Assert.Null((await store.GetAsync(withMeta))!.Metadata);

        // metadata is display/payload — OUT of the dedup identity (a different map still dedups to the same row)
        var f1 = await store.AddAsync("confirmed", "fact body", taskKey: kind, metadata: Meta(("a", "1")), dedup: true);
        var f2 = await store.AddAsync("confirmed", "fact body", taskKey: kind, metadata: Meta(("a", "2")), dedup: true);
        Assert.Equal(f1, f2);
        Assert.Equal("1", (await store.GetAsync(f1))!.Metadata?["a"]);   // dedup does not mutate the matched row
    }

    /// <summary>CMEM6 — the queryable side of metadata: <c>metadataMatch</c> on <see cref="ICuratedMemoryStore.ListAsync"/>
    /// and <see cref="ICuratedMemoryStore.SearchAsync"/> requires EVERY given key/value pair exactly (AND), composes
    /// with the other strict filters, and a null/empty map is no filter. Task-parameterized for the shared container.</summary>
    public static async Task Metadata_filter_matches_all_pairs(ICuratedMemoryStore store, string task = "mfilter")
    {
        var zhTerm = await store.AddAsync("glossary", "the encryption key alpha", taskKey: task, metadata: Meta(("lang", "zh"), ("role", "term")));
        var zhOnly = await store.AddAsync("glossary", "the encryption key beta",  taskKey: task, metadata: Meta(("lang", "zh")));
        var jaTerm = await store.AddAsync("glossary", "the encryption key gamma", taskKey: task, metadata: Meta(("lang", "ja"), ("role", "term")));
        await store.AddAsync("glossary", "the encryption key delta", taskKey: task);   // no metadata

        // ListAsync AND-of-pairs — only the entry carrying BOTH lang=zh AND role=term
        Assert.Equal([zhTerm], (await store.ListAsync(taskKey: task, metadataMatch: Meta(("lang", "zh"), ("role", "term")))).Select(e => e.Id));

        // a single pair matches every entry carrying it
        var zh = (await store.ListAsync(taskKey: task, metadataMatch: Meta(("lang", "zh")))).Select(e => e.Id).ToHashSet();
        Assert.Equal(new HashSet<long> { zhTerm, zhOnly }, zh);

        // a value mismatch → nothing
        Assert.Empty(await store.ListAsync(taskKey: task, metadataMatch: Meta(("lang", "de"))));

        // composes with SearchAsync (content query AND metadata filter)
        var searched = (await store.SearchAsync("encryption", taskKey: task, metadataMatch: Meta(("role", "term")))).Select(e => e.Id).ToHashSet();
        Assert.Equal(new HashSet<long> { zhTerm, jaTerm }, searched);

        // null/empty match = no metadata filter (all four rows of the task)
        Assert.Equal(4, (await store.ListAsync(taskKey: task)).Count);
        Assert.Equal(4, (await store.ListAsync(taskKey: task, metadataMatch: new Dictionary<string, string>())).Count);
    }

    /// <summary>CMEM4 (content-only after CMEM6) — keyword <see cref="ICuratedMemoryStore.SearchAsync"/> matches
    /// CONTENT, composes with the ListAsync-family strict filters (kind/taskKey/scope, enabledOnly default false),
    /// caps via limit, and returns empty on a whitespace or unmatched query. Sticks to single ≥3-char lowercase
    /// tokens — the portable cross-backend guarantee (FTS5-trigram / ILIKE / Contains).</summary>
    public static async Task Search_matches_content_with_filters(ICuratedMemoryStore store, string task = "search")
    {
        var byContent = await store.AddAsync("glossary", "the data encryption key wraps secrets", taskKey: task);
        var inNotes   = await store.AddAsync("notes", "rotate the encryption schedule quarterly", taskKey: task);
        var disabled  = await store.AddAsync("glossary", "legacy encryption note", enabled: false, taskKey: task);
        var scoped    = await store.AddAsync("glossary", "scoped encryption fact", taskKey: task, scope: "site:a");
        await store.AddAsync("glossary", "unrelated parsing fact", taskKey: task);
        await store.AddAsync("glossary", "encryption fact of another task", taskKey: task + "-other");

        // matches content; includes disabled by default (the admin/catalog family, like ListAsync);
        // strict task filter keeps other tasks out
        var hits = (await store.SearchAsync("encryption", taskKey: task)).Select(e => e.Id).ToHashSet();
        Assert.Equal(new HashSet<long> { byContent, inNotes, disabled, scoped }, hits);

        // enabledOnly composes
        var enabledHits = (await store.SearchAsync("encryption", taskKey: task, enabledOnly: true)).Select(e => e.Id).ToHashSet();
        Assert.Equal(new HashSet<long> { byContent, inNotes, scoped }, enabledHits);

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

    public static async Task Remove_deletes(ICuratedMemoryStore store)
    {
        var id = await store.AddAsync("k", "gone soon", metadata: Meta(("a", "1")));
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
