using Lyntai;
using Lyntai.Llm;
using Lyntai.Llm.Routing;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Core;

/// <summary>The v0.3 routing-policy behaviors on top of the router: retry-then-advance,
/// per-(provider, model) cooldown granularity, and the sole-candidate exemption.</summary>
public class RouterPolicyBehaviorTests
{
    private static LlmRequest Req => new() { Messages = [LlmMessage.User("hi")] };

    private static LlmRouter Router(LyntaiOptions options, DeadHostTracker? tracker, params ILlmProvider[] providers) =>
        new(providers, tracker ?? new DeadHostTracker(), options);

    [Fact]
    public async Task Retry_then_advance_retries_the_same_candidate_before_falling_over()
    {
        var options = new LyntaiOptions();
        options.Routing.Retry(LlmVerdict.Failed, 2); // up to 2 retries → 3 attempts total

        var flaky = new FakeLlmProvider("flaky");
        flaky.Replies.Enqueue(new LlmReply("", LlmVerdict.Failed, Detail: "blip 1"));
        flaky.Replies.Enqueue(new LlmReply("", LlmVerdict.Failed, Detail: "blip 2"));
        flaky.Replies.Enqueue(new LlmReply("recovered on the third try", LlmVerdict.Ok));
        var backup = new FakeLlmProvider("backup");

        var reply = await Router(options, null, flaky, backup).CompleteAsync([new("flaky"), new("backup")], Req);

        Assert.Equal("recovered on the third try", reply.Text);
        Assert.Equal(3, flaky.Calls.Count);  // original + 2 retries
        Assert.Empty(backup.Calls);          // never needed to advance
    }

    [Fact]
    public async Task Retry_budget_exhausted_then_advances()
    {
        var options = new LyntaiOptions();
        options.Routing.Retry(LlmVerdict.Failed, 1); // 1 retry → 2 attempts, both fail

        var flaky = new FakeLlmProvider("flaky");
        flaky.Replies.Enqueue(new LlmReply("", LlmVerdict.Failed, Detail: "down 1"));
        flaky.Replies.Enqueue(new LlmReply("", LlmVerdict.Failed, Detail: "down 2"));
        var backup = new FakeLlmProvider("backup");
        backup.Replies.Enqueue(new LlmReply("from backup", LlmVerdict.Ok));

        var reply = await Router(options, null, flaky, backup).CompleteAsync([new("flaky"), new("backup")], Req);

        Assert.Equal("from backup", reply.Text);
        Assert.Equal(2, flaky.Calls.Count); // exhausted the budget on the same candidate
    }

    [Fact]
    public async Task Rate_limited_never_retries_the_same_host_even_with_a_retry_budget()
    {
        var options = new LyntaiOptions();
        options.Routing.Retry(LlmVerdict.RateLimited, 5); // ignored — cooled verdicts don't retry

        var limited = new FakeLlmProvider("limited");
        limited.Replies.Enqueue(new LlmReply("", LlmVerdict.RateLimited, Detail: "429"));
        var backup = new FakeLlmProvider("backup");
        backup.Replies.Enqueue(new LlmReply("from backup", LlmVerdict.Ok));

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
        var host = new FakeLlmProvider("host");
        host.Replies.Enqueue(new LlmReply("", LlmVerdict.RateLimited, Detail: "429 small"));
        host.Replies.Enqueue(new LlmReply("large model served", LlmVerdict.Ok));

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

        var host = new FakeLlmProvider("host");
        host.Replies.Enqueue(new LlmReply("", LlmVerdict.RateLimited, Detail: "429"));
        host.Replies.Enqueue(new LlmReply("second model", LlmVerdict.Ok));

        // first candidate rate-limited cools the whole provider; the second (same provider) is skipped,
        // no live candidate remains → the rate-limit surfaces
        var reply = await Router(options, tracker, host).CompleteAsync([new("host", "small"), new("host", "large")], Req);

        Assert.Equal(LlmVerdict.RateLimited, reply.Verdict);
        Assert.True(tracker.IsDead("host"));
    }

    [Fact]
    public async Task Sole_candidate_is_not_benched_when_already_on_cooldown()
    {
        var options = new LyntaiOptions(); // ExemptSoleCandidate = true
        var tracker = new DeadHostTracker(threshold: 1, TimeSpan.FromMinutes(5), () => DateTimeOffset.UtcNow);
        tracker.MarkDead("only"); // already cooled

        var only = new FakeLlmProvider("only");
        only.Replies.Enqueue(new LlmReply("served despite cooldown", LlmVerdict.Ok));

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

        var only = new FakeLlmProvider("only");
        only.Replies.Enqueue(new LlmReply("should not be reached", LlmVerdict.Ok));

        var reply = await Router(options, tracker, only).CompleteAsync([new("only")], Req);

        Assert.Equal(LlmVerdict.Failed, reply.Verdict); // skipped for cooldown, no live candidate
        Assert.Empty(only.Calls);
    }

    [Fact]
    public async Task Custom_action_can_make_a_verdict_surface_instead_of_falling_back()
    {
        var options = new LyntaiOptions();
        options.Routing.On(LlmVerdict.Failed, FallbackAction.Surface); // don't fall back on Failed

        var p1 = new FakeLlmProvider("p1");
        p1.Replies.Enqueue(new LlmReply("", LlmVerdict.Failed, Detail: "surfaced"));
        var p2 = new FakeLlmProvider("p2");
        p2.Replies.Enqueue(new LlmReply("should not be reached", LlmVerdict.Ok));

        var reply = await Router(options, null, p1, p2).CompleteAsync([new("p1"), new("p2")], Req);

        Assert.Equal(LlmVerdict.Failed, reply.Verdict);
        Assert.Empty(p2.Calls);
    }

    [Fact]
    public async Task Streaming_retry_then_advance_reconnects_the_same_candidate_pre_content()
    {
        var options = new LyntaiOptions();
        options.Routing.Retry(LlmVerdict.Failed, 1);

        var flaky = new FakeLlmProvider("flaky");
        var attempt = 0;
        flaky.StreamScript = _ => ++attempt == 1
            ? [LlmChunk.Error(LlmVerdict.Failed, "cold start")]
            : [LlmChunk.Content("second attempt streamed"), LlmChunk.Final()];

        var chunks = new List<LlmChunk>();
        await foreach (var c in Router(options, null, flaky).StreamAsync([new("flaky")], Req))
            chunks.Add(c);

        Assert.Equal("second attempt streamed",
            string.Concat(chunks.Where(c => c.Kind == LlmChunkKind.Content).Select(c => c.Text)));
        Assert.Equal(2, flaky.StreamCalls); // reconnected the same candidate before the first token
    }
}
