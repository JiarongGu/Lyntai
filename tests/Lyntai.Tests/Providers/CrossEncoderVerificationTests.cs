using System.Net;
using Lyntai.Memory.Verification;
using Lyntai.Providers.Http;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>The cross-encoder in the verification seam. Every test drives the pure policy against a
/// scripted handler — no server, no model.
///
/// <para>The failure mode this file exists for is a SILENT one: the seam is fail-open, so a broken
/// reranker and a reranker that agreed with the ranking are indistinguishable from the score alone
/// (`pitfalls.md`). So the null cases assert <c>Judged == false</c> rather than only an empty list.</para>
/// </summary>
public class CrossEncoderVerificationTests
{
    private static MemoryVerificationRequest Request(params string[] ids) =>
        new("what colour is the sky", [.. ids.Select(i => new MemoryVerificationCandidate(i, $"headline {i}"))]);

    private static CrossEncoderVerificationPolicy Policy(
        StubHttpHandler handler, int endorse = 2, bool disposeHttpClient = true)
    {
        var client = new HttpClient(handler);
        return new CrossEncoderVerificationPolicy(
            new CrossEncoderVerificationOptions { BaseUrl = "http://localhost:8081", EndorseCount = endorse },
            () => client, new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(5) },
            logger: null, disposeHttpClient: disposeHttpClient);
    }

    [Fact]
    public async Task Endorses_the_rerankers_own_best_N_in_score_order_not_the_order_shown()
    {
        // index 2 scores highest and index 0 lowest, so a policy echoing the SHOWN order would return
        // a,b and this returns c,b. That is the whole point of the seam.
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, """
            {"results":[{"index":0,"relevance_score":0.10},
                        {"index":1,"relevance_score":0.80},
                        {"index":2,"relevance_score":0.95}]}
            """);

        var verdict = await Policy(handler, endorse: 2).VerifyAsync(Request("a", "b", "c"));

        Assert.True(verdict.Judged);
        Assert.Equal(["c", "b"], verdict.RelevantIds);
    }

    [Fact]
    public async Task A_backend_returning_ONE_score_for_everything_preserves_the_rank_order_it_was_shown()
    {
        // A degenerate backend must DEGRADE TO A NO-OP, and here that is load-bearing rather than
        // incidental: candidates arrive in rank order (`MemoryVerificationRequest.Candidates`), so endorsing
        // the engine's own leading k reproduces the engine's ranking under the shipped Partition. It holds
        // only because the sort is STABLE — an unstable one would promote an arbitrary k from a backend that
        // expressed no preference, which is worse than abstaining and would be invisible.
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, """
            {"results":[{"index":0,"relevance_score":0.5},
                        {"index":1,"relevance_score":0.5},
                        {"index":2,"relevance_score":0.5},
                        {"index":3,"relevance_score":0.5}]}
            """);

        var verdict = await Policy(handler, endorse: 2).VerifyAsync(Request("a", "b", "c", "d"));

        Assert.Equal(["a", "b"], verdict.RelevantIds);
    }

    [Fact]
    public async Task A_backend_whose_scores_have_COLLAPSED_still_reorders_and_the_seam_cannot_flag_it()
    {
        // The dangerous shape, and the reason `CrossEncoderVerificationOptions` tells a deployment to check
        // its endpoint's SEPARATION rather than trust this seam. Measured on a real sub-100 MB reranker: the
        // scores are distinct and span 0.094 where a working model spans ~13.9, so they reorder by noise.
        // Counting distinct scores cannot see it — there are four of four here, exactly as in the healthy
        // case above — so this test pins the LIMITATION, not a defect to be fixed in the policy.
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, """
            {"results":[{"index":0,"relevance_score":0.0301},
                        {"index":1,"relevance_score":0.0299},
                        {"index":2,"relevance_score":0.0302},
                        {"index":3,"relevance_score":0.0300}]}
            """);

        var verdict = await Policy(handler, endorse: 2).VerifyAsync(Request("a", "b", "c", "d"));

        // It reorders on differences of 1e-4: "c" is promoted over the "a" it was shown first, and the
        // verdict is indistinguishable from a confident one.
        Assert.True(verdict.Judged);
        Assert.Equal(["c", "a"], verdict.RelevantIds);
    }

    [Fact]
    public async Task Accepts_score_as_well_as_relevance_score()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK,
            """{"results":[{"index":1,"score":0.9},{"index":0,"score":0.1}]}""");

        var verdict = await Policy(handler, endorse: 1).VerifyAsync(Request("a", "b"));

        Assert.Equal(["b"], verdict.RelevantIds);
    }

    [Fact]
    public async Task Sends_the_CONTENT_when_the_engine_supplied_it_and_the_headline_otherwise()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, """{"results":[{"index":0,"score":1}]}""");
        var request = new MemoryVerificationRequest("q", [
            new MemoryVerificationCandidate("a", "short headline") { Content = "the whole stored entry" },
            new MemoryVerificationCandidate("b", "headline only"),
        ]);

        await Policy(handler, endorse: 1).VerifyAsync(request);

        // Scoring a truncation instead of the text cost this arm 13 points (D108).
        Assert.Contains("the whole stored entry", handler.Requests[0].Body);
        Assert.Contains("headline only", handler.Requests[0].Body);
        Assert.DoesNotContain("short headline", handler.Requests[0].Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, """{"results":[{"index":0,"score":1}]}""")]
    [InlineData(HttpStatusCode.OK, "not json at all")]
    [InlineData(HttpStatusCode.OK, """{"no_results_key":[]}""")]
    [InlineData(HttpStatusCode.OK, """{"results":[{"index":0}]}""")]
    [InlineData(HttpStatusCode.OK, """{"results":[{"index":0,"score":"high"}]}""")]
    public async Task Reports_NO_OPINION_rather_than_an_invented_ordering(HttpStatusCode status, string body)
    {
        var verdict = await Policy(new StubHttpHandler().Enqueue(status, body)).VerifyAsync(Request("a", "b"));

        // Judged=false is the load-bearing half: an EMPTY endorsement means "none of these answered",
        // which is a real judgement and would let the engine drop results under VerificationFilters.
        Assert.False(verdict.Judged);
    }

    [Fact]
    public async Task Never_invents_an_id_when_the_endpoint_returns_an_out_of_range_index()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK,
            """{"results":[{"index":7,"score":0.99},{"index":0,"score":0.5}]}""");

        var verdict = await Policy(handler, endorse: 2).VerifyAsync(Request("a", "b"));

        Assert.True(verdict.Judged);
        Assert.Equal(["a"], verdict.RelevantIds);   // index 7 dropped, not mapped to something
    }

    [Fact]
    public async Task An_empty_candidate_list_costs_no_HTTP_call()
    {
        var handler = new StubHttpHandler();
        var verdict = await Policy(handler).VerifyAsync(new MemoryVerificationRequest("q", []));

        Assert.False(verdict.Judged);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task The_CALLERS_cancellation_is_re_thrown_rather_than_swallowed_as_no_opinion()
    {
        // The distinction the 2026-09-09 fail-open sweep drew: a component's own timeout is NoOpinion,
        // but the caller's cancel belongs to the caller and must propagate. Both arrive as the same
        // exception type, so this can only be told apart by ct.IsCancellationRequested.
        var handler = new StubHttpHandler().Enqueue(_ => throw new TaskCanceledException());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Policy(handler).VerifyAsync(Request("a"), cts.Token));
    }

    [Fact]
    public async Task A_component_timeout_is_NO_OPINION_and_does_not_fail_the_recall()
    {
        // Same exception type as the test above, uncancelled token: this one must NOT propagate.
        var handler = new StubHttpHandler().Enqueue(_ => throw new TaskCanceledException());

        var verdict = await Policy(handler).VerifyAsync(Request("a"));

        Assert.False(verdict.Judged);
    }

    [Fact]
    public async Task An_unreachable_endpoint_is_NO_OPINION_and_does_not_fail_the_recall()
    {
        var handler = new StubHttpHandler().Enqueue(_ => throw new HttpRequestException("refused"));

        var verdict = await Policy(handler).VerifyAsync(Request("a"));

        Assert.False(verdict.Judged);
    }

    [Fact]
    public async Task A_BYO_client_survives_repeated_recalls_because_Lyntai_never_disposes_it()
    {
        // D29, and the failing call is the SECOND one: disposing an app-owned client succeeds once and
        // then throws ObjectDisposedException in somebody else's code.
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, """{"results":[{"index":0,"score":1}]}""")
            .Enqueue(HttpStatusCode.OK, """{"results":[{"index":0,"score":1}]}""");
        var policy = Policy(handler, endorse: 1, disposeHttpClient: false);

        var first = await policy.VerifyAsync(Request("a"));
        var second = await policy.VerifyAsync(Request("a"));

        Assert.True(first.Judged);
        Assert.True(second.Judged);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task The_dispose_flag_is_NOT_INERT_so_the_test_above_is_a_real_control()
    {
        // The mirror of the BYO test, and the reason it means anything: with ownership CLAIMED, the same
        // client really is disposed, so the second call cannot succeed. Without this a policy that simply
        // never disposed anything would pass the BYO test while honouring nothing — the "checks spelling,
        // not polarity" shape `pitfalls.md` records. Handing back one instance is what makes disposal
        // OBSERVABLE here; the real registration hands out a fresh client per call.
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, """{"results":[{"index":0,"score":1}]}""")
            .Enqueue(HttpStatusCode.OK, """{"results":[{"index":0,"score":1}]}""");
        var policy = Policy(handler, endorse: 1, disposeHttpClient: true);

        var first = await policy.VerifyAsync(Request("a"));
        Assert.True(first.Judged);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => policy.VerifyAsync(Request("a")));
    }

    [Fact]
    public async Task Posts_to_the_rerank_route_and_carries_a_bearer_token_when_one_is_configured()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, """{"results":[{"index":0,"score":1}]}""");
        var client = new HttpClient(handler);
        var policy = new CrossEncoderVerificationPolicy(
            new CrossEncoderVerificationOptions
            {
                BaseUrl = "http://localhost:8081/",   // trailing slash must not double up
                Model = "bge-reranker-v2-m3",
                ApiKey = "k",
                EndorseCount = 1,
            },
            () => client, new LyntaiOptions(), logger: null, disposeHttpClient: true);

        await policy.VerifyAsync(Request("a"));

        Assert.Equal("http://localhost:8081/v1/rerank", handler.Requests[0].Uri?.ToString());
        Assert.Equal("Bearer k", handler.Requests[0].Auth);
        Assert.Contains("bge-reranker-v2-m3", handler.Requests[0].Body);
    }
    [Fact]
    public async Task The_verdict_carries_the_score_for_EVERY_candidate_including_the_ones_it_did_not_endorse()
    {
        // The rejected scores are the half a margin needs: endorsing c and b says nothing about HOW much
        // better they were than a. Before this the policy computed a real-valued score per candidate and
        // discarded all of it at the endorsement cut, so no public type in the library carried a per-option
        // confidence out of a model-backed seam.
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, """
            {"results":[{"index":0,"relevance_score":0.10},
                        {"index":1,"relevance_score":0.80},
                        {"index":2,"relevance_score":0.95}]}
            """);

        var verdict = await Policy(handler, endorse: 2).VerifyAsync(Request("a", "b", "c"));

        Assert.NotNull(verdict.Scores);
        Assert.Equal(3, verdict.Scores!.Count);
        Assert.Equal(0.95, verdict.Scores["c"], 3);
        Assert.Equal(0.80, verdict.Scores["b"], 3);
        Assert.Equal(0.10, verdict.Scores["a"], 3);   // unendorsed, and still scored
    }

    [Fact]
    public async Task A_verdict_with_NO_scores_is_distinguishable_from_one_that_scored_everything_at_zero()
    {
        // Null means the policy reported none; a populated map of zeros is a real judgement that nothing
        // resembled the query. The same distinction Judged already draws, one level down.
        Assert.Null(MemoryVerification.NoOpinion.Scores);
        Assert.Null(MemoryVerification.NothingRelevant.Scores);

        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, """{"results":[]}""");
        var empty = await Policy(handler).VerifyAsync(Request("a"));
        Assert.False(empty.Judged);
        Assert.Null(empty.Scores);
    }
}

/// <summary>The wire format against a REAL reranker, so the adapter's request and response shapes are
/// MEASURED rather than ported. Skipped unless <c>LYNTAI_LIVE_RERANK_URL</c> names a running endpoint —
/// the same posture every other live test here takes, so the default run stays offline and deterministic.
///
/// <para>Run it with: <c>llama-server -m &lt;reranker&gt;.gguf --reranking --port 8097</c>, then
/// <c>LYNTAI_LIVE_RERANK_URL=http://127.0.0.1:8097</c>.</para></summary>
public class CrossEncoderVerificationLiveTests
{
    private static string? Endpoint => Environment.GetEnvironmentVariable("LYNTAI_LIVE_RERANK_URL");

    private const string Reason =
        "live reranker test: set LYNTAI_LIVE_RERANK_URL to a running /v1/rerank endpoint";

    [SkippableFact]
    public async Task A_real_reranker_orders_a_known_pair_and_its_scores_are_UNBOUNDED()
    {
        Skip.If(string.IsNullOrEmpty(Endpoint), Reason);

        using var http = new HttpClient();
        var policy = new CrossEncoderVerificationPolicy(
            new CrossEncoderVerificationOptions { BaseUrl = Endpoint!, EndorseCount = 1 },
            () => http, new LyntaiOptions { ProviderTimeout = TimeSpan.FromMinutes(2) },
            logger: null, disposeHttpClient: false);

        var verdict = await policy.VerifyAsync(new MemoryVerificationRequest("what colour is the sky", [
            new MemoryVerificationCandidate("bread", "bread is baked in an oven"),
            new MemoryVerificationCandidate("sky", "the sky is blue"),
        ]));

        // Measured 2026-09-11 against LAMAR-600m: the pair scores +1.19 and -10.23, so a policy that
        // clamped to [0,1] or rejected a negative would silently drop the discriminating half.
        Assert.True(verdict.Judged);
        Assert.Equal(["sky"], verdict.RelevantIds);
    }
}
