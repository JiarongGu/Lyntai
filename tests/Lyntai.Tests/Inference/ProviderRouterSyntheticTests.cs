using Lyntai.Inference;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Inference;

/// <summary>What the generic router answers when no backend answered at all. "Nothing serves this" is the
/// blameless <see cref="ProviderVerdict.NotConfigured"/>; "everything that serves this is benched" is a FAULT —
/// telling a caller to set something up while the backend they configured is down is exactly the masking the
/// blameless verdicts exist to prevent.</summary>
public class ProviderRouterSyntheticTests
{
    private static ProviderRouter<VectorRequest, VectorResponse> Router(DeadHostTracker tracker, params IModelProvider[] providers) =>
        new(providers, VectorResponse.Failure,
            c => c.Supports(ProviderKinds.Vector, ProviderOperation.Complete, accepts: ProviderKinds.Text),
            deadHosts: tracker);

    [Fact]
    public async Task Every_capable_backend_benched_is_a_fault_not_NotConfigured()
    {
        var tracker = new DeadHostTracker();
        tracker.MarkDead("a");
        tracker.MarkDead("b");

        var response = await Router(tracker, new FakeVectorProvider { Id = "a" }, new FakeVectorProvider { Id = "b" })
            .CallAsync(new VectorRequest(["hello"]));

        Assert.Equal(ProviderVerdict.Failed, response.Verdict);
        Assert.Contains("dead-host cooldown", response.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_capable_backend_at_all_is_still_NotConfigured()
    {
        var response = await Router(new DeadHostTracker()).CallAsync(new VectorRequest(["hello"]));

        Assert.Equal(ProviderVerdict.NotConfigured, response.Verdict);
    }
}
