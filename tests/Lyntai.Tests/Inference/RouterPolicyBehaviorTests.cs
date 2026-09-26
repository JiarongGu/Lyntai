using Lyntai.Inference;
using Lyntai;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Inference;

/// <summary>The routing-policy behaviors on top of the router: retry-then-advance,
/// per-(provider, model) cooldown granularity, and the sole-candidate exemption.</summary>
public class RouterPolicyBehaviorTests
{
    private static TextRequest Req => new() { Messages = [TextMessage.User("hi")] };

    private static TextRouter Router(LyntaiOptions options, DeadHostTracker? tracker, params IModelProvider[] providers) =>
        new(providers, tracker ?? new DeadHostTracker(), options);

    [Fact]
    public async Task Retry_then_advance_retries_the_same_candidate_before_falling_over()
    {
        var options = new LyntaiOptions();
        options.Routing.Retry(ProviderVerdict.Failed, 2); // up to 2 retries → 3 attempts total

        var flaky = new FakeTextProvider("flaky");
        flaky.Replies.Enqueue(new TextResponse("", ProviderVerdict.Failed, Detail: "blip 1"));
        flaky.Replies.Enqueue(new TextResponse("", ProviderVerdict.Failed, Detail: "blip 2"));
        flaky.Replies.Enqueue(new TextResponse("recovered on the third try", ProviderVerdict.Ok));
        var backup = new FakeTextProvider("backup");

        var reply = await Router(options, null, flaky, backup).CompleteAsync([new("flaky"), new("backup")], Req);

        Assert.Equal("recovered on the third try", reply.Text);
        Assert.Equal(3, flaky.Calls.Count);  // original + 2 retries
        Assert.Empty(backup.Calls);          // never needed to advance
    }

    [Fact]
    public async Task Retry_budget_exhausted_then_advances()
    {
        var options = new LyntaiOptions();
        options.Routing.Retry(ProviderVerdict.Failed, 1); // 1 retry → 2 attempts, both fail

        var flaky = new FakeTextProvider("flaky");
        flaky.Replies.Enqueue(new TextResponse("", ProviderVerdict.Failed, Detail: "down 1"));
        flaky.Replies.Enqueue(new TextResponse("", ProviderVerdict.Failed, Detail: "down 2"));
        var backup = new FakeTextProvider("backup");
        backup.Replies.Enqueue(new TextResponse("from backup", ProviderVerdict.Ok));

        var reply = await Router(options, null, flaky, backup).CompleteAsync([new("flaky"), new("backup")], Req);

        Assert.Equal("from backup", reply.Text);
        Assert.Equal(2, flaky.Calls.Count); // exhausted the budget on the same candidate
    }

    [Fact]
    public async Task Rate_limited_never_retries_the_same_host_even_with_a_retry_budget()
    {
        var options = new LyntaiOptions();
        options.Routing.Retry(ProviderVerdict.RateLimited, 5); // ignored — cooled verdicts don't retry

        var limited = new FakeTextProvider("limited");
        limited.Replies.Enqueue(new TextResponse("", ProviderVerdict.RateLimited, Detail: "429"));
        var backup = new FakeTextProvider("backup");
        backup.Replies.Enqueue(new TextResponse("from backup", ProviderVerdict.Ok));

        var reply = await Router(options, null, limited, backup).CompleteAsync([new("limited"), new("backup")], Req);

        Assert.Equal("from backup", reply.Text);
        Assert.Single(limited.Calls); // one shot, then cooled and advanced
    }

    [Fact]
    public async Task Per_model_cooldown_does_not_bench_sibling_models_on_the_same_host()
    {
        var options = new LyntaiOptions();
        options.Routing.CooldownScope = CooldownScope.ProviderAndModel;
        var tracker = new DeadHostTracker(threshold: 3, TimeSpan.FromMinutes(5), () => DateTimeOffset.UtcNow);

        // one provider, two models; the small model gets rate-limited, the large one must stay live
        var host = new FakeTextProvider("host");
        host.Replies.Enqueue(new TextResponse("", ProviderVerdict.RateLimited, Detail: "429 small"));
        host.Replies.Enqueue(new TextResponse("large model served", ProviderVerdict.Ok));

        var reply = await Router(options, tracker, host).CompleteAsync([new("host", "small"), new("host", "large")], Req);

        Assert.Equal("large model served", reply.Text);
        Assert.True(tracker.IsDead("host::small"));   // only the rate-limited model is cooled
        Assert.False(tracker.IsDead("host::large"));  // its sibling is untouched
    }

    [Fact]
    public async Task Provider_scope_cooldown_benches_the_whole_host()
    {
        var options = new LyntaiOptions(); // default scope = Provider
        var tracker = new DeadHostTracker(threshold: 3, TimeSpan.FromMinutes(5), () => DateTimeOffset.UtcNow);

        var host = new FakeTextProvider("host");
        host.Replies.Enqueue(new TextResponse("", ProviderVerdict.RateLimited, Detail: "429"));
        host.Replies.Enqueue(new TextResponse("second model", ProviderVerdict.Ok));

        // first candidate rate-limited cools the whole provider; the second (same provider) is skipped,
        // no live candidate remains → the rate-limit surfaces
        var reply = await Router(options, tracker, host).CompleteAsync([new("host", "small"), new("host", "large")], Req);

        Assert.Equal(ProviderVerdict.RateLimited, reply.Verdict);
        Assert.True(tracker.IsDead("host"));
    }

    [Fact]
    public async Task Sole_candidate_is_not_benched_when_already_on_cooldown()
    {
        var options = new LyntaiOptions(); // ExemptSoleCandidate = true
        var tracker = new DeadHostTracker(threshold: 1, TimeSpan.FromMinutes(5), () => DateTimeOffset.UtcNow);
        tracker.MarkDead("only"); // already cooled

        var only = new FakeTextProvider("only");
        only.Replies.Enqueue(new TextResponse("served despite cooldown", ProviderVerdict.Ok));

        var reply = await Router(options, tracker, only).CompleteAsync([new("only")], Req);

        Assert.Equal("served despite cooldown", reply.Text); // tried anyway — benching the sole option is useless
        Assert.Single(only.Calls);
    }

    [Fact]
    public async Task Sole_candidate_exemption_can_be_disabled()
    {
        var options = new LyntaiOptions();
        options.Routing.ExemptSoleCandidate = false;
        var tracker = new DeadHostTracker(threshold: 1, TimeSpan.FromMinutes(5), () => DateTimeOffset.UtcNow);
        tracker.MarkDead("only");

        var only = new FakeTextProvider("only");
        only.Replies.Enqueue(new TextResponse("should not be reached", ProviderVerdict.Ok));

        var reply = await Router(options, tracker, only).CompleteAsync([new("only")], Req);

        Assert.Equal(ProviderVerdict.Failed, reply.Verdict); // skipped for cooldown, no live candidate
        Assert.Empty(only.Calls);
    }

    [Fact]
    public async Task Custom_action_can_make_a_verdict_surface_instead_of_falling_back()
    {
        var options = new LyntaiOptions();
        options.Routing.On(ProviderVerdict.Failed, FallbackAction.Surface); // don't fall back on Failed

        var p1 = new FakeTextProvider("p1");
        p1.Replies.Enqueue(new TextResponse("", ProviderVerdict.Failed, Detail: "surfaced"));
        var p2 = new FakeTextProvider("p2");
        p2.Replies.Enqueue(new TextResponse("should not be reached", ProviderVerdict.Ok));

        var reply = await Router(options, null, p1, p2).CompleteAsync([new("p1"), new("p2")], Req);

        Assert.Equal(ProviderVerdict.Failed, reply.Verdict);
        Assert.Empty(p2.Calls);
    }

    [Fact]
    public async Task Retries_record_only_one_dead_host_failure_per_request()
    {
        // Retry(Failed,2) + default threshold 3 must NOT bench the host after a single request:
        // the 3 attempts count as ONE failed request, so one RecordFailure, not three.
        var options = new LyntaiOptions();
        options.Routing.Retry(ProviderVerdict.Failed, 2);
        var tracker = new DeadHostTracker(threshold: 3, TimeSpan.FromMinutes(5), () => DateTimeOffset.UtcNow);

        var flaky = new FakeTextProvider("flaky");
        for (var i = 0; i < 3; i++) flaky.Replies.Enqueue(new TextResponse("", ProviderVerdict.Failed, Detail: $"blip {i}"));
        var backup = new FakeTextProvider("backup");
        backup.Replies.Enqueue(new TextResponse("from backup", ProviderVerdict.Ok));

        await Router(options, tracker, flaky, backup).CompleteAsync([new("flaky"), new("backup")], Req);

        Assert.Equal(3, flaky.Calls.Count);      // all 3 attempts happened
        Assert.False(tracker.IsDead("flaky"));   // but only ONE failure recorded — not benched
    }

    [Fact]
    public async Task Streaming_empty_content_chunks_are_never_yielded_to_the_consumer()
    {
        var options = new LyntaiOptions();
        var p = new FakeTextProvider("p")
        {
            // an empty/role-only chunk, then real content — the empty one must not leak downstream
            StreamScript = _ => [TextChunk.Content(""), TextChunk.Content("real answer"), TextChunk.Final()],
        };

        var chunks = new List<TextChunk>();
        await foreach (var c in Router(options, null, p).StreamAsync([new("p")], Req)) chunks.Add(c);

        var contents = chunks.Where(c => c.Kind == TextChunkKind.Content).ToList();
        Assert.Single(contents);                        // the empty chunk was filtered
        Assert.Equal("real answer", contents[0].Text);
        Assert.Equal(TextChunkKind.Final, chunks[^1].Kind);
    }

    [Fact]
    public async Task Streaming_retry_then_advance_reconnects_the_same_candidate_pre_content()
    {
        var options = new LyntaiOptions();
        options.Routing.Retry(ProviderVerdict.Failed, 1);

        var flaky = new FakeTextProvider("flaky");
        var attempt = 0;
        flaky.StreamScript = _ => ++attempt == 1
            ? [TextChunk.Error(ProviderVerdict.Failed, "cold start")]
            : [TextChunk.Content("second attempt streamed"), TextChunk.Final()];

        var chunks = new List<TextChunk>();
        await foreach (var c in Router(options, null, flaky).StreamAsync([new("flaky")], Req))
            chunks.Add(c);

        Assert.Equal("second attempt streamed",
            string.Concat(chunks.Where(c => c.Kind == TextChunkKind.Content).Select(c => c.Text)));
        Assert.Equal(2, flaky.StreamCalls); // reconnected the same candidate before the first token
    }
}
