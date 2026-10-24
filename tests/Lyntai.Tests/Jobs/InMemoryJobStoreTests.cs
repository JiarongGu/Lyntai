using Lyntai.Storage;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Jobs;

/// <summary>The full <see cref="JobStoreContract"/> against <see cref="InMemoryJobStore"/> (every fact
/// inherited from <see cref="JobStoreContractFacts"/>).</summary>
public class InMemoryJobStoreTests : JobStoreContractFacts
{
    protected override IJobStore New() => new InMemoryJobStore(Clock.Get);
}
