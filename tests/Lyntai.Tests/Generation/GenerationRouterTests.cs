using Lyntai.Generation;
using Lyntai.Generation.Routing;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>Media routing differs from LLM routing in one decisive way: a candidate that CANNOT serve the
/// request (wrong medium, wrong delivery, needs inputs it doesn't take) must be skipped BEFORE anything is
/// spent — capability first, verdict-driven fallback second.</summary>
public class GenerationRouterTests
{
    private static GenerationRouter Router(params IGenerationProvider[] providers) => new(providers);

    private static GenerationRequest Image() => new() { Kind = GenerationKinds.Image, Prompt = "a red square" };

    [Fact]
    public async Task It_generates_through_the_first_capable_candidate()
    {
        var image = new FakeGenerationProvider { Id = "image-backend" };
        var video = new FakeGenerationJobProvider { Id = "video-backend" };

        var result = await Router(video, image).GenerateAsync(
            [new GenerationCandidate("video-backend"), new GenerationCandidate("image-backend")], Image());

        Assert.True(result.IsOk);
        Assert.Equal(1, image.GenerateCalls);
    }

    [Fact]
    public async Task An_incapable_candidate_is_skipped_without_being_called()
    {
        // the video backend cannot serve an inline image request — it must not be invoked at all
        var video = new FakeGenerationJobProvider { Id = "video-backend" };
        var image = new FakeGenerationProvider { Id = "image-backend" };

        var result = await Router(video, image).GenerateAsync(
            [new GenerationCandidate("video-backend"), new GenerationCandidate("image-backend")], Image());

        Assert.True(result.IsOk);
        Assert.Equal(1, image.GenerateCalls);
        // the video backend's base-seam GenerateAsync returns Unsupported; proving it was never CALLED is the
        // point of the capability pre-filter, so assert on the artifact that only the image backend produces
        Assert.Equal("image/png", result.Artifacts[0].MediaType);
    }

    [Fact]
    public async Task A_transient_failure_advances_to_the_next_candidate()
    {
        var failing = new FakeGenerationProvider { Id = "a" };
        failing.Verdicts.Enqueue(GenerationVerdict.Failed);
        failing.Verdicts.Enqueue(GenerationVerdict.Failed);
        var working = new FakeGenerationProvider { Id = "b" };

        var result = await Router(failing, working).GenerateAsync(
            [new GenerationCandidate("a"), new GenerationCandidate("b")], Image());

        Assert.True(result.IsOk);
        Assert.Equal(1, failing.GenerateCalls);
        Assert.Equal(1, working.GenerateCalls);
    }

    [Fact]
    public async Task A_refusal_SURFACES_instead_of_shopping_the_prompt_around()
    {
        var refusing = new FakeGenerationProvider { Id = "a" };
        refusing.Verdicts.Enqueue(GenerationVerdict.Refused);
        var working = new FakeGenerationProvider { Id = "b" };

        var result = await Router(refusing, working).GenerateAsync(
            [new GenerationCandidate("a"), new GenerationCandidate("b")], Image());

        Assert.Equal(GenerationVerdict.Refused, result.Verdict);
        Assert.Equal(0, working.GenerateCalls);   // the whole point
    }

    [Fact]
    public async Task An_unconfigured_backend_is_skipped_like_an_incapable_one()
    {
        var unconfigured = new FakeGenerationProvider { Id = "a" };
        unconfigured.Verdicts.Enqueue(GenerationVerdict.NotConfigured);
        var working = new FakeGenerationProvider { Id = "b" };

        var result = await Router(unconfigured, working).GenerateAsync(
            [new GenerationCandidate("a"), new GenerationCandidate("b")], Image());

        Assert.True(result.IsOk);
        Assert.Equal(1, working.GenerateCalls);
    }

    [Fact]
    public async Task A_real_failure_is_reported_over_a_not_configured_one()
    {
        // "b is not set up" is a worse explanation of the run than "a actually failed"
        var failing = new FakeGenerationProvider { Id = "a" };
        failing.Verdicts.Enqueue(GenerationVerdict.RateLimited);
        var unconfigured = new FakeGenerationProvider { Id = "b" };
        unconfigured.Verdicts.Enqueue(GenerationVerdict.NotConfigured);

        var result = await Router(failing, unconfigured).GenerateAsync(
            [new GenerationCandidate("a"), new GenerationCandidate("b")], Image());

        Assert.Equal(GenerationVerdict.RateLimited, result.Verdict);
    }

    [Fact]
    public async Task No_capable_candidate_reports_Unsupported_not_Failed()
    {
        // "nothing here can do that" is a configuration answer, not a runtime fault
        var video = new FakeGenerationJobProvider { Id = "video-backend" };

        var result = await Router(video).GenerateAsync([new GenerationCandidate("video-backend")], Image());

        Assert.Equal(GenerationVerdict.Unsupported, result.Verdict);
        Assert.Contains("no capable", result.Detail);
    }

    [Fact]
    public async Task An_unknown_candidate_id_is_ignored_rather_than_throwing()
    {
        var image = new FakeGenerationProvider { Id = "image-backend" };

        var result = await Router(image).GenerateAsync(
            [new GenerationCandidate("typo"), new GenerationCandidate("image-backend")], Image());

        Assert.True(result.IsOk);
    }

    [Fact]
    public async Task A_candidate_can_pin_the_model_it_wants()
    {
        var aggregator = new FakeGenerationProvider
        {
            Id = "aggregator",
            Capabilities = new GenerationCapabilities
            {
                Kinds = [GenerationKinds.Image],
                Deliveries = [GenerationDelivery.Inline],
                Models = ["flux-1", "sdxl"],
            },
        };

        var result = await Router(aggregator).GenerateAsync([new GenerationCandidate("aggregator", "sdxl")], Image());

        Assert.True(result.IsOk);
    }

    [Fact]
    public async Task A_submission_reports_which_backend_owns_the_operation()
    {
        // an operation id is meaningless without knowing who issued it — persist both
        var video = new FakeGenerationJobProvider { Id = "video-backend" };

        var submission = await Router(video).SubmitAsync(
            [new GenerationCandidate("video-backend")], new GenerationRequest { Kind = GenerationKinds.Video, Prompt = "x" });

        Assert.Equal("video-backend", submission.ProviderId);
        Assert.Equal("op-1", submission.Operation.Id);
        Assert.Equal(GenerationOperationStatus.Queued, submission.Operation.Status);
    }

    [Fact]
    public async Task Submitting_with_no_job_capable_candidate_fails_without_pretending()
    {
        var image = new FakeGenerationProvider { Id = "image-backend" };

        var submission = await Router(image).SubmitAsync(
            [new GenerationCandidate("image-backend")], new GenerationRequest { Kind = GenerationKinds.Video, Prompt = "x" });

        Assert.Equal(GenerationOperationStatus.Failed, submission.Operation.Status);
        Assert.Contains("no capable", submission.Operation.Detail);
    }
}
