using Lyntai.Generation;
using Lyntai.Inference;
using Lyntai.Tests.Fakes;
using static Lyntai.Tests.Fakes.CandidateLists;

namespace Lyntai.Tests.Generation;

/// <summary>A media verdict can be BLAMELESS and REPORTABLE at once (<c>docs/task-archive.md</c> Part 40,
/// <c>docs/DECISIONS.md</c> D36).
///
/// <para>A blameless verdict never MASKS a real failure, or <c>[downHost → Failed, neverConfigured →
/// NotConfigured]</c> sends the caller off to set up a key while the backend they HAD configured is the one
/// that is down (D31). When nothing substantive failed, the blameless backend's own words are the honest
/// answer — a synthetic "every capable backend reported it is not configured" is not even accurate when every
/// candidate said <see cref="ProviderVerdict.Unsupported"/>. So the router keeps a second slot, as
/// <c>TextRouter.CompleteAsync</c> does (<c>last ?? lastBlameless ?? synthetic</c>), and that slot is what
/// lets <see cref="ProviderVerdict.ContextWindowExceeded"/> map to <see cref="ProviderVerdict.Unsupported"/>
/// without repeated oversized prompts benching a healthy backend.</para></summary>
public class GenerationBlamelessReportingTests
{
    private static MediaRequest Image() => new() { Kind = ProviderKinds.Image, Prompt = "a red square" };


    // ---- the reporting rule --------------------------------------------------------------------------

    [Fact]
    public async Task A_run_that_only_blameless_backends_answered_reports_what_the_FIRST_one_said()
    {
        // the hole Part 40 names: both candidates explained themselves, and "not configured" is neither what
        // happened nor something the caller can act on
        var first = new SayingProvider
        {
            Id = "a", Verdict = ProviderVerdict.Unsupported, Detail = "prompt is too long: 210000 tokens",
        };
        var second = new SayingProvider
        {
            Id = "b", Verdict = ProviderVerdict.Unsupported, Detail = "1024x1792 is past this model's limit",
        };

        var result = await new MediaRouter([first, second]).GenerateAsync(Order("a", "b"), Image());

        Assert.Equal(ProviderVerdict.Unsupported, result.Verdict);
        Assert.Contains("prompt is too long", result.Detail);
        // the FIRST one, like firstFailure — the first backend's answer explains a media run better than the
        // last one's, and the two slots must not disagree about that
        Assert.DoesNotContain("past this model's limit", result.Detail);
    }

    [Fact]
    public async Task A_real_failure_still_outranks_a_blameless_reason_even_when_it_comes_LATER()
    {
        // D31's rule, and it is not up for renegotiation: the blameless slot answers only when the
        // substantive one is empty. Ordered blameless-first precisely because that is the case a
        // "remember whatever spoke first" implementation would get wrong.
        var gap = new SayingProvider
        {
            Id = "a", Verdict = ProviderVerdict.Unsupported, Detail = "cannot take an image input",
        };
        var broken = new SayingProvider
        {
            Id = "b", Verdict = ProviderVerdict.Failed, Detail = "connection reset by peer",
        };

        var result = await new MediaRouter([gap, broken]).GenerateAsync(Order("a", "b"), Image());

        Assert.Equal(ProviderVerdict.Failed, result.Verdict);
        Assert.Contains("connection reset", result.Detail);
        Assert.DoesNotContain("cannot take an image input", result.Detail);
    }

    [Fact]
    public async Task A_blameless_verdict_with_nothing_to_say_falls_through_to_the_synthetic_message()
    {
        // an empty detail is not a reason, and the synthetic sentence says strictly more than it does — so
        // the blameless slot takes only a result that actually explained itself
        var silent = new SayingProvider { Id = "a", Verdict = ProviderVerdict.NotConfigured, Detail = null };

        var result = await new MediaRouter([silent]).GenerateAsync(Order("a"), Image());

        Assert.Equal(ProviderVerdict.NotConfigured, result.Verdict);
        Assert.Contains("every capable backend reported it is not configured", result.Detail);
    }

    [Fact]
    public async Task Reporting_a_blameless_reason_does_not_turn_it_into_a_fault()
    {
        // the whole point of the split: what is REPORTED is not what is BLAMED. One penalised
        // failure would be enough to bench at this threshold, so the second run is the assertion.
        var tracker = new DeadHostTracker(threshold: 1, cooldown: TimeSpan.FromMinutes(5));
        var gap = new SayingProvider
        {
            Id = "a", Verdict = ProviderVerdict.Unsupported, Detail = "this model takes no image input",
        };
        var unconfigured = new SayingProvider
        {
            Id = "b", Verdict = ProviderVerdict.NotConfigured, Detail = "no BaseUrl configured",
        };
        var router = new MediaRouter([gap, unconfigured], deadHosts: tracker);

        await router.GenerateAsync(Order("a", "b"), Image());
        await router.GenerateAsync(Order("a", "b"), Image());

        Assert.Equal(2, gap.GenerateCalls);          // still in rotation
        Assert.Equal(2, unconfigured.GenerateCalls); // …so a key set later is picked up
        Assert.False(tracker.IsDead("generation::a"));
        Assert.False(tracker.IsDead("generation::b"));
    }

    // ---- what the rule unblocks: the oversized prompt -------------------------------------------------

    [Fact]
    public async Task An_oversized_prompt_no_longer_benches_a_perfectly_healthy_backend()
    {
        // an image backend that answers "prompt is too long" is not ill — as ProviderVerdict.Failed it would
        // take PenalizeAndAdvance, so a few oversized prompts in a row would put it on dead-host cooldown and
        // route UNRELATED later requests away from it. The taxonomy is one (D136), so the precise member
        // survives and the policy carries an explicit entry for it — with no penalty.
        var oversizedVerdict = ProviderVerdictClassifier.FromErrorText("prompt is too long: 210000 tokens");
        Assert.Equal(ProviderVerdict.ContextWindowExceeded, oversizedVerdict);

        var tracker = new DeadHostTracker(threshold: 1, cooldown: TimeSpan.FromMinutes(5));
        var oversized = new SayingProvider
        {
            Id = "hosted", Verdict = oversizedVerdict, Detail = "prompt is too long: 210000 tokens",
        };
        var working = new FakeGenerationProvider { Id = "local" };
        var router = new MediaRouter([oversized, working], deadHosts: tracker);

        var first = await router.GenerateAsync(Order("hosted", "local"), Image());
        var second = await router.GenerateAsync(Order("hosted", "local"), Image());

        Assert.True(first.IsOk);
        Assert.True(second.IsOk);
        Assert.Equal(2, oversized.GenerateCalls);
        Assert.False(tracker.IsDead("generation::hosted"));
    }

    [Fact]
    public async Task An_oversized_prompt_is_STILL_the_reason_when_nothing_else_could_serve_it()
    {
        // the half the mapping alone would have lost: "your prompt is too long" is the one thing the caller
        // can act on, and telling them "no capable media backend" instead is a strictly worse answer
        var oversized = new SayingProvider
        {
            Id = "hosted",
            Verdict = ProviderVerdictClassifier.FromErrorText("prompt is too long: 210000 tokens"),
            Detail = "prompt is too long: 210000 tokens",
        };

        var result = await new MediaRouter([oversized]).GenerateAsync(Order("hosted"), Image());

        // the member itself, not flattened to Unsupported — and it is SUBSTANTIVE rather than blameless, which is right: "too big for this backend" is actionable,
        // so it reaches the caller through firstFailure instead of the blameless slot
        Assert.Equal(ProviderVerdict.ContextWindowExceeded, result.Verdict);
        Assert.Contains("prompt is too long", result.Detail);
    }

    /// <summary>A backend that answers with a fixed verdict and its OWN words — the words are the whole
    /// subject here, which is why <c>FakeGenerationProvider</c>'s synthesized <c>"fake {verdict}"</c> detail
    /// is not enough.</summary>
    private sealed class SayingProvider : IModelProvider
    {
        public string Id { get; init; } = "saying";

        /// <summary>What every render reports; Ok is deliberately not supported — this fake exists for the
        /// failure paths.</summary>
        public ProviderVerdict Verdict { get; init; } = ProviderVerdict.Unsupported;

        /// <summary>The backend's own words; null = a verdict with nothing to say.</summary>
        public string? Detail { get; init; }

        public int GenerateCalls { get; private set; }

        public ProviderCapabilities Capabilities { get; } = new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Image],
            Operations = [ProviderOperation.Complete],
            SupportsInputs = true,
        };

        public Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default) =>
            Task.FromResult(new ProviderProbeResult(true, "up"));

        public Task<MediaResponse> GenerateAsync(MediaRequest request, CancellationToken ct = default)
        {
            GenerateCalls++;
            return Task.FromResult(MediaResponse.Failure(Verdict, Detail));
        }
    }
}

/// <summary>The SUBMIT path's half of the same rule. Kept in its own class because it registers a process-wide
/// error-text matcher: the submit path has no verdict to be handed, it CLASSIFIES the rejection's text, and
/// nothing in the built-in corpus classifies as blameless on its own.</summary>
// serialized with every other class that registers one — AddErrorTextMatcher mutates a PROCESS-WIDE list. It
// does NOT protect the rest of the suite, so the matcher below answers for its own probe token and nothing else.
[Collection("verdict-matchers")]
public class GenerationSubmitBlamelessReportingTests
{
    private static MediaRequest Video() => new() { Kind = ProviderKinds.Video, Prompt = "a cat surfing" };


    [Fact]
    public async Task A_submission_only_blameless_queues_rejected_still_reports_what_one_of_them_said()
    {
        // the same hole as the inline path: dropping the reason for being blameless leaves the durable job
        // handler and the agent tool with a list of candidate ids nobody can act on
        using var _ = ProviderVerdictClassifier.AddErrorTextMatcher(t =>
            t.Contains("queue-blameless-probe", StringComparison.Ordinal) ? ProviderVerdict.NotConfigured : null);

        var unconfigured = FakeGenerationJobProvider.Rejecting("needs-setup", "queue-blameless-probe: BaseUrl and ApiKey are both required");

        var submission = await new MediaRouter([unconfigured]).SubmitAsync(Order("needs-setup"), Video());

        Assert.Equal("", submission.ProviderId);   // still "no candidate accepted" — the id belongs in the sentence
        Assert.Contains("no capable", submission.Operation.Detail);
        Assert.Contains("'needs-setup' said: queue-blameless-probe", submission.Operation.Detail);
    }

    [Fact]
    public async Task A_substantive_rejection_still_outranks_a_blameless_one_in_the_report()
    {
        // blameless FIRST, so a slot that simply remembered whichever spoke first would report the wrong one
        using var _ = ProviderVerdictClassifier.AddErrorTextMatcher(t =>
            t.Contains("queue-blameless-probe", StringComparison.Ordinal) ? ProviderVerdict.NotConfigured : null);

        var unconfigured = FakeGenerationJobProvider.Rejecting("needs-setup", "queue-blameless-probe: no key");
        var broken = FakeGenerationJobProvider.Rejecting("broken", "queue is full");

        var submission = await new MediaRouter([unconfigured, broken])
            .SubmitAsync(Order("needs-setup", "broken"), Video());

        Assert.Contains("'broken' said: queue is full", submission.Operation.Detail);
        Assert.DoesNotContain("queue-blameless-probe", submission.Operation.Detail);
    }

}
