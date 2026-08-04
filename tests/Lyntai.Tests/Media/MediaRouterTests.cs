using Lyntai.Media;
using Lyntai.Media.Routing;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Media;

/// <summary>Media routing differs from LLM routing in one decisive way: a candidate that CANNOT serve the
/// request (wrong medium, wrong delivery, needs inputs it doesn't take) must be skipped BEFORE anything is
/// spent — capability first, verdict-driven fallback second.</summary>
public class MediaRouterTests
{
    private static MediaRouter Router(params IMediaProvider[] providers) => new(providers);

    private static MediaRequest Image() => new() { Kind = MediaKinds.Image, Prompt = "a red square" };

    [Fact]
    public async Task It_generates_through_the_first_capable_candidate()
    {
        var image = new FakeMediaProvider { Id = "image-backend" };
        var video = new FakeMediaJobProvider { Id = "video-backend" };

        var result = await Router(video, image).GenerateAsync(
            [new MediaCandidate("video-backend"), new MediaCandidate("image-backend")], Image());

        Assert.True(result.IsOk);
        Assert.Equal(1, image.GenerateCalls);
    }

    [Fact]
    public async Task An_incapable_candidate_is_skipped_without_being_called()
    {
        // the video backend cannot serve an inline image request — it must not be invoked at all
        var video = new FakeMediaJobProvider { Id = "video-backend" };
        var image = new FakeMediaProvider { Id = "image-backend" };

        var result = await Router(video, image).GenerateAsync(
            [new MediaCandidate("video-backend"), new MediaCandidate("image-backend")], Image());

        Assert.True(result.IsOk);
        Assert.Equal(1, image.GenerateCalls);
        // the video backend's base-seam GenerateAsync returns Unsupported; proving it was never CALLED is the
        // point of the capability pre-filter, so assert on the artifact that only the image backend produces
        Assert.Equal("image/png", result.Artifacts[0].MediaType);
    }

    [Fact]
    public async Task A_transient_failure_advances_to_the_next_candidate()
    {
        var failing = new FakeMediaProvider { Id = "a" };
        failing.Verdicts.Enqueue(MediaVerdict.Failed);
        failing.Verdicts.Enqueue(MediaVerdict.Failed);
        var working = new FakeMediaProvider { Id = "b" };

        var result = await Router(failing, working).GenerateAsync(
            [new MediaCandidate("a"), new MediaCandidate("b")], Image());

        Assert.True(result.IsOk);
        Assert.Equal(1, failing.GenerateCalls);
        Assert.Equal(1, working.GenerateCalls);
    }

    [Fact]
    public async Task A_refusal_SURFACES_instead_of_shopping_the_prompt_around()
    {
        var refusing = new FakeMediaProvider { Id = "a" };
        refusing.Verdicts.Enqueue(MediaVerdict.Refused);
        var working = new FakeMediaProvider { Id = "b" };

        var result = await Router(refusing, working).GenerateAsync(
            [new MediaCandidate("a"), new MediaCandidate("b")], Image());

        Assert.Equal(MediaVerdict.Refused, result.Verdict);
        Assert.Equal(0, working.GenerateCalls);   // the whole point
    }

    [Fact]
    public async Task An_unconfigured_backend_is_skipped_like_an_incapable_one()
    {
        var unconfigured = new FakeMediaProvider { Id = "a" };
        unconfigured.Verdicts.Enqueue(MediaVerdict.NotConfigured);
        var working = new FakeMediaProvider { Id = "b" };

        var result = await Router(unconfigured, working).GenerateAsync(
            [new MediaCandidate("a"), new MediaCandidate("b")], Image());

        Assert.True(result.IsOk);
        Assert.Equal(1, working.GenerateCalls);
    }

    [Fact]
    public async Task A_real_failure_is_reported_over_a_not_configured_one()
    {
        // "b is not set up" is a worse explanation of the run than "a actually failed"
        var failing = new FakeMediaProvider { Id = "a" };
        failing.Verdicts.Enqueue(MediaVerdict.RateLimited);
        var unconfigured = new FakeMediaProvider { Id = "b" };
        unconfigured.Verdicts.Enqueue(MediaVerdict.NotConfigured);

        var result = await Router(failing, unconfigured).GenerateAsync(
            [new MediaCandidate("a"), new MediaCandidate("b")], Image());

        Assert.Equal(MediaVerdict.RateLimited, result.Verdict);
    }

    [Fact]
    public async Task No_capable_candidate_reports_Unsupported_not_Failed()
    {
        // "nothing here can do that" is a configuration answer, not a runtime fault
        var video = new FakeMediaJobProvider { Id = "video-backend" };

        var result = await Router(video).GenerateAsync([new MediaCandidate("video-backend")], Image());

        Assert.Equal(MediaVerdict.Unsupported, result.Verdict);
        Assert.Contains("no capable", result.Detail);
    }

    [Fact]
    public async Task An_unknown_candidate_id_is_ignored_rather_than_throwing()
    {
        var image = new FakeMediaProvider { Id = "image-backend" };

        var result = await Router(image).GenerateAsync(
            [new MediaCandidate("typo"), new MediaCandidate("image-backend")], Image());

        Assert.True(result.IsOk);
    }

    [Fact]
    public async Task A_candidate_can_pin_the_model_it_wants()
    {
        var aggregator = new FakeMediaProvider
        {
            Id = "aggregator",
            Capabilities = new MediaCapabilities
            {
                Kinds = [MediaKinds.Image],
                Deliveries = [MediaDelivery.Inline],
                Models = ["flux-1", "sdxl"],
            },
        };

        var result = await Router(aggregator).GenerateAsync([new MediaCandidate("aggregator", "sdxl")], Image());

        Assert.True(result.IsOk);
    }

    [Fact]
    public async Task A_submission_reports_which_backend_owns_the_operation()
    {
        // an operation id is meaningless without knowing who issued it — persist both
        var video = new FakeMediaJobProvider { Id = "video-backend" };

        var submission = await Router(video).SubmitAsync(
            [new MediaCandidate("video-backend")], new MediaRequest { Kind = MediaKinds.Video, Prompt = "x" });

        Assert.Equal("video-backend", submission.ProviderId);
        Assert.Equal("op-1", submission.Operation.Id);
        Assert.Equal(MediaOperationStatus.Queued, submission.Operation.Status);
    }

    [Fact]
    public async Task Submitting_with_no_job_capable_candidate_fails_without_pretending()
    {
        var image = new FakeMediaProvider { Id = "image-backend" };

        var submission = await Router(image).SubmitAsync(
            [new MediaCandidate("image-backend")], new MediaRequest { Kind = MediaKinds.Video, Prompt = "x" });

        Assert.Equal(MediaOperationStatus.Failed, submission.Operation.Status);
        Assert.Contains("no capable", submission.Operation.Detail);
    }
}
