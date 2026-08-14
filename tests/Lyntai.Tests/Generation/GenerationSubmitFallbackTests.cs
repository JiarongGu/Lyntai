using Lyntai.Generation;
using Lyntai.Generation.Routing;
using Lyntai.Lifecycle;
using Lyntai.Llm;
using Lyntai.Llm.Routing;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>The SUBMIT path answers a rejection with the routing policy, and reports the reason it was given.
///
/// <para>Both halves were missing for the same reason: a <see cref="GenerationOperation"/> carries a
/// <see cref="GenerationOperationStatus"/>, not a verdict, so there was nothing for
/// <see cref="GenerationRoutingPolicy.ActionFor"/> to switch on and nothing but candidate ids left to report.
/// Every rejection therefore advanced AND took a dead-host strike — including one from a backend that answered
/// "not configured" before it opened a socket, which is exactly the penalty-for-a-known-fact that
/// <c>NotConfigured</c> was introduced to prevent (<c>docs/DECISIONS.md</c> D31).</para></summary>
// serialized with every other class that registers a matcher: LlmVerdictClassifier.AddErrorTextMatcher mutates
// a PROCESS-WIDE list. It does NOT protect the rest of the suite, so the matcher below answers for its own
// probe token and nothing else.
[Collection("verdict-matchers")]
public class GenerationSubmitFallbackTests
{
    private static GenerationRequest Video() => new() { Kind = GenerationKinds.Video, Prompt = "a cat surfing" };

    private static GenerationCandidate[] Order(params string[] ids) => [.. ids.Select(id => new GenerationCandidate(id))];

    /// <summary>How long a bounded await waits before failing outright — see the note in
    /// <c>RouterCooldownKeyTests</c>: a leaked permit makes the waiting caller wait FOREVER, and an unbounded
    /// await would turn a red test into a dead run.</summary>
    private static readonly TimeSpan GateWait = TimeSpan.FromSeconds(5);

    // ---- the verdict ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_rate_limited_submission_BENCHES_the_backend_rather_than_taking_one_strike()
    {
        // a 429 is the queue telling us to stop; the threshold exists for faults that MIGHT be transient, and
        // spending two more submissions to be refused twice more is not what it is for
        var tracker = new DeadHostTracker(threshold: 3, cooldown: TimeSpan.FromMinutes(5));
        var limited = new RejectingJobProvider { Id = "hosted", Detail = "429 Too Many Requests" };
        var working = new FakeGenerationJobProvider { Id = "local" };
        var router = new GenerationRouter([limited, working], deadHosts: tracker);

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
        using var _ = LlmVerdictClassifier.AddErrorTextMatcher(t =>
            t.Contains("queue-unconfigured-probe", StringComparison.Ordinal) ? LlmVerdict.NotConfigured : null);

        var tracker = new DeadHostTracker(threshold: 1, cooldown: TimeSpan.FromMinutes(5));
        var unconfigured = new RejectingJobProvider
        {
            Id = "needs-setup",
            Detail = "queue-unconfigured-probe: BaseUrl and ApiKey are both required",
        };
        var working = new FakeGenerationJobProvider { Id = "local" };
        var router = new GenerationRouter([unconfigured, working], deadHosts: tracker);

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
        var broken = new RejectingJobProvider { Id = "broken", Detail = "queue down" };
        var working = new FakeGenerationJobProvider { Id = "local" };
        var router = new GenerationRouter([broken, working], deadHosts: tracker);

        await router.SubmitAsync(Order("broken", "local"), Video());
        await router.SubmitAsync(Order("broken", "local"), Video());

        Assert.Equal(1, broken.SubmitCalls);
        Assert.True(tracker.IsDead("generation::broken"));
    }

    [Fact]
    public async Task A_refused_submission_SURFACES_instead_of_shopping_the_prompt_to_the_next_queue()
    {
        // the same rule the inline path follows: a content refusal is the backend judging the PROMPT, and
        // re-submitting it elsewhere is not a library's decision to make
        var refusing = new RejectingJobProvider { Id = "hosted", Detail = "content policy violation" };
        var permissive = new FakeGenerationJobProvider { Id = "local" };

        var submission = await new GenerationRouter([refusing, permissive])
            .SubmitAsync(Order("hosted", "local"), Video());

        Assert.Equal(0, permissive.SubmitCalls);         // the whole point
        Assert.Equal("", submission.ProviderId);         // refused is not accepted
        Assert.Contains("content policy", submission.Operation.Detail);
    }

    [Fact]
    public async Task A_host_that_pairs_a_hosted_queue_with_a_permissive_one_can_override_the_refusal_rule()
    {
        // proof the policy is genuinely consulted rather than the Refused case being hardcoded here
        var policy = new GenerationRoutingPolicy().On(GenerationVerdict.Refused, GenerationFallbackAction.Advance);
        var refusing = new RejectingJobProvider { Id = "hosted", Detail = "content policy violation" };
        var permissive = new FakeGenerationJobProvider { Id = "local" };

        var submission = await new GenerationRouter([refusing, permissive], policy)
            .SubmitAsync(Order("hosted", "local"), Video());

        Assert.Equal("local", submission.ProviderId);
        Assert.Equal(1, permissive.SubmitCalls);
    }

    // ---- the reason ----------------------------------------------------------------------------------

    [Fact]
    public async Task A_failed_submission_reports_the_FIRST_backends_own_reason()
    {
        // without this the only thing that survives is a list of candidate ids, which is what the durable job
        // handler fails the job with and what the agent tool hands a model — neither can act on "these didn't
        // work". The FIRST reason, like the inline path keeps the first substantive failure.
        var first = new RejectingJobProvider { Id = "broken", Detail = "queue is full" };
        var second = new RejectingJobProvider { Id = "also-broken", Detail = "disk on fire" };

        var submission = await new GenerationRouter([first, second])
            .SubmitAsync(Order("broken", "also-broken"), Video());

        Assert.Contains("no capable", submission.Operation.Detail);          // the synthesized half is kept
        Assert.Contains("'broken' said: queue is full", submission.Operation.Detail);
        Assert.DoesNotContain("disk on fire", submission.Operation.Detail);
    }

    [Fact]
    public async Task The_reported_ProviderId_stays_EMPTY_even_though_the_message_names_the_backend()
    {
        // IGenerationRouter defines empty as "no candidate accepted", and both callers branch on exactly that
        // — so the rejecting backend's id belongs in the sentence, never in this field
        var broken = new RejectingJobProvider { Id = "broken", Detail = "queue is full" };

        var submission = await new GenerationRouter([broken]).SubmitAsync(Order("broken"), Video());

        Assert.Equal("", submission.ProviderId);
        Assert.Equal(GenerationOperationStatus.Failed, submission.Operation.Status);
        Assert.Contains("broken", submission.Operation.Detail);
    }

    [Fact]
    public async Task A_rejection_with_no_reason_at_all_still_names_who_rejected_it()
    {
        var silent = new RejectingJobProvider { Id = "silent", Detail = null };

        var submission = await new GenerationRouter([silent]).SubmitAsync(Order("silent"), Video());

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

        var refusing = new RejectingJobProvider { Id = "hosted", Detail = "content policy violation" };
        var router = new GenerationRouter([refusing], null, new DeadHostTracker(), _ => key, admission);

        var submission = await router.SubmitAsync(Order("hosted"), Video()).WaitAsync(GateWait);

        Assert.Equal("", submission.ProviderId);
        Assert.Equal(0, admission.GateCount);
        // and the gate still admits, which a leaked permit on a limit of 1 would prevent
        var next = admission.EnterAsync(key, CancellationToken.None);
        Assert.True(next.IsCompleted);
        (await next).Dispose();
    }

    /// <summary>A job backend that always REJECTS the submission, with a detail the test chooses — the thing
    /// the router now classifies. Conclusive on purpose: an inconclusive rejection is decided before the
    /// verdict is, and is already covered by <c>GenerationTimeoutTests</c>.</summary>
    private sealed class RejectingJobProvider : IGenerationProvider, IGenerationJobProvider
    {
        public string Id { get; init; } = "rejecting";

        /// <summary>What the rejected submission reports as its reason; null = none at all.</summary>
        public string? Detail { get; init; }

        public int SubmitCalls { get; private set; }

        public GenerationCapabilities Capabilities { get; } = new()
        {
            Kinds = [GenerationKinds.Video],
            Deliveries = [GenerationDelivery.Job],
        };

        public Task<GenerationProbeResult> ProbeAsync(CancellationToken ct = default) =>
            Task.FromResult(new GenerationProbeResult(true, "up"));

        public Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default) =>
            Task.FromResult(GenerationResult.Failure(GenerationVerdict.Unsupported, "job backend"));

        public Task<GenerationOperation> SubmitAsync(GenerationRequest request, CancellationToken ct = default)
        {
            SubmitCalls++;
            return Task.FromResult(new GenerationOperation("", GenerationOperationStatus.Failed, Detail: Detail));
        }

        public Task<GenerationOperation> PollAsync(string operationId, CancellationToken ct = default) =>
            Task.FromResult(new GenerationOperation(operationId, GenerationOperationStatus.Failed));

        public Task<GenerationResult> FetchAsync(string operationId, CancellationToken ct = default) =>
            Task.FromResult(GenerationResult.Failure(GenerationVerdict.Failed, "nothing"));

        public Task<GenerationOperation> CancelAsync(string operationId, CancellationToken ct = default) =>
            Task.FromResult(new GenerationOperation(operationId, GenerationOperationStatus.Cancelled));
    }
}
