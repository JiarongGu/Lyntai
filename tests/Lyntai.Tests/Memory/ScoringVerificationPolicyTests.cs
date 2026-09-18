using Lyntai.Inference;
using Lyntai.Memory.Verification;
using Microsoft.Extensions.Logging;

namespace Lyntai.Tests.Memory;

/// <summary>The memory half of verifying with a scoring backend: what to send, how many to endorse, and
/// what an unusable answer means.
///
/// <para><b>Not one of these needs HTTP</b>, which is the point of D140 — the policy and the
/// <c>/v1/rerank</c> client were ONE class in a provider package, so every policy assertion used to be
/// written against a scripted wire. A fake <see cref="IModelProvider"/> declaring
/// <see cref="ProviderKinds.Score"/> is all this layer ever needed.</para>
///
/// <para>The failure mode this file exists for is a SILENT one: the seam is fail-open, so a broken backend
/// and a backend that agreed with the ranking are indistinguishable from the endorsement alone
/// (<c>pitfalls.md</c>). So the null cases assert <c>Judged == false</c> rather than only an empty
/// list.</para></summary>
public class ScoringVerificationPolicyTests
{
    /// <summary>A backend that produces scores and records what it was asked to score.</summary>
    private sealed class FakeScorer(
        Func<IReadOnlyList<string>, IReadOnlyList<double>> score, string id = "scorer") : IScoreProvider
    {
        public string Id => id;
        public List<IReadOnlyList<string>> Sent { get; } = [];

        public ProviderCapabilities Capabilities { get; } = new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Score],
            Operations = [ProviderOperation.Complete],
        };

        /// <summary>Throws where the scripted function throws, rather than converting it to a verdict: these
        /// tests are about what the seam does with a backend that FAILS, and the router classifying an
        /// escaping exception is the path a real in-process backend takes.</summary>
        public Task<ScoreResponse> CallAsync(ScoreRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Sent.Add(request.Documents);
            var scores = score(request.Documents);
            return Task.FromResult(scores.Count == 0
                ? new ScoreResponse(ProviderVerdict.Ok, scores)
                : ScoreResponse.Success(scores));
        }
    }

    /// <summary>A text backend — registered, available, and no use here. The filter must skip it.</summary>
    private sealed class ChatOnly : IModelProvider
    {
        public string Id => "chat";
        public ProviderCapabilities Capabilities { get; } = new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Text],
            Operations = [ProviderOperation.Complete, ProviderOperation.Stream],
        };
    }

    private static MemoryVerificationRequest Request(params string[] ids) =>
        new("what colour is the sky", [.. ids.Select(i => new MemoryVerificationCandidate(i, $"headline {i}"))]);

    private static ScoringVerificationPolicy Policy(IModelProvider backend, int endorse = 2) =>
        new([new ChatOnly(), backend], new ScoringVerificationOptions { EndorseCount = endorse });

    [Fact]
    public async Task Endorses_the_backends_own_best_N_in_score_order_not_the_order_shown()
    {
        // index 2 scores highest and index 0 lowest, so a policy echoing the SHOWN order would return
        // a,b and this returns c,b. That is the whole point of the seam.
        var verdict = await Policy(new FakeScorer(_ => [0.10, 0.80, 0.95])).VerifyAsync(Request("a", "b", "c"));

        Assert.True(verdict.Judged);
        Assert.Equal(["c", "b"], verdict.RelevantIds);
    }

    [Fact]
    public async Task A_backend_returning_ONE_score_for_everything_preserves_the_rank_order_it_was_shown()
    {
        // A degenerate backend must DEGRADE TO A NO-OP, and here that is load-bearing rather than
        // incidental: candidates arrive in rank order, so endorsing the engine's own leading k reproduces
        // the engine's ranking under the shipped Partition. It holds only because the sort is STABLE — an
        // unstable one would promote an arbitrary k from a backend that expressed no preference, which is
        // worse than abstaining and would be invisible.
        var verdict = await Policy(new FakeScorer(_ => [0.5, 0.5, 0.5, 0.5]))
            .VerifyAsync(Request("a", "b", "c", "d"));

        Assert.Equal(["a", "b"], verdict.RelevantIds);
    }

    [Fact]
    public async Task A_backend_whose_scores_have_COLLAPSED_still_reorders_and_the_seam_cannot_flag_it()
    {
        // The dangerous shape, and the reason RerankOptions tells a deployment to check its endpoint's
        // SEPARATION rather than trust this seam. Measured on a real sub-100 MB reranker: the scores are
        // distinct and span 0.094 where a working model spans ~13.9, so they reorder by noise. Counting
        // distinct scores cannot see it — four of four here, exactly as in the healthy case above — so this
        // pins the LIMITATION, not a defect to be fixed in the policy.
        var verdict = await Policy(new FakeScorer(_ => [0.0301, 0.0299, 0.0302, 0.0300]))
            .VerifyAsync(Request("a", "b", "c", "d"));

        Assert.True(verdict.Judged);
        Assert.Equal(["c", "a"], verdict.RelevantIds);
    }

    [Fact]
    public async Task Sends_the_CONTENT_when_the_engine_supplied_it_and_the_headline_otherwise()
    {
        var backend = new FakeScorer(d => [.. d.Select(_ => 1.0)]);
        var request = new MemoryVerificationRequest("q", [
            new MemoryVerificationCandidate("a", "short headline") { Content = "the whole stored entry" },
            new MemoryVerificationCandidate("b", "headline only"),
        ]);

        await Policy(backend, endorse: 1).VerifyAsync(request);

        // Scoring a truncation instead of the text cost this arm 13 points (D108).
        Assert.Equal(["the whole stored entry", "headline only"], Assert.Single(backend.Sent));
    }

    [Fact]
    public async Task Every_scored_candidate_keeps_its_score_not_only_the_endorsed_ones()
    {
        // The rejected scores are what a caller needs to read a margin.
        var verdict = await Policy(new FakeScorer(_ => [0.1, 0.9, 0.5]), endorse: 1)
            .VerifyAsync(Request("a", "b", "c"));

        Assert.Equal(["b"], verdict.RelevantIds);
        Assert.Equal(3, verdict.Scores.Count);
        Assert.Equal(0.5, verdict.Scores["c"]);
    }

    // ---- WHICH backend, when more than one produces scores -------------------------------------------
    // Registering a second Score backend for any reason at all used to change what verified memory, decided
    // by DI registration order and reported nowhere. Both sibling seams (LlmVerificationOptions.ClientName,
    // LlmAnnotationOptions.ClientName) have always been able to say; this one could not.

    private static ScoringVerificationPolicy Policy(IReadOnlyList<IModelProvider> backends, string? providerId) =>
        new(backends, new ScoringVerificationOptions { EndorseCount = 1, ProviderId = providerId });

    [Fact]
    public async Task A_NAMED_backend_is_the_one_asked_even_when_another_was_registered_FIRST()
    {
        var first = new FakeScorer(d => [.. d.Select(_ => 1.0)], "fast");
        var named = new FakeScorer(d => [.. d.Select(_ => 1.0)], "accurate");

        await Policy([first, named], "accurate").VerifyAsync(Request("a", "b"));

        Assert.Empty(first.Sent);                       // registration order no longer decides
        Assert.Single(named.Sent);
    }

    [Fact]
    public async Task Naming_NOTHING_keeps_first_registered_wins_so_an_existing_deployment_is_untouched()
    {
        var first = new FakeScorer(d => [.. d.Select(_ => 1.0)], "fast");
        var second = new FakeScorer(d => [.. d.Select(_ => 1.0)], "accurate");

        await Policy([first, second], providerId: null).VerifyAsync(Request("a", "b"));

        Assert.Single(first.Sent);
        Assert.Empty(second.Sent);
    }

    [Fact]
    public void Naming_a_backend_NOBODY_REGISTERED_throws_where_it_is_composed()
    {
        // It must not be a NoOpinion at first recall: that is the silent degradation the option exists to
        // remove, and it would read as "the reranker had no opinion" rather than "you named a typo".
        var error = Assert.Throws<InvalidOperationException>(
            () => Policy([new FakeScorer(_ => [1.0], "accurate")], "acurate"));

        Assert.Contains("acurate", error.Message, StringComparison.Ordinal);
        Assert.Contains("accurate", error.Message, StringComparison.Ordinal);   // says what IS registered
    }

    [Fact]
    public void Naming_a_backend_that_does_NOT_produce_scores_throws_too()
    {
        // The likelier typo of the two: a real id that happens to be the chat model. The Supports filter
        // would skip it and report NoOpinion forever.
        var error = Assert.Throws<InvalidOperationException>(() => Policy([new ChatOnly()], "chat"));

        Assert.Contains(ProviderKinds.Score, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_named_backend_is_matched_case_insensitively_like_every_other_id_lookup_here()
    {
        // TextRouter, GenerationRouter, IToolRegistry and BoundedProviderPool all fold case; an ordinal
        // table here would make a backend reachable by the router and invisible to this seam.
        _ = Policy([new FakeScorer(_ => [1.0], "Accurate")], "accurate");
    }

    // ---- fail-open, and the one case that must NOT ---------------------------------------------------

    [Fact]
    public async Task NO_backend_produces_scores_so_the_ranking_is_left_exactly_as_it_was()
    {
        var policy = new ScoringVerificationPolicy([new ChatOnly()], new ScoringVerificationOptions());

        Assert.False((await policy.VerifyAsync(Request("a", "b"))).Judged);
    }

    [Fact]
    public async Task A_backend_that_THROWS_is_no_opinion_rather_than_a_failed_recall()
    {
        // The provider throws by contract — there is no score meaning "I could not". Failing open is this
        // layer's decision, which is exactly what could not be expressed while the two were one class.
        var verdict = await Policy(new FakeScorer(_ => throw new InvalidOperationException("malformed")))
            .VerifyAsync(Request("a"));

        // Judged=false is the load-bearing half: an EMPTY endorsement means "none of these answered",
        // which is a real judgement and would let the engine drop results under VerificationFilters.
        Assert.False(verdict.Judged);
    }

    /// <summary>Records the LEVEL each line was written at, which is the whole subject of the pair below.</summary>
    private sealed class CapturingLogger : ILogger<ScoringVerificationPolicy>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error,
            Func<TState, Exception?, string> formatter) => Levels.Add(level);
    }

    private static async Task<List<LogLevel>> LevelsFrom(Exception thrown)
    {
        var logger = new CapturingLogger();
        var policy = new ScoringVerificationPolicy(
            [new FakeScorer(_ => throw thrown)], new ScoringVerificationOptions(), logger);

        await policy.VerifyAsync(Request("a"));
        return logger.Levels;
    }

    /// <summary>The wiring defect this seam used to report at Warning on EVERY recall is now refused once,
    /// at composition, by <c>AddLyntai</c> — a backend declaring ProviderKinds.Score without implementing
    /// IScoreProvider cannot reach a deployment at all (D153). Pinned by
    /// <c>SemanticMemoryWiringTests.A_backend_that_DECLARES_vectors_without_implementing_the_seam_is_refused</c>
    /// for the vector half; both kinds go through one guard.
    ///
    /// <para>What remains here is the TRANSIENT case, which stays at debug: a transport blip is per-recall
    /// noise, not a defect, and the two must not log alike.</para></summary>
    [Fact]
    public async Task A_TRANSIENT_failure_stays_at_DEBUG_rather_than_crying_wolf_every_recall()
    {
        Assert.DoesNotContain(LogLevel.Warning, await LevelsFrom(new HttpRequestException("connection reset")));
        Assert.DoesNotContain(LogLevel.Warning, await LevelsFrom(new InvalidOperationException("malformed")));
    }

    [Fact]
    public async Task A_short_answer_is_NO_OPINION_rather_than_a_mis_paired_endorsement()
    {
        // Pairing by position is only sound when the counts agree; a silent truncation would endorse the
        // wrong candidates rather than none.
        var verdict = await Policy(new FakeScorer(_ => [0.9])).VerifyAsync(Request("a", "b", "c"));

        Assert.False(verdict.Judged);
    }

    [Fact]
    public async Task An_empty_candidate_list_asks_no_backend_at_all()
    {
        var backend = new FakeScorer(_ => [1.0]);

        var verdict = await Policy(backend).VerifyAsync(new MemoryVerificationRequest("q", []));

        Assert.False(verdict.Judged);
        Assert.Empty(backend.Sent);
    }

    [Fact]
    public async Task The_CALLERS_cancellation_is_re_thrown_rather_than_swallowed_as_no_opinion()
    {
        // The distinction the 2026-09-09 fail-open sweep drew: a component's own timeout is NoOpinion, but
        // the caller's cancel belongs to the caller and must propagate. Both arrive as the same exception
        // type, so this can only be told apart by ct.IsCancellationRequested.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Policy(new FakeScorer(_ => [1.0])).VerifyAsync(Request("a"), cts.Token));
    }

    [Fact]
    public async Task A_component_timeout_is_NO_OPINION_and_does_not_fail_the_recall()
    {
        // Same exception type as the test above, uncancelled token: this one must NOT propagate.
        var verdict = await Policy(new FakeScorer(_ => throw new TaskCanceledException()))
            .VerifyAsync(Request("a"));

        Assert.False(verdict.Judged);
    }

    [Fact]
    public async Task A_verdict_with_NO_scores_is_distinguishable_from_one_that_scored_everything_at_zero()
    {
        // Null means the policy reported none; a populated map of zeros is a real judgement that nothing
        // resembled the query. The same distinction Judged already draws, one level down.
        Assert.Null(MemoryVerification.NoOpinion.Scores);
        Assert.Null(MemoryVerification.NothingRelevant.Scores);

        var allZero = await Policy(new FakeScorer(d => [.. d.Select(_ => 0.0)])).VerifyAsync(Request("a", "b"));

        Assert.True(allZero.Judged);
        Assert.NotNull(allZero.Scores);
        Assert.Equal(0.0, allZero.Scores!["a"]);
    }

    // ---- the seam's policy-agnostic contract ---------------------------------------------------------
    // PolicyContractCoverageTests fails until every shipped IMemoryVerificationPolicy is run through these,
    // which is what caught this policy the moment it became a second shipped implementation.

    private static ScoringVerificationPolicy Working() =>
        Policy(new FakeScorer(d => [.. d.Select((_, i) => (double)d.Count - i)]), endorse: 2);

    [Fact] public Task Never_null() => MemoryVerificationPolicyContract.It_never_returns_null(Working());

    [Fact] public Task Ids_were_shown() =>
        MemoryVerificationPolicyContract.Every_id_it_returns_was_one_it_was_shown(Working());

    [Fact] public Task No_duplicates() => MemoryVerificationPolicyContract.It_returns_no_duplicates(Working());

    [Fact] public Task Empty_candidates() =>
        MemoryVerificationPolicyContract.An_empty_candidate_set_is_no_opinion(Working());

    [Fact] public Task Fails_open() =>
        MemoryVerificationPolicyContract.A_failing_policy_yields_NoOpinion_and_not_NothingRelevant(
            Policy(new FakeScorer(_ => throw new InvalidOperationException("backend down"))));

    [Fact] public Task Its_own_timeout_fails_open() =>
        MemoryVerificationPolicyContract.A_policy_timing_out_on_its_own_yields_NoOpinion(
            Policy(new FakeScorer(_ => throw new TaskCanceledException())));

    [Fact] public Task Cancellation_propagates() =>
        MemoryVerificationPolicyContract.Cancellation_propagates_rather_than_becoming_no_opinion(Working());
}
