using Lyntai;
using Lyntai.Inference.Caching;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>Every <see cref="ResponseCacheContract"/> fact as a theory case — derive with a cache factory and
/// the whole contract runs on that backend (<see cref="StoreContractSuite{TStore}"/>). The cache must read the
/// suite's <see cref="StoreContractSuite{TStore}.Clock"/>, since two of the facts are about TIME passing.</summary>
public abstract class ResponseCacheContractFacts : StoreContractSuite<IResponseCache>
{
    protected abstract IResponseCache NewCache();
    protected sealed override IResponseCache CreateStore(string fact) => NewCache();

    [SkippableTheory, ContractFacts(typeof(ResponseCacheContract))]
    public Task Satisfies_the_contract(string fact) => RunAsync(typeof(ResponseCacheContract), fact);
}

/// <summary>The <see cref="ResponseCacheContract"/> against the in-process default.</summary>
public class InMemoryResponseCacheContractTests : ResponseCacheContractFacts
{
    protected override IResponseCache NewCache() => new InMemoryResponseCache(new LyntaiOptions(), Clock.Get);
}

/// <summary>The <see cref="ResponseCacheContract"/> against SQLite over a per-test temp db.</summary>
public class SqliteResponseCacheContractTests : ResponseCacheContractFacts, IDisposable
{
    private readonly TempDb _db = new();
    protected override IResponseCache NewCache() => new SqliteResponseCache(_db.Factory, new LyntaiOptions(), Clock.Get);
    public void Dispose() => _db.Dispose();
}
