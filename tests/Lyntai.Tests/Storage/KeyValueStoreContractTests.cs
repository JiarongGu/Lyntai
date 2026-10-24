using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>Every <see cref="KeyValueStoreContract"/> fact as a theory case — derive with a store factory and
/// the WHOLE contract runs on that backend, including any fact added later (<see cref="StoreContractSuite{TStore}"/>).</summary>
public abstract class KeyValueStoreContractFacts : StoreContractSuite<IKeyValueStore>
{
    protected abstract IKeyValueStore NewStore();
    protected sealed override IKeyValueStore CreateStore(string fact) => NewStore();

    [SkippableTheory, ContractFacts(typeof(KeyValueStoreContract))]
    public Task Satisfies_the_contract(string fact) => RunAsync(typeof(KeyValueStoreContract), fact);
}

/// <summary>The <see cref="KeyValueStoreContract"/> against the InMemory backend.</summary>
public class InMemoryKeyValueStoreContractTests : KeyValueStoreContractFacts
{
    protected override IKeyValueStore NewStore() => new InMemoryKeyValueStore();
}

/// <summary>The <see cref="KeyValueStoreContract"/> against SQLite over a per-test temp db.</summary>
public class SqliteKeyValueStoreContractTests : KeyValueStoreContractFacts, IDisposable
{
    private readonly TempDb _db = new();
    protected override IKeyValueStore NewStore() => new SqliteKeyValueStore(_db.Factory);
    public void Dispose() => _db.Dispose();
}
