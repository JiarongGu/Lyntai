using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>Every <see cref="TraceStoreContract"/> fact as a theory case — derive with a store factory and the
/// whole contract runs on that backend (<see cref="StoreContractSuite{TStore}"/>).</summary>
public abstract class TraceStoreContractFacts : StoreContractSuite<ITraceStore>
{
    protected abstract ITraceStore NewStore();
    protected sealed override ITraceStore CreateStore(string fact) => NewStore();

    [SkippableTheory, ContractFacts(typeof(TraceStoreContract))]
    public Task Satisfies_the_contract(string fact) => RunAsync(typeof(TraceStoreContract), fact);
}

/// <summary>The <see cref="TraceStoreContract"/> against the InMemory backend.</summary>
public class InMemoryTraceStoreContractTests : TraceStoreContractFacts
{
    protected override ITraceStore NewStore() => new InMemoryTraceStore();
}

/// <summary>The <see cref="TraceStoreContract"/> against SQLite over a per-test temp db.</summary>
public class SqliteTraceStoreContractTests : TraceStoreContractFacts, IDisposable
{
    private readonly TempDb _db = new();
    protected override ITraceStore NewStore() => new SqliteTraceStore(_db.Factory);
    public void Dispose() => _db.Dispose();
}
