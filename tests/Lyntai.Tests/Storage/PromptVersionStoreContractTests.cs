using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>Runs the <see cref="PromptVersionStoreContract"/> against the InMemory backend.</summary>
public class InMemoryPromptVersionStoreContractTests
{
    private static InMemoryPromptVersionStore New() => new();

    [Fact] public Task None_yet() => PromptVersionStoreContract.No_version_yet_returns_null_active_and_empty_history(New(), "k");
    [Fact] public Task Monotonic() => PromptVersionStoreContract.Save_creates_monotonic_versions_and_the_latest_is_active(New(), "k");
    [Fact] public Task History() => PromptVersionStoreContract.History_is_newest_first_with_exactly_one_active(New(), "k");
    [Fact] public Task Rollback() => PromptVersionStoreContract.Rollback_reactivates_an_earlier_revision_without_rewriting_history(New(), "k");
    [Fact] public Task Rollback_missing() => PromptVersionStoreContract.Rollback_to_a_missing_version_returns_null_and_changes_nothing(New(), "k");
    [Fact] public Task Isolation() => PromptVersionStoreContract.Names_are_isolated(New(), "k");
}

/// <summary>Runs the <see cref="PromptVersionStoreContract"/> against SQLite over a per-test temp db.</summary>
public class SqlitePromptVersionStoreContractTests : IDisposable
{
    private readonly TempDb _db = new();
    private SqlitePromptVersionStore Store => new(_db.Factory);

    public void Dispose() => _db.Dispose();

    [Fact] public Task None_yet() => PromptVersionStoreContract.No_version_yet_returns_null_active_and_empty_history(Store, "k");
    [Fact] public Task Monotonic() => PromptVersionStoreContract.Save_creates_monotonic_versions_and_the_latest_is_active(Store, "k");
    [Fact] public Task History() => PromptVersionStoreContract.History_is_newest_first_with_exactly_one_active(Store, "k");
    [Fact] public Task Rollback() => PromptVersionStoreContract.Rollback_reactivates_an_earlier_revision_without_rewriting_history(Store, "k");
    [Fact] public Task Rollback_missing() => PromptVersionStoreContract.Rollback_to_a_missing_version_returns_null_and_changes_nothing(Store, "k");
    [Fact] public Task Isolation() => PromptVersionStoreContract.Names_are_isolated(Store, "k");
}
