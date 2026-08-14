using Lyntai.Generation;
using Lyntai.Generation.Routing;
using Lyntai.Llm;
using Lyntai.Llm.Routing;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>A media verdict has to be able to be BLAMELESS and REPORTABLE at once, and until 2026-08-05 the
/// router forced a choice between them (<c>TASKS.md</c> Part 40, opened by <c>docs/DECISIONS.md</c> D36).
///
/// <para>The rule that forced it is right and stays: a blameless verdict must never MASK a real failure, or
/// <c>[downHost → Failed, neverConfigured → NotConfigured]</c> sends the caller off to set up a key while the
/// backend they HAD configured is the one that is down (D31). What was missing is the OTHER half — when
/// nothing substantive failed at all, the blameless backend's own words are the honest answer, and the
/// synthetic "every capable backend reported it is not configured" was not even accurate for a run in which
/// every candidate said <see cref="GenerationVerdict.Unsupported"/>.</para>
///
/// <para>So the router keeps a second slot, exactly as <c>LlmRouter.CompleteAsync</c> already did
/// (<c>last ?? lastBlameless ?? synthetic</c>) — and only once that was in place could
/// <see cref="LlmVerdict.ContextWindowExceeded"/> become <see cref="GenerationVerdict.Unsupported"/>, which is
/// what stops repeated oversized prompts from benching a healthy backend. Doing the mapping first would just
/// have swapped one cost for the other.</para></summary>
public class GenerationBlamelessReportingTests
{
    private static GenerationRequest Image() => new() { Kind = GenerationKinds.Image, Prompt = "a red square" };

    private static GenerationCandidate[] Order(params string[] ids) => [.. ids.Select(id => new GenerationCandidate(id))];

    // ---- the reporting rule --------------------------------------------------------------------------

    [Fact]
    public async Task A_run_that_only_blameless_backends_answered_reports_what_the_FIRST_one_said()
    {
        // the hole Part 40 names: both candidates explained themselves, and the caller used to be told
        // "not configured" — which is neither what happened nor something they can act on
        var first = new SayingProvider
        {
            Id = "a", Verdict = GenerationVerdict.Unsupported, Detail = "prompt is too long: 210000 tokens",
        };
        var second = new SayingProvider
        {
            Id = "b", Verdict = GenerationVerdict.Unsupported, Detail = "1024x1792 is past this model's limit",
        };

        var result = await new GenerationRouter([first, second]).GenerateAsync(Order("a", "b"), Image());

        Assert.Equal(GenerationVerdict.Unsupported, result.Verdict);
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
            Id = "a", Verdict = GenerationVerdict.Unsupported, Detail = "cannot take an image input",
        };
        var broken = new SayingProvider
        {
            Id = "b", Verdict = GenerationVerdict.Failed, Detail = "connection reset by peer",
        };

        var result = await new GenerationRouter([gap, broken]).GenerateAsync(Order("a", "b"), Image());

        Assert.Equal(GenerationVerdict.Failed, result.Verdict);
        Assert.Contains("connection reset", result.Detail);
        Assert.DoesNotContain("cannot take an image input", result.Detail);
    }

    [Fact]
    public async Task A_blameless_verdict_with_nothing_to_say_falls_through_to_the_synthetic_message()
    {
        // an empty detail is not a reason, and the synthetic sentence says strictly more than it does — so
        // the blameless slot takes only a result that actually explained itself
        var silent = new SayingProvider { Id = "a", Verdict = GenerationVerdict.NotConfigured, Detail = null };

        var result = await new GenerationRouter([silent]).GenerateAsync(Order("a"), Image());

        Assert.Equal(GenerationVerdict.NotConfigured, result.Verdict);
        Assert.Contains("every capable backend reported it is not configured", result.Detail);
    }

    [Fact]
    public async Task Reporting_a_blameless_reason_does_not_turn_it_into_a_fault()
    {
        // the whole point of the split: what is REPORTED changed, what is BLAMED did not. One penalised
        // failure would be enough to bench at this threshold, so the second run is the assertion.
        var tracker = new DeadHostTracker(threshold: 1, cooldown: TimeSpan.FromMinutes(5));
        var gap = new SayingProvider
        {
            Id = "a", Verdict = GenerationVerdict.Unsupported, Detail = "this model takes no image input",
        };
        var unconfigured = new SayingProvider
        {
            Id = "b", Verdict = GenerationVerdict.NotConfigured, Detail = "no BaseUrl configured",
        };
        var router = new GenerationRouter([gap, unconfigured], deadHosts: tracker);

        await router.GenerateAsync(Order("a", "b"), Image());
        await router.GenerateAsync(Order("a", "b"), Image());

        Assert.Equal(2, gap.GenerateCalls);          // still in rotation
        Assert.Equal(2, unconfigured.GenerateCalls); // …so a key set later is picked up
        Assert.False(tracker.IsDead("generation::a"));
        Assert.False(tracker.IsDead("generation::b"));
    }

    // ---- what the rule was blocking: the oversized prompt ---------------------------------------------

    [Fact]
    public async Task An_oversized_prompt_no_longer_benches_a_perfectly_healthy_backend()
    {
        // an image backend that answers "prompt is too long" is not ill — but as GenerationVerdict.Failed it
        // took PenalizeAndAdvance, so a few oversized prompts in a row put it on dead-host cooldown and
        // UNRELATED later requests were routed away from it
        var translated = GenerationVerdictClassifier.FromErrorText("prompt is too long: 210000 tokens");
        Assert.Equal(GenerationVerdict.Unsupported, translated);

        var tracker = new DeadHostTracker(threshold: 1, cooldown: TimeSpan.FromMinutes(5));
        var oversized = new SayingProvider
        {
            Id = "hosted", Verdict = translated, Detail = "prompt is too long: 210000 tokens",
        };
        var working = new FakeGenerationProvider { Id = "local" };
        var router = new GenerationRouter([oversized, working], deadHosts: tracker);

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
            Verdict = GenerationVerdictClassifier.FromErrorText("prompt is too long: 210000 tokens"),
            Detail = "prompt is too long: 210000 tokens",
        };

        var result = await new GenerationRouter([oversized]).GenerateAsync(Order("hosted"), Image());

        Assert.Equal(GenerationVerdict.Unsupported, result.Verdict);
        Assert.Contains("prompt is too long", result.Detail);
    }

    /// <summary>A backend that answers with a fixed verdict and its OWN words — the words are the whole
    /// subject here, which is why <c>FakeGenerationProvider</c>'s synthesized <c>"fake {verdict}"</c> detail
    /// is not enough.</summary>
    private sealed class SayingProvider : IGenerationProvider
    {
        public string Id { get; init; } = "saying";

        /// <summary>What every render reports; Ok is deliberately not supported — this fake exists for the
        /// failure paths.</summary>
        public GenerationVerdict Verdict { get; init; } = GenerationVerdict.Unsupported;

        /// <summary>The backend's own words; null = a verdict with nothing to say.</summary>
        public string? Detail { get; init; }

        public int GenerateCalls { get; private set; }

        public GenerationCapabilities Capabilities { get; } = new()
        {
            Kinds = [GenerationKinds.Image],
            Deliveries = [GenerationDelivery.Inline],
            SupportsInputs = true,
        };

        public Task<GenerationProbeResult> ProbeAsync(CancellationToken ct = default) =>
            Task.FromResult(new GenerationProbeResult(true, "up"));

        public Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default)
        {
            GenerateCalls++;
            return Task.FromResult(GenerationResult.Failure(Verdict, Detail));
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
    private static GenerationRequest Video() => new() { Kind = GenerationKinds.Video, Prompt = "a cat surfing" };

    private static GenerationCandidate[] Order(params string[] ids) => [.. ids.Select(id => new GenerationCandidate(id))];

    [Fact]
    public async Task A_submission_only_blameless_queues_rejected_still_reports_what_one_of_them_said()
    {
        // the same hole as the inline path: the reason was dropped for being blameless, leaving the durable
        // job handler and the agent tool with a list of candidate ids nobody can act on
        using var _ = LlmVerdictClassifier.AddErrorTextMatcher(t =>
            t.Contains("queue-blameless-probe", StringComparison.Ordinal) ? LlmVerdict.NotConfigured : null);

        var unconfigured = new RejectingJobBackend
        {
            Id = "needs-setup", Detail = "queue-blameless-probe: BaseUrl and ApiKey are both required",
        };

        var submission = await new GenerationRouter([unconfigured]).SubmitAsync(Order("needs-setup"), Video());

        Assert.Equal("", submission.ProviderId);   // still "no candidate accepted" — the id belongs in the sentence
        Assert.Contains("no capable", submission.Operation.Detail);
        Assert.Contains("'needs-setup' said: queue-blameless-probe", submission.Operation.Detail);
    }

    [Fact]
    public async Task A_substantive_rejection_still_outranks_a_blameless_one_in_the_report()
    {
        // blameless FIRST, so a slot that simply remembered whichever spoke first would report the wrong one
        using var _ = LlmVerdictClassifier.AddErrorTextMatcher(t =>
            t.Contains("queue-blameless-probe", StringComparison.Ordinal) ? LlmVerdict.NotConfigured : null);

        var unconfigured = new RejectingJobBackend { Id = "needs-setup", Detail = "queue-blameless-probe: no key" };
        var broken = new RejectingJobBackend { Id = "broken", Detail = "queue is full" };

        var submission = await new GenerationRouter([unconfigured, broken])
            .SubmitAsync(Order("needs-setup", "broken"), Video());

        Assert.Contains("'broken' said: queue is full", submission.Operation.Detail);
        Assert.DoesNotContain("queue-blameless-probe", submission.Operation.Detail);
    }

    /// <summary>A job backend that always rejects the submission, with a detail the test chooses — the text
    /// the router classifies. Conclusive on purpose: an inconclusive rejection is decided BEFORE the verdict
    /// is, and is covered by <c>GenerationTimeoutTests</c>.</summary>
    private sealed class RejectingJobBackend : IGenerationProvider, IGenerationJobProvider
    {
        public string Id { get; init; } = "rejecting";

        /// <summary>What the rejected submission reports as its reason.</summary>
        public string? Detail { get; init; }

        public GenerationCapabilities Capabilities { get; } = new()
        {
            Kinds = [GenerationKinds.Video],
            Deliveries = [GenerationDelivery.Job],
        };

        public Task<GenerationProbeResult> ProbeAsync(CancellationToken ct = default) =>
            Task.FromResult(new GenerationProbeResult(true, "up"));

        public Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default) =>
            Task.FromResult(GenerationResult.Failure(GenerationVerdict.Unsupported, "job backend"));

        public Task<GenerationOperation> SubmitAsync(GenerationRequest request, CancellationToken ct = default) =>
            Task.FromResult(new GenerationOperation("", GenerationOperationStatus.Failed, Detail: Detail));

        public Task<GenerationOperation> PollAsync(string operationId, CancellationToken ct = default) =>
            Task.FromResult(new GenerationOperation(operationId, GenerationOperationStatus.Failed));

        public Task<GenerationResult> FetchAsync(string operationId, CancellationToken ct = default) =>
            Task.FromResult(GenerationResult.Failure(GenerationVerdict.Failed, "nothing"));

        public Task<GenerationOperation> CancelAsync(string operationId, CancellationToken ct = default) =>
            Task.FromResult(new GenerationOperation(operationId, GenerationOperationStatus.Cancelled));
    }
}
