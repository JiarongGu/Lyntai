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
}
