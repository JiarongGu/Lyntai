using Lyntai.Generation;
using Lyntai.Inference;
using Lyntai.Tests.Fakes;
using static Lyntai.Tests.Fakes.CandidateLists;
using static Lyntai.Tests.Fakes.TestTimeouts;

namespace Lyntai.Tests.Generation;

/// <summary>The SUBMIT path answers a rejection with the routing policy, and reports the reason it was given.
///
/// <para>Both halves were missing for the same reason: a <see cref="QueuedOperation"/> carries a
/// <see cref="QueuedOperationStatus"/>, not a verdict, so there was nothing for
/// <see cref="MediaRoutingPolicy.ActionFor"/> to switch on and nothing but candidate ids left to report.
/// Every rejection therefore advanced AND took a dead-host strike — including one from a backend that answered
/// "not configured" before it opened a socket, which is exactly the penalty-for-a-known-fact that
/// <c>NotConfigured</c> was introduced to prevent (<c>docs/DECISIONS.md</c> D31).</para></summary>
// serialized with every other class that registers a matcher: ProviderVerdictClassifier.AddErrorTextMatcher mutates
// a PROCESS-WIDE list. It does NOT protect the rest of the suite, so the matcher below answers for its own
// probe token and nothing else.
[Collection("verdict-matchers")]
public class GenerationSubmitFallbackTests
{
    private static MediaRequest Video() => new() { Kind = ProviderKinds.Video, Prompt = "a cat surfing" };

    // ---- the verdict ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_rate_limited_submission_BENCHES_the_backend_rather_than_taking_one_strike()
    {
        // a 429 is the queue telling us to stop; the threshold exists for faults that MIGHT be transient, and
        // spending two more submissions to be refused twice more is not what it is for
        var tracker = new DeadHostTracker(threshold: 3, cooldown: TimeSpan.FromMinutes(5));
        var limited = FakeGenerationJobProvider.Rejecting("hosted", "429 Too Many Requests");
        var working = new FakeGenerationJobProvider { Id = "local" };
        var router = new MediaRouter([limited, working], deadHosts: tracker);

        await router.SubmitAsync(Order("hosted", "local"), Video());
        var second = await router.SubmitAsync(Order("hosted", "local"), Video());

        Assert.Equal(1, limited.SubmitCalls);            // asked once, benched, skipped on the second run
        Assert.Equal("local", second.ProviderId);
        Assert.True(tracker.IsDead("generation::hosted"));
    }

    [Fact]
    public async Task A_blameless_rejection_never_counts_toward_the_dead_host_threshold()
    {
        // the shipped case: a queue backend that answers "not configured" before it opens a socket is not ill,
        // and benching it takes it out of rotation for being honest about a fact known before the call
        using var _ = ProviderVerdictClassifier.AddErrorTextMatcher(t =>
            t.Contains("queue-unconfigured-probe", StringComparison.Ordinal) ? ProviderVerdict.NotConfigured : null);

        var tracker = new DeadHostTracker(threshold: 1, cooldown: TimeSpan.FromMinutes(5));
        var unconfigured = FakeGenerationJobProvider.Rejecting("needs-setup", "queue-unconfigured-probe: BaseUrl and ApiKey are both required");
        var working = new FakeGenerationJobProvider { Id = "local" };
        var router = new MediaRouter([unconfigured, working], deadHosts: tracker);

        await router.SubmitAsync(Order("needs-setup", "local"), Video());
        await router.SubmitAsync(Order("needs-setup", "local"), Video());

        Assert.Equal(2, unconfigured.SubmitCalls);       // still in rotation, so a key set later is picked up
        Assert.False(tracker.IsDead("generation::needs-setup"));
    }

    [Fact]
    public async Task A_rejection_nothing_recognises_still_counts_toward_the_threshold()
    {
        // the counterweight: classifying the detail must not turn every unrecognised rejection blameless, or
        // a genuinely broken queue would never be benched at all
        var tracker = new DeadHostTracker(threshold: 1, cooldown: TimeSpan.FromMinutes(5));
        var broken = FakeGenerationJobProvider.Rejecting("broken", "queue down");
        var working = new FakeGenerationJobProvider { Id = "local" };
        var router = new MediaRouter([broken, working], deadHosts: tracker);

        var first = await router.SubmitAsync(Order("broken", "local"), Video());
        var second = await router.SubmitAsync(Order("broken", "local"), Video());

        Assert.Equal("local", first.ProviderId);
        Assert.Equal("local", second.ProviderId);
        Assert.Equal(1, broken.SubmitCalls);
        Assert.True(tracker.IsDead("generation::broken"));
    }

    [Fact]
    public async Task A_submission_carrying_an_Unsupported_VERDICT_advances_blamelessly_whatever_its_text_says()
    {
        // the backend knows the request is the problem, not its health — and its text would otherwise classify
        // as a rate limit and bench it. Threshold 1: a single recorded failure would bench it.
        var tracker = new DeadHostTracker(threshold: 1, cooldown: TimeSpan.FromMinutes(5));
        var cannot = FakeGenerationJobProvider.Rejecting("graph", "429 Too Many Requests", ProviderVerdict.Unsupported);
        var working = new FakeGenerationJobProvider { Id = "local" };
        var router = new MediaRouter([cannot, working], deadHosts: tracker);

        for (var i = 0; i < 3; i++)
            Assert.Equal("local", (await router.SubmitAsync(Order("graph", "local"), Video())).ProviderId);

        Assert.Equal(3, cannot.SubmitCalls);             // asked every time: never benched
        Assert.False(tracker.IsDead("generation::graph"));
        // the same text WITHOUT a verdict benches on the first run —
        // A_rate_limited_submission_BENCHES_the_backend_rather_than_taking_one_strike
    }

    [Fact]
    public async Task A_refused_submission_SURFACES_instead_of_shopping_the_prompt_to_the_next_queue()
    {
        // the same rule the inline path follows: a content refusal is the backend judging the PROMPT, and
        // re-submitting it elsewhere is not a library's decision to make
        var refusing = FakeGenerationJobProvider.Rejecting("hosted", "content policy violation");
        var permissive = new FakeGenerationJobProvider { Id = "local" };

        var submission = await new MediaRouter([refusing, permissive])
            .SubmitAsync(Order("hosted", "local"), Video());

        Assert.Equal(0, permissive.SubmitCalls);         // the whole point
        Assert.Equal("", submission.ProviderId);         // refused is not accepted
        Assert.Contains("content policy", submission.Operation.Detail);
    }

    [Fact]
    public async Task A_host_that_pairs_a_hosted_queue_with_a_permissive_one_can_override_the_refusal_rule()
    {
        // proof the policy is genuinely consulted rather than the Refused case being hardcoded here
        var policy = new MediaRoutingPolicy().On(ProviderVerdict.Refused, FallbackAction.Advance);
        var refusing = FakeGenerationJobProvider.Rejecting("hosted", "content policy violation");
        var permissive = new FakeGenerationJobProvider { Id = "local" };

        var submission = await new MediaRouter([refusing, permissive], policy)
            .SubmitAsync(Order("hosted", "local"), Video());

        Assert.Equal("local", submission.ProviderId);
        Assert.Equal(1, permissive.SubmitCalls);
    }

    [Fact]
    public async Task A_submission_no_candidate_accepted_carries_the_verdict_of_its_most_telling_rejection()
    {
        // the synthesized "nobody took it" must say WHY, or a caller cannot tell it from a surfaced refusal
        var unsupported = FakeGenerationJobProvider.Rejecting("comfy", "no workflow", ProviderVerdict.Unsupported);
        var broken = FakeGenerationJobProvider.Rejecting("broken", "queue is full");

        var blameless = await new MediaRouter([unsupported]).SubmitAsync(Order("comfy"), Video());
        var mixed = await new MediaRouter([unsupported, broken]).SubmitAsync(Order("comfy", "broken"), Video());

        Assert.Equal(("", ProviderVerdict.Unsupported), (blameless.ProviderId, blameless.Operation.Verdict));
        Assert.Equal(ProviderVerdict.Failed, mixed.Operation.Verdict);   // a real failure outranks a blameless one
    }

    [Fact]
    public async Task A_submission_nothing_could_even_attempt_says_why_in_its_verdict()
    {
        var image = new FakeGenerationProvider { Id = "image" };   // not job-capable: a capability gap
        var tracker = new DeadHostTracker(threshold: 5, cooldown: TimeSpan.FromMinutes(5));
        tracker.MarkDead("generation::a");
        tracker.MarkDead("generation::b");
        var benched = new MediaRouter(
            [new FakeGenerationJobProvider { Id = "a" }, new FakeGenerationJobProvider { Id = "b" }], deadHosts: tracker);

        var gap = await new MediaRouter([image]).SubmitAsync(Order("image"), Video());
        var cooling = await benched.SubmitAsync(Order("a", "b"), Video());

        Assert.Equal(ProviderVerdict.Unsupported, gap.Operation.Verdict);
        Assert.Equal(ProviderVerdict.RateLimited, cooling.Operation.Verdict);
    }

    [Fact]
    public async Task A_surfaced_refusal_carries_the_verdict_it_was_surfaced_for()
    {
        // classified from the text, so the backend set none: the router says what it acted on
        var refusing = FakeGenerationJobProvider.Rejecting("hosted", "content policy violation");

        var submission = await new MediaRouter([refusing]).SubmitAsync(Order("hosted"), Video());

        Assert.Equal(ProviderVerdict.Refused, submission.Operation.Verdict);
    }

    // ---- the reason ----------------------------------------------------------------------------------

    [Fact]
    public async Task A_failed_submission_reports_the_FIRST_backends_own_reason()
    {
        // without this the only thing that survives is a list of candidate ids, which is what the durable job
        // handler fails the job with and what the agent tool hands a model — neither can act on "these didn't
        // work". The FIRST reason, like the inline path keeps the first substantive failure.
        var first = FakeGenerationJobProvider.Rejecting("broken", "queue is full");
        var second = FakeGenerationJobProvider.Rejecting("also-broken", "disk on fire");

        var submission = await new MediaRouter([first, second])
            .SubmitAsync(Order("broken", "also-broken"), Video());

        Assert.Contains("no capable", submission.Operation.Detail);          // the synthesized half is kept
        Assert.Contains("'broken' said: queue is full", submission.Operation.Detail);
        Assert.DoesNotContain("disk on fire", submission.Operation.Detail);
    }

    [Fact]
    public async Task The_reported_ProviderId_stays_EMPTY_even_though_the_message_names_the_backend()
    {
        // IMediaRouter defines empty as "no candidate accepted", and both callers branch on exactly that
        // — so the rejecting backend's id belongs in the sentence, never in this field
        var broken = FakeGenerationJobProvider.Rejecting("broken", "queue is full");

        var submission = await new MediaRouter([broken]).SubmitAsync(Order("broken"), Video());

        Assert.Equal("", submission.ProviderId);
        Assert.Equal(QueuedOperationStatus.Failed, submission.Operation.Status);
        Assert.Contains("broken", submission.Operation.Detail);
    }

    [Fact]
    public async Task A_rejection_with_no_reason_at_all_still_names_who_rejected_it()
    {
        var silent = FakeGenerationJobProvider.Rejecting("silent", null);

        var submission = await new MediaRouter([silent]).SubmitAsync(Order("silent"), Video());

        Assert.Contains("'silent' rejected it", submission.Operation.Detail);
    }

    // ---- the gate ------------------------------------------------------------------------------------

    [Fact]
    public async Task A_SURFACED_refusal_still_releases_its_admission_permit()
    {
        // Surface is a NEW return from the middle of the submit `using` — precisely where a hand-rolled
        // release goes missing, and a permit that never comes back pins its gate for the life of the process
        var options = new ProviderAdmissionOptions();
        options.BySlot["hosted"] = 1;
        var admission = new ProviderAdmission(options);
        var key = ProviderKey.For("hosted").With("v", "a").Build();

        var refusing = FakeGenerationJobProvider.Rejecting("hosted", "content policy violation");
        var router = new MediaRouter([refusing], null, new DeadHostTracker(), _ => key, admission);

        var submission = await router.SubmitAsync(Order("hosted"), Video()).WaitAsync(GateWait);

        Assert.Equal("", submission.ProviderId);
        Assert.Equal(0, admission.GateCount);
        // and the gate still admits, which a leaked permit on a limit of 1 would prevent
        var next = admission.EnterAsync(key, CancellationToken.None);
        Assert.True(next.IsCompleted);
        (await next).Dispose();
    }

}
