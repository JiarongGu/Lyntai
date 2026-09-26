using System.Net;
using System.Text.Json.Nodes;
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
/// <para><b>Every failure here is a non-Ok VERDICT with no scores, never a degraded ranking</b>, because
/// there is no score meaning "I could not" and a zero ranks as confidently as a real one. Failing open is
/// a decision for whoever is asking.</para></summary>
public class HttpRerankTransportTests
{
    private static HttpModelProvider Scorer(StubHttpHandler handler, bool dispose = true,
        Action<HttpModelOptions>? configure = null)
    {
        var options = new HttpModelOptions { BaseUrl = "http://localhost:8081", Produces = ProviderKinds.Score };
        configure?.Invoke(options);
        return new("rerank", options,
            () => new HttpClient(handler, disposeHandler: false),
            new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(5) },
            logger: null, disposeHttpClient: dispose);
    }

    /// <summary>A reranker that scores each document it is SENT with <paramref name="score"/> and answers
    /// sorted best-first, as real endpoints do.</summary>
    private static StubHttpHandler Reranker(Func<string, double> score) =>
        new StubHttpHandler().Enqueue(request =>
        {
            var sent = Sent(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var results = new JsonArray([.. sent
                .Select((d, i) => (Index: i, Score: score(d)))
                .OrderByDescending(r => r.Score)
                .Select(r => (JsonNode)new JsonObject { ["index"] = r.Index, ["relevance_score"] = r.Score })]);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(new JsonObject { ["results"] = results }.ToJsonString()),
            };
        });

    private static List<string> Sent(string body) =>
        [.. JsonNode.Parse(body)!["documents"]!.AsArray().Select(d => d!.GetValue<string>())];

    // twelve sentences, the needle in the seventh — in neither the first piece nor the last
    private static readonly string LongDocument = string.Join(' ', Enumerable.Range(0, 12)
        .Select(i => i == 6 ? "The needle is here." : $"Sentence number {i:00} says nothing much."));

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
    [InlineData("not json at all")]
    [InlineData("""{"no_results_key":[]}""")]
    [InlineData("""{"results":[{"index":0}]}""")]
    [InlineData("""{"results":[{"index":0,"score":"high"}]}""")]
    // an index the caller never sent is unusable, and trusting the REST of that answer is the fail-open
    // direction: the other documents scored and one silently absent
    [InlineData("""{"results":[{"index":7,"score":0.99},{"index":0,"score":0.5}]}""")]
    // a document left unscored would read as 0.0, which ranks as confidently as a real score
    [InlineData("""{"results":[{"index":0,"score":0.5}]}""")]
    public async Task A_malformed_or_incomplete_2xx_answer_FAILS_rather_than_ranking_on_holes(string body)
    {
        var scorer = Scorer(new StubHttpHandler().Enqueue(HttpStatusCode.OK, body));

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
    public async Task A_document_over_MaxInputChars_is_sent_as_PIECES_and_scores_as_its_BEST_piece()
    {
        var handler = Reranker(d => d.Contains("needle") ? 5.0 : d.Contains("alpha") ? 1.0 : -2.0);
        var scorer = Scorer(handler, configure: o => o.MaxInputChars = 60);

        var scores = await scorer.ScoreAsync("where is the needle",
            ["alpha document", LongDocument, "beta document"]);

        Assert.Equal([1.0, 5.0, -2.0], scores);   // one per DOCUMENT, in input order
        var request = Assert.Single(handler.Requests);
        var sent = Sent(request.Body);
        Assert.True(sent.Count > 3, $"the long document should travel as several pieces; sent {sent.Count}");
        Assert.All(sent, d => Assert.True(d.Length <= 60, $"a {d.Length}-character piece was sent"));
        Assert.Equal("alpha document", sent[0]);
        Assert.Equal("beta document", sent[^1]);
        Assert.Contains($"\"top_n\":{sent.Count}", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overflow_TRUNCATE_sends_each_document_cut_at_the_bound_one_per_document()
    {
        var handler = Reranker(d => d.Contains("needle") ? 5.0 : -2.0);
        var scorer = Scorer(handler, configure: o =>
        {
            o.MaxInputChars = 60;
            o.Segmentation = new InputSegmentation { Overflow = InputOverflow.Truncate };
        });

        var scores = await scorer.ScoreAsync("where is the needle", ["alpha document", LongDocument]);

        Assert.Equal([-2.0, -2.0], scores);   // the needle sat past the cut
        // the pair window holds the 19-character query, so each document has 41
        Assert.Equal(["alpha document", InputSegmenter.Truncate(LongDocument, 41)],
            Sent(Assert.Single(handler.Requests).Body));
    }

    // ---- on a reranker, MaxInputChars is the PAIR window: the query counts ----------------------------------

    private static string SentQuery(string body) => JsonNode.Parse(body)!["query"]!.GetValue<string>();

    [Fact]
    public async Task On_a_reranker_MaxInputChars_is_the_PAIR_window_so_each_piece_leaves_room_for_the_query()
    {
        const string query = "where is the needle";   // 19 characters
        var handler = Reranker(d => d.Contains("needle") ? 5.0 : -2.0);

        var scores = await Scorer(handler, configure: o => o.MaxInputChars = 60)
            .ScoreAsync(query, [LongDocument]);

        Assert.Equal([5.0], scores);
        var sent = Sent(Assert.Single(handler.Requests).Body);
        Assert.All(sent, d => Assert.True(d.Length <= 60 - query.Length, $"a {d.Length}-character piece beside the query"));
        Assert.Equal(query, SentQuery(handler.Requests[0].Body));   // within its share, untouched
    }

    [Fact]
    public async Task A_query_past_its_share_is_cut_ONCE_at_a_word_boundary_and_every_document_meets_the_same_one()
    {
        var query = string.Join(' ', Enumerable.Range(0, 20).Select(i => $"word{i:00}"));   // 139 characters
        var handler = Reranker(_ => 1.0);

        await Scorer(handler, configure: o => o.MaxInputChars = 60).ScoreAsync(query, ["short", LongDocument]);

        // the default share leaves the document 30 of the 60: the query keeps what is left, to a word
        var kept = SentQuery(Assert.Single(handler.Requests).Body);
        Assert.Equal(InputSegmenter.QueryWithin(query, 60, 0.5), kept);
        Assert.InRange(kept.Length, 15, 30);
        Assert.Equal(' ', query[kept.Length]);
        Assert.All(Sent(handler.Requests[0].Body), d => Assert.True(d.Length <= 60 - kept.Length));
    }

    [Fact]
    public async Task The_query_counts_at_its_NFKC_length_so_a_compatibility_character_shrinks_the_documents_room()
    {
        var query = string.Concat(Enumerable.Repeat("㎡", 10));   // 10 characters, 20 after NFKC
        var document = string.Join(' ', Enumerable.Range(0, 8).Select(i => $"word{i}"));   // 47 characters
        var handler = Reranker(_ => 1.0);

        await Scorer(handler, configure: o => o.MaxInputChars = 60).ScoreAsync(query, [document]);

        // 60 less 20 leaves 40, so the 47-character document is split; counted raw it would have gone whole
        var sent = Sent(Assert.Single(handler.Requests).Body);
        Assert.True(sent.Count >= 2, $"{sent.Count} piece(s)");
        Assert.All(sent, d => Assert.True(d.Length <= 40, $"a {d.Length}-character piece"));
    }

    // ---- one text element longer than the budget is cut at a code point, never sent whole ------------------

    /// <summary>ONE text element: a base with <paramref name="marks"/> combining marks after it.</summary>
    private static string Cluster(int marks) => "a" + new string('\u0301', marks);

    [Fact]
    public async Task A_QUERY_holding_one_element_past_its_share_is_cut_inside_it_and_the_call_still_answers()
    {
        var query = Cluster(600) + " where is the needle";
        var handler = Reranker(_ => 1.0);

        var response = await Scorer(handler, configure: o => o.MaxInputChars = 506)
            .CallAsync(new ScoreRequest(query, ["short", LongDocument]));

        Assert.True(response.IsOk, response.Detail);
        var kept = SentQuery(Assert.Single(handler.Requests).Body);
        Assert.InRange(InputSegmenter.Measure(kept), 1, 253);
        Assert.All(Sent(handler.Requests[0].Body),
            d => Assert.True(InputSegmenter.Measure(d) <= 506 - InputSegmenter.Measure(kept)));
    }

    [Fact]
    public async Task A_DOCUMENT_holding_one_element_past_the_budget_is_cut_inside_it_at_code_points()
    {
        var document = Cluster(700);
        var handler = Reranker(_ => 1.0);

        await Scorer(handler, configure: o => o.MaxInputChars = 101).ScoreAsync("q", [document]);

        var sent = Sent(Assert.Single(handler.Requests).Body);
        Assert.True(sent.Count >= 7, $"{sent.Count} piece(s)");
        Assert.All(sent, d => Assert.True(InputSegmenter.Measure(d) <= 100, $"a piece counting {InputSegmenter.Measure(d)}"));
        Assert.Equal(document, string.Concat(sent));   // a combining mark is no boundary, so nothing overlaps
    }

    [Fact]
    public async Task A_vanishing_MinDocumentShare_still_leaves_every_document_one_character()
    {
        var handler = Reranker(_ => 1.0);

        var response = await Scorer(handler, configure: o =>
        {
            o.MaxInputChars = 60;
            o.Segmentation = new InputSegmentation { MinDocumentShare = 1e-30 };   // below decimal's range
        }).CallAsync(new ScoreRequest(new string('x', 100), ["ok"]));

        Assert.True(response.IsOk, response.Detail);
        Assert.Equal(59, SentQuery(handler.Requests[0].Body).Length);
        Assert.Equal(1, Lyntai.Inference.InputSegmentation.DocumentShare(506, 1e-30));
    }

    [Fact]
    public async Task MaxPiecesPerInput_keeps_a_long_documents_FIRST_and_LAST_pieces_and_drops_the_middle()
    {
        var handler = Reranker(d => d.Contains("needle") ? 5.0 : -2.0);

        var scores = await Scorer(handler, configure: o =>
        {
            o.MaxInputChars = 60;
            o.Segmentation = new InputSegmentation { MaxPiecesPerInput = 2 };
        }).ScoreAsync("where is the needle", [LongDocument]);

        var all = InputSegmenter.Split(LongDocument, 41);
        Assert.Equal([all[0], all[^1]], Sent(Assert.Single(handler.Requests).Body));
        Assert.Equal([-2.0], scores);   // the needle was in a piece the cap dropped: coverage has gaps
    }

    /// <summary>One registration's calls vary — a few long candidates or many, a GPU or a CPU — so a deployment
    /// sizing a call by measured time narrows the cap per REQUEST.</summary>
    [Fact]
    public async Task A_requests_own_piece_cap_narrows_a_segmented_call()
    {
        var handler = Reranker(_ => 1.0);

        var response = await Scorer(handler, configure: o => o.MaxInputChars = 60)
            .CallAsync(new ScoreRequest("where is the needle", [LongDocument]) { MaxPiecesPerInput = 1 });

        Assert.True(response.IsOk, response.Detail);
        Assert.Equal([InputSegmenter.Split(LongDocument, 41)[0]], Sent(Assert.Single(handler.Requests).Body));
    }

    [Fact]
    public async Task A_requests_piece_cap_never_widens_the_registrations()
    {
        var handler = Reranker(_ => 1.0);

        await Scorer(handler, configure: o =>
        {
            o.MaxInputChars = 60;
            o.Segmentation = new InputSegmentation { MaxPiecesPerInput = 2 };
        }).CallAsync(new ScoreRequest("where is the needle", [LongDocument]) { MaxPiecesPerInput = 5 });

        var all = InputSegmenter.Split(LongDocument, 41);
        Assert.Equal([all[0], all[^1]], Sent(Assert.Single(handler.Requests).Body));
    }

    [Fact]
    public async Task With_no_document_over_MaxInputChars_the_request_is_BYTE_IDENTICAL_to_one_without_it()
    {
        // edge whitespace included: a bound that trimmed documents it did not need to split would show here
        string[] documents = ["  padded document  ", "ends with a newline\n", "plain"];
        var bounded = Reranker(_ => 1.0);
        var unbounded = Reranker(_ => 1.0);

        // the pair window: the one-character query beside the longest, 20-character document
        await Scorer(bounded, configure: o => o.MaxInputChars = 21).ScoreAsync("q", documents);
        await Scorer(unbounded).ScoreAsync("q", documents);

        Assert.Equal(unbounded.Requests[0].Body, bounded.Requests[0].Body);
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
