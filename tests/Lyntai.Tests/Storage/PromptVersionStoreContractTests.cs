using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>Every <see cref="PromptVersionStoreContract"/> method as a [Fact] — derive with a store
/// factory and the whole contract runs on that backend automatically (T11: no silent skips). Postgres
/// deliberately does NOT derive: it runs the Uid-namespaced subset on the shared container.</summary>
public abstract class PromptVersionStoreContractFacts
{
    protected abstract IPromptVersionStore NewStore();

    [Fact] public Task None_yet() => PromptVersionStoreContract.No_version_yet_returns_null_active_and_empty_history(NewStore(), "k");
    [Fact] public Task Monotonic() => PromptVersionStoreContract.Save_creates_monotonic_versions_and_the_latest_is_active(NewStore(), "k");
    [Fact] public Task History() => PromptVersionStoreContract.History_is_newest_first_with_exactly_one_active(NewStore(), "k");
    [Fact] public Task Rollback() => PromptVersionStoreContract.Rollback_reactivates_an_earlier_revision_without_rewriting_history(NewStore(), "k");
    [Fact] public Task Rollback_missing() => PromptVersionStoreContract.Rollback_to_a_missing_version_returns_null_and_changes_nothing(NewStore(), "k");
    [Fact] public Task Isolation() => PromptVersionStoreContract.Names_are_isolated(NewStore(), "k");
}

/// <summary>The <see cref="PromptVersionStoreContract"/> against the InMemory backend.</summary>
public class InMemoryPromptVersionStoreContractTests : PromptVersionStoreContractFacts
{
    protected override IPromptVersionStore NewStore() => new InMemoryPromptVersionStore();
}

/// <summary>The <see cref="PromptVersionStoreContract"/> against SQLite over a per-test temp db.</summary>
public class SqlitePromptVersionStoreContractTests : PromptVersionStoreContractFacts, IDisposable
{
    private readonly TempDb _db = new();
    protected override IPromptVersionStore NewStore() => new SqlitePromptVersionStore(_db.Factory);
    public void Dispose() => _db.Dispose();
}
