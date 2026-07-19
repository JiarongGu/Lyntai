using Lyntai.Storage;

namespace Lyntai.Tests.Storage;

/// <summary>Backend-agnostic <see cref="IPromptVersionStore"/> contract — run by the InMemory, SQLite, and
/// Postgres test classes so monotonic versioning, active pointer, newest-first history, rollback (without
/// rewriting history) and name isolation are pinned identically. Prompt names are namespaced by a
/// caller-supplied <paramref name="key"/> so the methods are safe on the shared Postgres container.</summary>
public static class PromptVersionStoreContract
{
    public static async Task No_version_yet_returns_null_active_and_empty_history(IPromptVersionStore store, string key)
    {
        Assert.Null(await store.GetActiveAsync(key + "-greeting"));
        Assert.Empty(await store.HistoryAsync(key + "-greeting"));
    }

    public static async Task Save_creates_monotonic_versions_and_the_latest_is_active(IPromptVersionStore store, string key)
    {
        var name = key + "-greeting";
        var v1 = await store.SaveAsync(name, "Hello {name}", author: "alice");
        var v2 = await store.SaveAsync(name, "Hi {name}!", author: "bob");

        Assert.Equal(1, v1.Version);
        Assert.Equal(2, v2.Version);

        var active = await store.GetActiveAsync(name);
        Assert.NotNull(active);
        Assert.Equal(2, active!.Version);
        Assert.Equal("Hi {name}!", active.Template);
        Assert.Equal("bob", active.Author);
        Assert.True(active.IsActive);
    }

    public static async Task History_is_newest_first_with_exactly_one_active(IPromptVersionStore store, string key)
    {
        var name = key + "-p";
        await store.SaveAsync(name, "v1");
        await store.SaveAsync(name, "v2");
        await store.SaveAsync(name, "v3");

        var history = await store.HistoryAsync(name);
        Assert.Equal([3, 2, 1], history.Select(h => h.Version)); // newest version first
        Assert.Single(history, h => h.IsActive);
        Assert.Equal(3, history.Single(h => h.IsActive).Version);
    }

    public static async Task Rollback_reactivates_an_earlier_revision_without_rewriting_history(IPromptVersionStore store, string key)
    {
        var name = key + "-p";
        await store.SaveAsync(name, "v1 template");
        await store.SaveAsync(name, "v2 template");

        var rolledBack = await store.RollbackAsync(name, 1);
        Assert.NotNull(rolledBack);
        Assert.Equal(1, rolledBack!.Version);
        Assert.True(rolledBack.IsActive);

        var active = await store.GetActiveAsync(name);
        Assert.Equal(1, active!.Version);
        Assert.Equal("v1 template", active.Template);

        // history preserved — both revisions still exist, v2 just no longer active
        var history = await store.HistoryAsync(name);
        Assert.Equal(2, history.Count);
        Assert.False(history.Single(h => h.Version == 2).IsActive);
    }

    public static async Task Rollback_to_a_missing_version_returns_null_and_changes_nothing(IPromptVersionStore store, string key)
    {
        var name = key + "-p";
        await store.SaveAsync(name, "only");

        Assert.Null(await store.RollbackAsync(name, 99));
        Assert.Equal(1, (await store.GetActiveAsync(name))!.Version); // untouched
    }

    public static async Task Names_are_isolated(IPromptVersionStore store, string key)
    {
        var a = key + "-a";
        var b = key + "-b";
        await store.SaveAsync(a, "a-v1");
        await store.SaveAsync(b, "b-v1");
        await store.SaveAsync(b, "b-v2");

        Assert.Equal(1, (await store.GetActiveAsync(a))!.Version);
        Assert.Equal(2, (await store.GetActiveAsync(b))!.Version);
    }
}
