using Lyntai.Inference;

namespace Lyntai.Tests.Generation;

/// <summary><see cref="IModelProvider.IsAvailable"/> is part of the one provider seam, and the media router reads
/// it as the text and generic routers do: a backend reporting itself unavailable is never called, and does not
/// count toward the sole-candidate cooldown exemption.</summary>
public class MediaRouterAvailabilityTests
{
    private static readonly MediaRequest Image = new() { Kind = ProviderKinds.Image, Prompt = "a lighthouse" };

    [Fact]
    public async Task An_unavailable_backend_is_skipped_and_the_next_one_renders()
    {
        var down = new Renderer("down") { Available = false };
        var up = new Renderer("up");

        var result = await new MediaRouter([down, up]).GenerateAsync([new("down"), new("up")], Image);

        Assert.True(result.IsOk);
        Assert.Equal("up", result.ProviderId);
        Assert.Equal(0, down.Calls);
    }

    [Fact]
    public async Task An_unavailable_backend_does_not_withdraw_the_sole_candidate_exemption()
    {
        // "up" is the only AVAILABLE capable backend, so it is tried even while benched
        var tracker = new DeadHostTracker();
        tracker.MarkDead("generation::up");
        var down = new Renderer("down") { Available = false };
        var up = new Renderer("up");

        var result = await new MediaRouter([down, up], deadHosts: tracker).GenerateAsync([new("down"), new("up")], Image);

        Assert.True(result.IsOk);
        Assert.Equal(1, up.Calls);
    }

    private sealed class Renderer(string id) : IModelProvider
    {
        public string Id => id;

        public bool Available { get; init; } = true;

        public bool IsAvailable => Available;

        public int Calls { get; private set; }

        public ProviderCapabilities Capabilities { get; } = new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Image],
            Operations = [ProviderOperation.Complete],
        };

        public Task<MediaResponse> GenerateAsync(MediaRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(MediaResponse.Success([new MediaArtifact("image/png", Data: [0x89])]));
        }
    }
}
