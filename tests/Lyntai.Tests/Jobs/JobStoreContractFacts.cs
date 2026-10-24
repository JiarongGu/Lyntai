using Lyntai.Storage;
using Lyntai.Tests.Storage;

namespace Lyntai.Tests.Jobs;

/// <summary>Every <see cref="JobStoreContract"/> fact as a theory case — derive with a store factory over the
/// suite's <see cref="StoreContractSuite{TStore}.Clock"/> and the whole contract runs on that backend
/// (<see cref="StoreContractSuite{TStore}"/>).</summary>
public abstract class JobStoreContractFacts : StoreContractSuite<IJobStore>
{
    /// <summary>A store reading <see cref="StoreContractSuite{TStore}.Clock"/>, which the facts advance.</summary>
    protected abstract IJobStore New();
    protected sealed override IJobStore CreateStore(string fact) => New();

    [SkippableTheory, ContractFacts(typeof(JobStoreContract))]
    public Task Satisfies_the_contract(string fact) => RunAsync(typeof(JobStoreContract), fact);
}
