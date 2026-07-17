using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

public class PromptVersionStoreTests : IDisposable
{
    private readonly TempDb _db = new();
    private readonly SqlitePromptVersionStore _store;

    public PromptVersionStoreTests() => _store = new SqlitePromptVersionStore(_db.Factory);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task No_version_yet_returns_null_active()
    {
        Assert.Null(await _store.GetActiveAsync("greeting"));
        Assert.Empty(await _store.HistoryAsync("greeting"));
    }

    [Fact]
    public async Task Save_creates_monotonic_versions_and_the_latest_is_active()
    {
        var v1 = await _store.SaveAsync("greeting", "Hello {name}", author: "alice");
        var v2 = await _store.SaveAsync("greeting", "Hi {name}!", author: "bob");

        Assert.Equal(1, v1.Version);
        Assert.Equal(2, v2.Version);

        var active = await _store.GetActiveAsync("greeting");
        Assert.NotNull(active);
        Assert.Equal(2, active.Version);
        Assert.Equal("Hi {name}!", active.Template);
        Assert.Equal("bob", active.Author);
        Assert.True(active.IsActive);
    }

    [Fact]
    public async Task History_is_newest_first_with_exactly_one_active()
    {
        await _store.SaveAsync("p", "v1");
        await _store.SaveAsync("p", "v2");
        await _store.SaveAsync("p", "v3");

        var history = await _store.HistoryAsync("p");

        Assert.Equal([3, 2, 1], history.Select(h => h.Version));
        Assert.Single(history.Where(h => h.IsActive));
        Assert.Equal(3, history.Single(h => h.IsActive).Version);
    }

    [Fact]
    public async Task Rollback_reactivates_an_earlier_revision_without_rewriting_history()
    {
        await _store.SaveAsync("p", "v1 template");
        await _store.SaveAsync("p", "v2 template");

        var rolledBack = await _store.RollbackAsync("p", 1);

        Assert.NotNull(rolledBack);
        Assert.Equal(1, rolledBack.Version);
        Assert.True(rolledBack.IsActive);

        var active = await _store.GetActiveAsync("p");
        Assert.Equal(1, active!.Version);
        Assert.Equal("v1 template", active.Template);

        // history is preserved — both revisions still exist, v2 is just no longer active
        var history = await _store.HistoryAsync("p");
        Assert.Equal(2, history.Count);
        Assert.False(history.Single(h => h.Version == 2).IsActive);
    }

    [Fact]
    public async Task Rollback_to_a_missing_version_returns_null_and_changes_nothing()
    {
        await _store.SaveAsync("p", "only");

        var result = await _store.RollbackAsync("p", 99);

        Assert.Null(result);
        Assert.Equal(1, (await _store.GetActiveAsync("p"))!.Version); // untouched
    }

    [Fact]
    public async Task Names_are_isolated()
    {
        await _store.SaveAsync("a", "a-v1");
        await _store.SaveAsync("b", "b-v1");
        await _store.SaveAsync("b", "b-v2");

        Assert.Equal(1, (await _store.GetActiveAsync("a"))!.Version);
        Assert.Equal(2, (await _store.GetActiveAsync("b"))!.Version);
    }
}
