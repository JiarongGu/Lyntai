using Lyntai.Inference;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>A backend that DECLARES queued delivery without implementing <see cref="IMediaJobProvider"/> cannot be
/// submitted to, so the queued capability filter drops it — and, like an unavailable backend, it does not count
/// toward the sole-candidate cooldown exemption.</summary>
public class MediaRouterQueuedCapabilityTests
{
    private static readonly MediaRequest Video = new() { Kind = ProviderKinds.Video, Prompt = "a wave" };

    [Fact]
    public async Task A_declared_queue_the_type_cannot_serve_does_not_withdraw_the_sole_candidate_exemption()
    {
        // "real" is the only backend that can take a submission, so it is tried even while benched
        var tracker = new DeadHostTracker();
        tracker.MarkDead("generation::real");
        var real = new FakeGenerationJobProvider { Id = "real" };

        var submission = await new MediaRouter([new DeclaresQueued(), real], deadHosts: tracker)
            .SubmitAsync([new("declares-only"), new("real")], Video);

        Assert.Equal("real", submission.ProviderId);
        Assert.Equal(1, real.SubmitCalls);
    }

    [Fact]
    public async Task A_declared_queue_the_type_cannot_serve_alone_is_a_capability_gap()
    {
        var submission = await new MediaRouter([new DeclaresQueued()]).SubmitAsync([new("declares-only")], Video);

        Assert.Equal("", submission.ProviderId);
        Assert.Equal(ProviderVerdict.Unsupported, submission.Operation.Verdict);
    }

    /// <summary>Says it queues video, and is only an <see cref="IModelProvider"/>.</summary>
    private sealed class DeclaresQueued : IModelProvider
    {
        public string Id => "declares-only";

        public ProviderCapabilities Capabilities { get; } = new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Video],
            Operations = [ProviderOperation.Queued],
        };

        public Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default) =>
            Task.FromResult(new ProviderProbeResult(true, "up"));

        public Task<MediaResponse> GenerateAsync(MediaRequest request, CancellationToken ct = default) =>
            Task.FromResult(MediaResponse.Failure(ProviderVerdict.Unsupported, "queued only"));
    }
}
