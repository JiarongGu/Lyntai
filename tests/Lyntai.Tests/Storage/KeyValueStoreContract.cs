using Lyntai.Storage;

namespace Lyntai.Tests.Storage;

/// <summary>Backend-agnostic <see cref="IKeyValueStore"/> contract — run by the InMemory, SQLite, and
/// Postgres test classes so set/get/overwrite/delete/missing semantics are pinned identically. Every
/// method is scoped to a caller-supplied <paramref name="key"/> so it is safe on the shared Postgres
/// container (InMemory/SQLite get a fresh store per test and can pass a fixed literal).</summary>
public static class KeyValueStoreContract
{
    public static async Task Set_get_delete_round_trip(IKeyValueStore store, string key)
    {
        await store.SetAsync(key, "v1");
        Assert.Equal("v1", await store.GetAsync(key));

        await store.DeleteAsync(key);
        Assert.Null(await store.GetAsync(key));
    }

    public static async Task Missing_key_returns_null(IKeyValueStore store, string key)
    {
        Assert.Null(await store.GetAsync(key + "-never-set"));
    }

    public static async Task Overwrite_updates_the_value(IKeyValueStore store, string key)
    {
        await store.SetAsync(key, "old");
        Assert.Equal("old", await store.GetAsync(key));

        await store.SetAsync(key, "new"); // upsert
        Assert.Equal("new", await store.GetAsync(key));
    }

    public static async Task Cjk_value_round_trips(IKeyValueStore store, string key)
    {
        await store.SetAsync(key, "灵台平台"); // CJK must survive the round-trip on every backend
        Assert.Equal("灵台平台", await store.GetAsync(key));
    }

    public static async Task List_keys_filters_by_prefix_in_ordinal_order(IKeyValueStore store, string ns)
    {
        await store.SetAsync(ns + "/b", "1");
        await store.SetAsync(ns + "/a", "2");
        await store.SetAsync(ns + "/B", "3");
        await store.SetAsync("no-" + ns, "4"); // does NOT start with the prefix → excluded

        // ordinal (byte) order pins 'B' (66) BEFORE 'a' (97) — a culture/CI sort would interleave them
        Assert.Equal([ns + "/B", ns + "/a", ns + "/b"], await store.ListKeysAsync(ns));

        // the prefix match itself is case-SENSITIVE: exact-case narrows, wrong-case misses
        Assert.Equal([ns + "/b"], await store.ListKeysAsync(ns + "/b"));
        Assert.Empty(await store.ListKeysAsync(ns + "/A"));
    }

    public static async Task List_keys_treats_like_wildcards_as_literals(IKeyValueStore store, string ns)
    {
        await store.SetAsync(ns + "a_b", "1");
        await store.SetAsync(ns + "axb", "2"); // would match "a_b" if '_' leaked through as a LIKE wildcard

        Assert.Equal([ns + "a_b"], await store.ListKeysAsync(ns + "a_"));
        Assert.Empty(await store.ListKeysAsync(ns + "%")); // '%' is a literal char, not match-all
    }

    public static async Task List_keys_without_prefix_lists_all_keys(IKeyValueStore store, string ns)
    {
        await store.SetAsync(ns + "/1", "v");
        await store.SetAsync(ns + "/2", "v");
        var keys = await store.ListKeysAsync();
        // containment (not equality) — on a shared database other namespaces' keys coexist
        Assert.Contains(ns + "/1", keys);
        Assert.Contains(ns + "/2", keys);
    }
}
