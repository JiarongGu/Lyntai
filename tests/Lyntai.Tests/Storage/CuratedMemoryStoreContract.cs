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
        var zh        = await store.AddAsync("glossary", "zh term",  task: t, scope: "lang:zh");
        var tGlobal   = await store.AddAsync("glossary", "any lang", task: t);                    // null scope
        var meta      = await store.AddAsync("rules",    "meta rule", task: m);
        var disabled  = await store.AddAsync("glossary", "off",       enabled: false, task: t, scope: "lang:zh");
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

        // ListAsync(task:) is a strict-equality admin filter — the null-task universal row is NOT included
        var listed = (await store.ListAsync(task: t)).Select(e => e.Id).ToHashSet();
        Assert.Contains(zh, listed);
        Assert.Contains(tGlobal, listed);
        Assert.Contains(disabled, listed);        // ListAsync doesn't drop disabled rows unless enabledOnly
        Assert.DoesNotContain(meta, listed);
        Assert.DoesNotContain(universal, listed);
    }
}
