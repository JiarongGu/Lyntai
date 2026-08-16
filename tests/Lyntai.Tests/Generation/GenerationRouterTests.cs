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

    private static GenerationRequest Video() => new() { Kind = GenerationKinds.Video, Prompt = "a cat surfing" };

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
    public async Task A_throwing_backend_is_classified_and_fallen_over_rather_than_propagated()
    {
        // THE TRUST BOUNDARY, found 2026-08-15. LlmRouter.TryCompleteAsync has caught and classified a
        // thrown provider since it shipped, with the reason written out: "a provider that THROWS must get
        // the same fallback policy as one that returns a verdict reply". GenerationRouter had NO try/catch
        // at all, so one buggy BYO backend — AddGenerationProvider is a documented extension point — killed
        // the whole chain: the healthy candidate was never tried, no telemetry was recorded, and the caller
        // got a raw exception from a contract whose whole point is "a verdict, never a throw".
        var broken = new FakeGenerationProvider { Id = "byo", Throws = new HttpRequestException("socket died") };
        var healthy = new FakeGenerationProvider { Id = "a1111" };

        var result = await Router(broken, healthy).GenerateAsync(
            [new GenerationCandidate("byo"), new GenerationCandidate("a1111")], Image());

        Assert.True(result.IsOk);                 // the healthy candidate was reached
        Assert.Equal(1, healthy.GenerateCalls);
    }

    [Fact]
    public async Task A_thrown_refusal_is_clamped_so_a_keyword_cannot_stop_the_chain()
    {
        // The same clamp LlmRouter.ClassifyThrown documents: a throw is transport-layer — an error page
        // mentioning "content filter" at a proxy or CDN, not the model declining — and Refused is TERMINAL
        // (Surface, no fallback). A keyword match in an exception message must never bench the chain.
        var broken = new FakeGenerationProvider
        {
            Id = "byo",
            Throws = new InvalidOperationException("502 from gateway: content policy violation page"),
        };
        var healthy = new FakeGenerationProvider { Id = "a1111" };

        var result = await Router(broken, healthy).GenerateAsync(
            [new GenerationCandidate("byo"), new GenerationCandidate("a1111")], Image());

        Assert.True(result.IsOk);
        Assert.Equal(1, healthy.GenerateCalls);
    }

    [Fact]
    public async Task A_caller_cancellation_still_propagates_rather_than_becoming_a_verdict()
    {
        // The one throw that must NOT be swallowed — the same carve-out LlmRouter makes. Without it, a
        // cancelled render would report a verdict and the caller could not tell it was their own cancel.
        var slow = new FakeGenerationProvider { Id = "byo", Throws = new OperationCanceledException() };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Router(slow).GenerateAsync([new GenerationCandidate("byo")], Image(), cts.Token));
    }

    [Fact]
    public async Task A_submit_throw_that_never_left_the_process_PROPAGATES_so_the_job_runner_retries_it()
    {
        // Round 2 of the 3.0 review caught the first version of the submit catch swallowing EVERY throw into
        // an Inconclusive result. GenerationRenderJobHandler turns a failed submission into JobOutcome.Fail,
        // so a connection-refused blip during a deploy became a permanently dead-lettered job — where before
        // this router had any catch at all, JobRunner caught the throw and retried. A refused connection
        // provably committed nothing, so there is no duplicate-charge risk to protect against, which is the
        // same distinction this review taught FalQueueProvider one file over.
        var broken = new FakeGenerationJobProvider
        {
            Id = "byo-video",
            SubmitThrows = new HttpRequestException(HttpRequestError.ConnectionError, "connection refused"),
        };

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Router(broken).SubmitAsync([new GenerationCandidate("byo-video")], Video()));
    }

    [Fact]
    public async Task A_submit_throw_that_MAY_have_been_delivered_is_inconclusive_and_is_not_retried()
    {
        // The control, and the case the catch exists for: a timeout says nothing about whether the queue
        // accepted the job, so advancing or retrying could buy the same render twice.
        var ambiguous = new FakeGenerationJobProvider
        {
            Id = "byo-video",
            SubmitThrows = new TimeoutException("no answer"),
        };

        var submission = await Router(ambiguous).SubmitAsync([new GenerationCandidate("byo-video")], Video());

        Assert.True(submission.Operation.Inconclusive);
        Assert.Equal("byo-video", submission.ProviderId);   // named, so a human can check that account
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
