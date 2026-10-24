using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>Every <see cref="CuratedMemoryStoreContract"/> fact as a theory case — derive with a store factory
/// and the whole contract runs on that backend (<see cref="StoreContractSuite{TStore}"/>).</summary>
public abstract class CuratedMemoryStoreContractFacts : StoreContractSuite<ICuratedMemoryStore>
{
    protected abstract ICuratedMemoryStore NewStore();
    protected sealed override ICuratedMemoryStore CreateStore(string fact) => NewStore();

    [SkippableTheory, ContractFacts(typeof(CuratedMemoryStoreContract))]
    public Task Satisfies_the_contract(string fact) => RunAsync(typeof(CuratedMemoryStoreContract), fact);
}

/// <summary>The <see cref="CuratedMemoryStoreContract"/> against the InMemory backend.</summary>
public class InMemoryCuratedMemoryStoreTests : CuratedMemoryStoreContractFacts
{
    protected override ICuratedMemoryStore NewStore() => new InMemoryCuratedMemoryStore();
}

/// <summary>The <see cref="CuratedMemoryStoreContract"/> against SQLite over a per-test temp db.</summary>
public class SqliteCuratedMemoryStoreTests : CuratedMemoryStoreContractFacts, IDisposable
{
    private readonly TempDb _db = new();
    protected override ICuratedMemoryStore NewStore() => new SqliteCuratedMemoryStore(_db.Factory);
    public void Dispose() => _db.Dispose();
}
