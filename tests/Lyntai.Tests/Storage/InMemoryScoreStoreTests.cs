using Lyntai.Storage;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Storage;

/// <summary>Every <see cref="ScoreStoreContract"/> fact as a theory case — derive with a store factory and the
/// whole contract runs on that backend (<see cref="StoreContractSuite{TStore}"/>).</summary>
public abstract class ScoreStoreContractFacts : StoreContractSuite<IScoreStore>
{
    protected abstract IScoreStore NewStore();
    protected sealed override IScoreStore CreateStore(string fact) => NewStore();

    [SkippableTheory, ContractFacts(typeof(ScoreStoreContract))]
    public Task Satisfies_the_contract(string fact) => RunAsync(typeof(ScoreStoreContract), fact);
}

/// <summary>The <see cref="ScoreStoreContract"/> against the InMemory backend.</summary>
public class InMemoryScoreStoreTests : ScoreStoreContractFacts
{
    protected override IScoreStore NewStore() => new InMemoryScoreStore();
}
