using System.Net;
using Lyntai.Inference;
using Lyntai.Providers.Http;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>The <c>/v1/rerank</c> wire shape, reached through a registration declaring
/// <see cref="ProviderKinds.Score"/> — no server, no model.
///
/// <para><b>A rerank backend is a backend like any other (D140)</b>: the same `AddHttpProvider` with a
/// different <c>Produces</c>, the same options, the same client lifetime. What it is FOR — how many to
/// endorse, which field of a candidate to send — is the caller's, and lives in
/// <c>ScoringVerificationPolicyTests</c>, which needs no HTTP at all.</para>
///
/// <para><b>Everything here THROWS rather than degrading</b>, because there is no score meaning "I could
/// not" and a zero ranks as confidently as a real one. Failing open is a decision for whoever is
/// asking.</para></summary>
public class HttpRerankTransportTests
{
    private static HttpModelProvider Scorer(StubHttpHandler handler, bool dispose = true) =>
        new("rerank",
            new HttpModelOptions
            {
                BaseUrl = "http://localhost:8081",
                Produces = ProviderKinds.Score,
            },
            () => new HttpClient(handler, disposeHandler: false),
            new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(5) },
            logger: null, disposeHttpClient: dispose);

    [Fact]
    public void A_score_backend_declares_the_kind_and_no_stream()
    {
        var caps = Scorer(new StubHttpHandler()).Capabilities;

        Assert.Equal([ProviderKinds.Score], caps.Produces);
        Assert.True(caps.Supports(ProviderKinds.Score, ProviderOperation.Complete, accepts: ProviderKinds.Text));
        Assert.False(caps.Supports(ProviderKinds.Score, ProviderOperation.Stream));
        Assert.False(caps.SupportsToolCalls);
    }

    [Fact]
    public async Task Scores_come_back_in_INPUT_order_though_the_endpoint_answers_SORTED()
    {
        // The contract that lets a caller pair scores with the documents it holds. The endpoint replies
        // best-first and carries its own indices; putting them back is the backend's job.
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, """
            {"results":[{"index":2,"relevance_score":0.95},
                        {"index":1,"relevance_score":0.80},
                        {"index":0,"relevance_score":0.10}]}
            """);

        var scores = await Scorer(handler).ScoreAsync("q", ["a", "b", "c"]);

        Assert.Equal([0.10, 0.80, 0.95], scores);
    }

    [Fact]
    public async Task Accepts_score_as_well_as_relevance_score()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK,
            """{"results":[{"index":1,"score":0.9},{"index":0,"score":0.1}]}""");

        Assert.Equal([0.1, 0.9], await Scorer(handler).ScoreAsync("q", ["a", "b"]));
    }

    [Fact]
    public async Task Posts_every_document_and_asks_for_a_score_for_each()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK,
            """{"results":[{"index":0,"score":1},{"index":1,"score":2}]}""");

        await Scorer(handler).ScoreAsync("what colour is the sky", ["first", "second"]);

        var body = Assert.Single(handler.Requests).Body;
        Assert.Contains("what colour is the sky", body, StringComparison.Ordinal);
        Assert.Contains("\"top_n\":2", body, StringComparison.Ordinal);
        Assert.Equal("http://localhost:8081/v1/rerank", handler.Requests[0].Uri?.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "not json at all")]
    [InlineData(HttpStatusCode.OK, """{"no_results_key":[]}""")]
    [InlineData(HttpStatusCode.OK, """{"results":[{"index":0}]}""")]
    [InlineData(HttpStatusCode.OK, """{"results":[{"index":0,"score":"high"}]}""")]
    // an index the caller never sent is unusable, and trusting the REST of that answer is the fail-open
    // direction — it used to be dropped, leaving the other documents scored and one silently absent
    [InlineData(HttpStatusCode.OK, """{"results":[{"index":7,"score":0.99},{"index":0,"score":0.5}]}""")]
    // a document left unscored would read as 0.0, which ranks as confidently as a real score
    [InlineData(HttpStatusCode.OK, """{"results":[{"index":0,"score":0.5}]}""")]
    public async Task A_malformed_or_incomplete_answer_FAILS_rather_than_ranking_on_holes(
        HttpStatusCode status, string body)
    {
        var scorer = Scorer(new StubHttpHandler().Enqueue(status, body));

        var response = await scorer.CallAsync(new ScoreRequest("q", ["a", "b"]));

        Assert.NotEqual(ProviderVerdict.Ok, response.Verdict);
        Assert.Empty(response.Scores);
    }

    [Fact]
    public async Task A_non_2xx_status_is_a_verdict_that_names_it()
    {
        var scorer = Scorer(new StubHttpHandler().Enqueue(HttpStatusCode.InternalServerError, "upstream died"));

        var response = await scorer.CallAsync(new ScoreRequest("q", ["a"]));

        Assert.Equal(ProviderVerdict.Failed, response.Verdict);
        Assert.Contains("500", response.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_429_is_RATE_LIMITED_so_a_second_reranker_can_take_over()
    {
        // Before D153 a rate-limited reranker threw, the verification seam reported NoOpinion, and the very
        // next recall asked the same exhausted host again -- there was no cooldown for it to reach.
        var scorer = Scorer(new StubHttpHandler().Enqueue(HttpStatusCode.TooManyRequests, "slow down"));

        var response = await scorer.CallAsync(new ScoreRequest("q", ["a"]));

        Assert.Equal(ProviderVerdict.RateLimited, response.Verdict);
    }

    [Fact]
    public async Task An_input_over_the_models_window_is_CONTEXT_WINDOW_EXCEEDED_not_a_host_fault()
    {
        // A host-fault verdict counts toward benching a healthy reranker; this one is about the INPUT.
        var scorer = Scorer(new StubHttpHandler().Enqueue(HttpStatusCode.BadRequest, """
            {"error":{"code":400,"message":"input (1052 tokens) is larger than the max context size (512 tokens). skipping","type":"invalid_request_error"}}
            """));

        var response = await scorer.CallAsync(new ScoreRequest("q", ["a"]));

        Assert.Equal(ProviderVerdict.ContextWindowExceeded, response.Verdict);
    }

    [Fact]
    public async Task An_empty_document_list_costs_no_HTTP_call()
    {
        var handler = new StubHttpHandler();

        Assert.Empty(await Scorer(handler).ScoreAsync("q", []));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task The_CALLERS_cancellation_is_re_thrown_rather_than_reported_as_a_timeout()
    {
        // Both arrive as OperationCanceledException, so this can only be told apart by
        // ct.IsCancellationRequested — never by the exception's type.
        var handler = new StubHttpHandler().Enqueue(_ => throw new TaskCanceledException());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Scorer(handler).ScoreAsync("q", ["a"], cts.Token));
    }

    [Fact]
    public async Task A_component_timeout_is_a_TIMEOUT_verdict_not_the_callers_cancellation()
    {
        var handler = new StubHttpHandler().Enqueue(_ => throw new TaskCanceledException());

        var response = await Scorer(handler).CallAsync(new ScoreRequest("q", ["a"]));

        Assert.Equal(ProviderVerdict.Timeout, response.Verdict);
    }

    [Fact]
    public async Task A_BYO_client_survives_repeated_calls_because_Lyntai_never_disposes_it()
    {
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, """{"results":[{"index":0,"score":1}]}""");
        var scorer = Scorer(handler, dispose: false);

        await scorer.ScoreAsync("q", ["a"]);
        await scorer.ScoreAsync("q", ["a"]);   // a disposed client would throw here

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task The_dispose_flag_is_NOT_INERT_so_the_test_above_is_a_real_control()
    {
        // The mirror of the BYO test, and the reason it means anything: with ownership CLAIMED, the same
        // client really is disposed, so the second call cannot succeed. Without this a transport that simply
        // never disposed anything would pass the BYO test while honouring nothing — the "checks spelling,
        // not polarity" shape `pitfalls.md` records. Handing back one instance is what makes disposal
        // OBSERVABLE here; the real registration hands out a fresh client per call.
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, """{"results":[{"index":0,"score":1}]}""");
        var client = new HttpClient(handler, disposeHandler: false);
        var scorer = new HttpModelProvider("rerank",
            new HttpModelOptions { BaseUrl = "http://localhost:8081", Produces = ProviderKinds.Score },
            () => client, new LyntaiOptions(), logger: null, disposeHttpClient: true);

        await scorer.ScoreAsync("q", ["a"]);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => scorer.ScoreAsync("q", ["a"]));
    }

    [Fact]
    public async Task Carries_a_bearer_token_and_the_model_when_configured_without_doubling_the_slash()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, """{"results":[{"index":0,"score":1}]}""");
        var scorer = new HttpModelProvider("rerank",
            new HttpModelOptions
            {
                BaseUrl = "http://localhost:8081/",        // trailing slash must not double up
                Produces = ProviderKinds.Score,
                Model = "bge-reranker-v2-m3",
                ApiKey = "k",
            },
            () => new HttpClient(handler, disposeHandler: false), new LyntaiOptions());

        await scorer.ScoreAsync("q", ["a"]);

        Assert.Equal("http://localhost:8081/v1/rerank", handler.Requests[0].Uri?.ToString());
        Assert.Equal("Bearer k", handler.Requests[0].Auth);
        Assert.Contains("bge-reranker-v2-m3", handler.Requests[0].Body, StringComparison.Ordinal);
    }
}

/// <summary>The wire format against a REAL reranker, so the request and response shapes are MEASURED rather
/// than ported. Skipped unless <c>LYNTAI_LIVE_RERANK_URL</c> names a running endpoint — the same posture
/// every other live test here takes, so the default run stays offline and deterministic.
///
/// <para>Run it with: <c>llama-server -m &lt;reranker&gt;.gguf --reranking --port 8097</c>, then
/// <c>LYNTAI_LIVE_RERANK_URL=http://127.0.0.1:8097</c>.</para></summary>
public class HttpRerankLiveTests
{
    private static string? Endpoint => Environment.GetEnvironmentVariable("LYNTAI_LIVE_RERANK_URL");

    private const string Reason =
        "live reranker test: set LYNTAI_LIVE_RERANK_URL to a running /v1/rerank endpoint";

    [SkippableFact]
    public async Task A_real_reranker_orders_a_known_pair_and_its_scores_are_UNBOUNDED()
    {
        Skip.If(string.IsNullOrEmpty(Endpoint), Reason);

        using var http = new HttpClient();
        var scorer = new HttpModelProvider("rerank",
            new HttpModelOptions { BaseUrl = Endpoint!, Produces = ProviderKinds.Score },
            () => http, new LyntaiOptions { ProviderTimeout = TimeSpan.FromMinutes(2) },
            logger: null, disposeHttpClient: false);

        var scores = await scorer.ScoreAsync("what colour is the sky",
            ["bread is baked in an oven", "the sky is blue"]);

        // Measured 2026-09-11 against LAMAR-600m: the pair scores +1.19 and -10.23, so anything that
        // clamped to [0,1] or rejected a negative would silently drop the discriminating half. Asserting
        // the ORDER rather than the values keeps this true of any working reranker.
        Assert.Equal(2, scores.Count);
        Assert.True(scores[1] > scores[0], $"expected the sky to outscore bread; got {scores[0]} / {scores[1]}");
    }
}
