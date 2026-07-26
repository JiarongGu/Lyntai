using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Jobs;

/// <summary>The full <see cref="JobStoreContract"/> against <see cref="InMemoryJobStore"/> (every fact
/// inherited from <see cref="JobStoreContractFacts"/>).</summary>
public class InMemoryJobStoreTests : JobStoreContractFacts
{
    protected override (IJobStore, MutableClock) New()
    {
        var clock = new MutableClock();
        return (new InMemoryJobStore(clock.Get), clock);
    }
}
