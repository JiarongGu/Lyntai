using Lyntai.Memory;
using Lyntai.Tests.Storage;

namespace Lyntai.Tests.Memory;

/// <summary>Every <see cref="VectorStoreContract"/> fact as a theory case — derive with a store factory and the
/// whole contract runs on that backend (<see cref="StoreContractSuite{TStore}"/>). Each fact namespaces its
/// own collection by the key it is handed.</summary>
public abstract class VectorStoreContractFacts : StoreContractSuite<IVectorStore>
{
    protected abstract IVectorStore New();
    protected sealed override IVectorStore CreateStore(string fact) => New();

    [SkippableTheory, ContractFacts(typeof(VectorStoreContract))]
    public Task Satisfies_the_contract(string fact) => RunAsync(typeof(VectorStoreContract), fact);
}

/// <summary>The <see cref="VectorStoreContract"/> against the in-process store.</summary>
public class InMemoryVectorStoreContractTests : VectorStoreContractFacts
{
    protected override IVectorStore New() => new InMemoryVectorStore();
}
