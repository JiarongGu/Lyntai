using Lyntai.Inference;
using Lyntai.Inference.Budgeting;
using Lyntai.Inference.RateLimiting;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>Media routing differs from LLM routing in one decisive way: a candidate that CANNOT serve the
/// request (wrong medium, wrong delivery, needs inputs it doesn't take) must be skipped BEFORE anything is
/// spent — capability first, verdict-driven fallback second.</summary>
public class MediaRouterTests
{
    private static MediaRouter Router(params IModelProvider[] providers) => new(providers);

    private static MediaRequest Image() => new() { Kind = ProviderKinds.Image, Prompt = "a red square" };

    private static MediaRequest Video() => new() { Kind = ProviderKinds.Video, Prompt = "a cat surfing" };

    [Fact]
    public async Task A_throwing_backend_is_classified_and_fallen_over_rather_than_propagated()
    {
        // THE TRUST BOUNDARY, as on the text side: a provider that THROWS gets the same fallback policy as one
        // that returns a verdict. AddProvider is a documented extension point, so without the catch one buggy
        // BYO backend kills the whole chain — the healthy candidate never tried, no telemetry, and a raw
        // exception from a contract whose whole point is "a verdict, never a throw".
        var broken = new FakeGenerationProvider { Id = "byo", Throws = new HttpRequestException("socket died") };
        var healthy = new FakeGenerationProvider { Id = "a1111" };

        var result = await Router(broken, healthy).GenerateAsync(
            [new ProviderCandidate("byo"), new ProviderCandidate("a1111")], Image());

        Assert.True(result.IsOk);                 // the healthy candidate was reached
        Assert.Equal(1, healthy.GenerateCalls);
    }

    [Fact]
    public async Task A_thrown_refusal_is_clamped_so_a_keyword_cannot_stop_the_chain()
    {
        // The same clamp TextRouter.ClassifyThrown documents: a throw is transport-layer — an error page
        // mentioning "content filter" at a proxy or CDN, not the model declining — and Refused is TERMINAL
        // (Surface, no fallback). A keyword match in an exception message must never bench the chain.
        var broken = new FakeGenerationProvider
        {
            Id = "byo",
            Throws = new InvalidOperationException("502 from gateway: content policy violation page"),
        };
        var healthy = new FakeGenerationProvider { Id = "a1111" };

        var result = await Router(broken, healthy).GenerateAsync(
            [new ProviderCandidate("byo"), new ProviderCandidate("a1111")], Image());

        Assert.True(result.IsOk);
        Assert.Equal(1, healthy.GenerateCalls);
    }

    [Fact]
    public async Task A_caller_cancellation_still_propagates_rather_than_becoming_a_verdict()
    {
        // The one throw that must NOT be swallowed — the same carve-out TextRouter makes. Without it, a
        // cancelled render would report a verdict and the caller could not tell it was their own cancel.
        var slow = new FakeGenerationProvider { Id = "byo", Throws = new OperationCanceledException() };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Router(slow).GenerateAsync([new ProviderCandidate("byo")], Image(), cts.Token));
    }

    [Fact]
    public async Task A_submit_throw_that_never_left_the_process_PROPAGATES_so_the_job_runner_retries_it()
    {
        // The submit catch must not swallow EVERY throw into an Inconclusive result: GenerationRenderJobHandler
        // turns a failed submission into JobOutcome.Fail, so a connection-refused blip during a deploy would
        // dead-letter the job for good, where a propagated throw is retried by JobRunner. A refused connection
        // provably committed nothing, so there is no duplicate-charge risk to protect against (FalProvider
        // draws the same line).
        var broken = new FakeGenerationJobProvider
        {
            Id = "byo-video",
            SubmitThrows = new HttpRequestException(HttpRequestError.ConnectionError, "connection refused"),
        };

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Router(broken).SubmitAsync([new ProviderCandidate("byo-video")], Video()));
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

        var submission = await Router(ambiguous).SubmitAsync([new ProviderCandidate("byo-video")], Video());

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
            [new ProviderCandidate("video-backend"), new ProviderCandidate("image-backend")], Image());

        Assert.True(result.IsOk);
        Assert.Equal(1, image.GenerateCalls);
        // the video backend's base-seam GenerateAsync returns Unsupported; proving it was never CALLED is the
        // point of the capability pre-filter, so assert on the artifact that only the image backend produces
        Assert.Equal("image/png", result.Artifacts[0].MediaType);
    }

    [Fact]
    public async Task A_request_carrying_INPUTS_skips_a_backend_that_cannot_read_them()
    {
        // The router is where a domain REQUEST becomes a generic capability query (D125), and
        // `request.Inputs.Count > 0` is the one part of that mapping no other test exercises through the
        // router. It matters because a backend that accepts the call and ignores the inputs returns a
        // plausible, WRONG artifact — the defect found in ComfyUiProvider (docs/task-archive.md Part 125).
        var blind = new FakeGenerationProvider
        {
            Id = "no-inputs",
            Capabilities = new ProviderCapabilities
            {
                Accepts = [ProviderKinds.Text],
                Produces = [ProviderKinds.Image],
                Operations = [ProviderOperation.Complete],
                SupportsInputs = false,
            },
        };

        var result = await Router(blind).GenerateAsync(
            [new ProviderCandidate("no-inputs")],
            Image() with { Inputs = [MediaInput.Init(new byte[] { 1, 2, 3 }, "image/png")] });

        Assert.False(result.IsOk);
        Assert.Equal(0, blind.GenerateCalls);
    }

    [Fact]
    public async Task A_transient_failure_advances_to_the_next_candidate()
    {
        var failing = new FakeGenerationProvider { Id = "a" };
        failing.Verdicts.Enqueue(ProviderVerdict.Failed);
        failing.Verdicts.Enqueue(ProviderVerdict.Failed);
        var working = new FakeGenerationProvider { Id = "b" };

        var result = await Router(failing, working).GenerateAsync(
            [new ProviderCandidate("a"), new ProviderCandidate("b")], Image());

        Assert.True(result.IsOk);
        Assert.Equal(1, failing.GenerateCalls);
        Assert.Equal(1, working.GenerateCalls);
    }

    [Fact]
    public async Task A_refusal_SURFACES_instead_of_shopping_the_prompt_around()
    {
        var refusing = new FakeGenerationProvider { Id = "a" };
        refusing.Verdicts.Enqueue(ProviderVerdict.Refused);
        var working = new FakeGenerationProvider { Id = "b" };

        var result = await Router(refusing, working).GenerateAsync(
            [new ProviderCandidate("a"), new ProviderCandidate("b")], Image());

        Assert.Equal(ProviderVerdict.Refused, result.Verdict);
        Assert.Equal(0, working.GenerateCalls);   // the whole point
    }

    [Fact]
    public async Task An_unconfigured_backend_is_skipped_like_an_incapable_one()
    {
        var unconfigured = new FakeGenerationProvider { Id = "a" };
        unconfigured.Verdicts.Enqueue(ProviderVerdict.NotConfigured);
        var working = new FakeGenerationProvider { Id = "b" };

        var result = await Router(unconfigured, working).GenerateAsync(
            [new ProviderCandidate("a"), new ProviderCandidate("b")], Image());

        Assert.True(result.IsOk);
        Assert.Equal(1, working.GenerateCalls);
    }

    [Fact]
    public async Task A_real_failure_is_reported_over_a_not_configured_one()
    {
        // "b is not set up" is a worse explanation of the run than "a actually failed"
        var failing = new FakeGenerationProvider { Id = "a" };
        failing.Verdicts.Enqueue(ProviderVerdict.RateLimited);
        var unconfigured = new FakeGenerationProvider { Id = "b" };
        unconfigured.Verdicts.Enqueue(ProviderVerdict.NotConfigured);

        var result = await Router(failing, unconfigured).GenerateAsync(
            [new ProviderCandidate("a"), new ProviderCandidate("b")], Image());

        Assert.Equal(ProviderVerdict.RateLimited, result.Verdict);
    }

    [Fact]
    public async Task No_capable_candidate_reports_Unsupported_not_Failed()
    {
        // "nothing here can do that" is a configuration answer, not a runtime fault
        var video = new FakeGenerationJobProvider { Id = "video-backend" };

        var result = await Router(video).GenerateAsync([new ProviderCandidate("video-backend")], Image());

        Assert.Equal(ProviderVerdict.Unsupported, result.Verdict);
        Assert.Contains("no capable", result.Detail);
    }

    [Fact]
    public async Task An_unknown_candidate_id_is_ignored_rather_than_throwing()
    {
        var image = new FakeGenerationProvider { Id = "image-backend" };

        var result = await Router(image).GenerateAsync(
            [new ProviderCandidate("typo"), new ProviderCandidate("image-backend")], Image());

        Assert.True(result.IsOk);
    }

    [Fact]
    public async Task A_candidate_can_pin_the_model_it_wants()
    {
        var aggregator = new FakeGenerationProvider
        {
            Id = "aggregator",
            Capabilities = new ProviderCapabilities
            {
                Accepts = [ProviderKinds.Text],
                Produces = [ProviderKinds.Image],
                Operations = [ProviderOperation.Complete],
                Models = ["flux-1", "sdxl"],
            },
        };

        var result = await Router(aggregator).GenerateAsync([new ProviderCandidate("aggregator", "sdxl")], Image());

        Assert.True(result.IsOk);
    }

    [Fact]
    public async Task A_response_names_the_backend_that_produced_it_even_after_a_fallback()
    {
        var down = new FakeGenerationProvider { Id = "down" };
        down.Verdicts.Enqueue(ProviderVerdict.Failed);
        var up = new FakeGenerationProvider { Id = "up" };

        var result = await Router(down, up).GenerateAsync([new("down"), new("up")], Image());

        Assert.True(result.IsOk);
        Assert.Equal("up", result.ProviderId);
    }

    [Fact]
    public async Task A_failure_names_the_backend_it_came_from_and_a_synthesized_one_names_none()
    {
        var refusing = new FakeGenerationProvider { Id = "hosted" };
        refusing.Verdicts.Enqueue(ProviderVerdict.Refused);

        var refused = await Router(refusing).GenerateAsync([new("hosted")], Image());
        var nothing = await Router(refusing).GenerateAsync([new("hosted")], Video());   // nothing capable

        Assert.Equal("hosted", refused.ProviderId);
        Assert.Null(nothing.ProviderId);
    }

    [Fact]
    public async Task The_governance_decorators_pass_the_backend_name_through()
    {
        var backend = new FakeGenerationProvider { Id = "sd" };
        IMediaRouter throttled = new RateLimitedMediaRouter(Router(backend),
            new TokenBucketRateLimiter(new RateLimitOptions { PermitsPerSecond = 100, Burst = 10 }));
        IMediaRouter budgeted = new BudgetedMediaRouter(Router(backend), new InMemoryUsageTracker(), new LyntaiOptions());

        Assert.Equal("sd", (await throttled.GenerateAsync([new("sd")], Image())).ProviderId);
        Assert.Equal("sd", (await budgeted.GenerateAsync([new("sd")], Image())).ProviderId);
    }

    [Fact]
    public async Task A_submission_reports_which_backend_owns_the_operation()
    {
        // an operation id is meaningless without knowing who issued it — persist both
        var video = new FakeGenerationJobProvider { Id = "video-backend" };

        var submission = await Router(video).SubmitAsync(
            [new ProviderCandidate("video-backend")], new MediaRequest { Kind = ProviderKinds.Video, Prompt = "x" });

        Assert.Equal("video-backend", submission.ProviderId);
        Assert.Equal("op-1", submission.Operation.Id);
        Assert.Equal(QueuedOperationStatus.Queued, submission.Operation.Status);
    }

    [Fact]
    public async Task Submitting_with_no_job_capable_candidate_fails_without_pretending()
    {
        var image = new FakeGenerationProvider { Id = "image-backend" };

        var submission = await Router(image).SubmitAsync(
            [new ProviderCandidate("image-backend")], new MediaRequest { Kind = ProviderKinds.Video, Prompt = "x" });

        Assert.Equal(QueuedOperationStatus.Failed, submission.Operation.Status);
        Assert.Contains("no capable", submission.Operation.Detail);
    }
}
