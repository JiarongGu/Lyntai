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

    public static async Task Remove_deletes(ICuratedMemoryStore store)
    {
        var id = await store.AddAsync("k", "gone soon");
        Assert.True(await store.RemoveAsync(id));
        Assert.Null(await store.GetAsync(id));
        Assert.False(await store.RemoveAsync(id)); // already gone → false
    }
}
