using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>Every <see cref="PromptVersionStoreContract"/> fact as a theory case — derive with a store factory
/// and the whole contract runs on that backend (<see cref="StoreContractSuite{TStore}"/>).</summary>
public abstract class PromptVersionStoreContractFacts : StoreContractSuite<IPromptVersionStore>
{
    protected abstract IPromptVersionStore NewStore();
    protected sealed override IPromptVersionStore CreateStore(string fact) => NewStore();

    [SkippableTheory, ContractFacts(typeof(PromptVersionStoreContract))]
    public Task Satisfies_the_contract(string fact) => RunAsync(typeof(PromptVersionStoreContract), fact);
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
