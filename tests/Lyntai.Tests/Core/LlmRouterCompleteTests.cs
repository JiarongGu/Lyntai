using Lyntai;
using Lyntai.Llm;
using Lyntai.Llm.Routing;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Core;

public class LlmRouterCompleteTests
{
    private static LlmRequest Req => new() { Messages = [LlmMessage.User("hi")] };

    private static LlmRouter Router(DeadHostTracker? tracker, params ILlmProvider[] providers) =>
        new(providers, tracker ?? new DeadHostTracker(), new LyntaiOptions());

    [Fact]
    public async Task First_ok_is_returned_and_second_not_called()
    {
        var p1 = new FakeLlmProvider("p1");
        p1.Replies.Enqueue(new LlmReply("from p1", LlmVerdict.Ok));
        var p2 = new FakeLlmProvider("p2");

        var reply = await Router(null, p1, p2).CompleteAsync([new("p1"), new("p2")], Req);

        Assert.Equal("from p1", reply.Text);
        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Empty(p2.Calls);
    }

    [Fact]
    public async Task Failed_advances_to_second_which_serves()
    {
        var p1 = new FakeLlmProvider("p1");
        p1.Replies.Enqueue(new LlmReply("", LlmVerdict.Failed, Detail: "boom"));
        var p2 = new FakeLlmProvider("p2");
        p2.Replies.Enqueue(new LlmReply("from p2", LlmVerdict.Ok));

        var reply = await Router(null, p1, p2).CompleteAsync([new("p1"), new("p2")], Req);

        Assert.Equal("from p2", reply.Text);
        Assert.Single(p1.Calls);
        Assert.Single(p2.Calls);
    }

    [Fact] // L1: a THROWING provider is classified through LlmVerdictClassifier, not flattened to Failed
    public async Task A_thrown_429_is_classified_RateLimited_and_cools_the_host()
    {
        var tracker = new DeadHostTracker();
        var p1 = new FakeLlmProvider("p1")
        { CompleteThrow = new HttpRequestException("throttled", null, System.Net.HttpStatusCode.TooManyRequests) };
        var p2 = new FakeLlmProvider("p2");
        p2.Replies.Enqueue(new LlmReply("from p2", LlmVerdict.Ok));

        var reply = await Router(tracker, p1, p2).CompleteAsync([new("p1"), new("p2")], Req);

        Assert.Equal("from p2", reply.Text);
        // RateLimited → immediate cooldown; a hand-rolled Failed would only penalize (1/threshold)
        Assert.True(tracker.IsDead("p1"));
    }

    [Fact] // R4: a THROWN exception whose text matches refusal keywords is a transport fault — never terminal Refused
    public async Task A_thrown_error_with_refusal_keywords_still_falls_over()
    {
        // an error page from a proxy/CDN mentioning "content filter" — the MODEL never declined anything
        var p1 = new FakeLlmProvider("p1")
        { CompleteThrow = new HttpRequestException("<html>Blocked by corporate content filter</html>") };
        var p2 = new FakeLlmProvider("p2");
        p2.Replies.Enqueue(new LlmReply("from p2", LlmVerdict.Ok));

        var reply = await Router(null, p1, p2).CompleteAsync([new("p1"), new("p2")], Req);

        Assert.Equal("from p2", reply.Text); // clamped to Failed → advance; NOT Refused → terminal surface
        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
    }

    [Fact]
    public async Task All_failed_returns_the_last_error()
    {
        var p1 = new FakeLlmProvider("p1");
        p1.Replies.Enqueue(new LlmReply("", LlmVerdict.Failed, Detail: "first"));
        var p2 = new FakeLlmProvider("p2");
        p2.Replies.Enqueue(new LlmReply("", LlmVerdict.Timeout, Detail: "second"));

        var reply = await Router(null, p1, p2).CompleteAsync([new("p1"), new("p2")], Req);

        Assert.Equal(LlmVerdict.Timeout, reply.Verdict);
        Assert.Equal("second", reply.Detail);
    }

    [Fact]
    public async Task RateLimited_cools_the_host_immediately_and_advances()
    {
        // amended §6: a 429 is terminal for the host's window, transient for the fleet
        var tracker = new DeadHostTracker(threshold: 3, TimeSpan.FromMinutes(5), () => DateTimeOffset.UtcNow);
        var p1 = new FakeLlmProvider("p1");
        p1.Replies.Enqueue(new LlmReply("", LlmVerdict.RateLimited, Detail: "429"));
        var p2 = new FakeLlmProvider("p2");
        p2.Replies.Enqueue(new LlmReply("served by fallback", LlmVerdict.Ok));

        var router = Router(tracker, p1, p2);
        var reply = await router.CompleteAsync([new("p1"), new("p2")], Req);

        Assert.Equal("served by fallback", reply.Text);
        Assert.True(tracker.IsDead("p1")); // ONE 429 → immediate cooldown, no threshold counting

        // the next call must skip p1 entirely — never re-ask inside the rate-limit window
        p2.Replies.Enqueue(new LlmReply("again", LlmVerdict.Ok));
        await router.CompleteAsync([new("p1"), new("p2")], Req);
        Assert.Single(p1.Calls);
    }

    [Fact]
    public async Task Context_window_exceeded_advances_without_penalizing_the_host()
    {
        // too-big-for-model is not a host fault: the correct remedy is a larger-context candidate
        var tracker = new DeadHostTracker(threshold: 1, TimeSpan.FromMinutes(5), () => DateTimeOffset.UtcNow);
        var small = new FakeLlmProvider("small");
        small.Replies.Enqueue(new LlmReply("", LlmVerdict.ContextWindowExceeded, Detail: "context_length_exceeded"));
        var big = new FakeLlmProvider("big");
        big.Replies.Enqueue(new LlmReply("handled by the big model", LlmVerdict.Ok));

        var reply = await Router(tracker, small, big).CompleteAsync([new("small"), new("big")], Req);

        Assert.Equal("handled by the big model", reply.Text);
        Assert.False(tracker.IsDead("small")); // threshold is 1 — any recorded failure would kill it
    }

    [Fact]
    public async Task Auth_failure_cools_the_host_and_advances()
    {
        var tracker = new DeadHostTracker(threshold: 3, TimeSpan.FromMinutes(5), () => DateTimeOffset.UtcNow);
        var badKey = new FakeLlmProvider("bad-key");
        badKey.Replies.Enqueue(new LlmReply("", LlmVerdict.AuthFailed, Detail: "401"));
        var goodKey = new FakeLlmProvider("good-key");
        goodKey.Replies.Enqueue(new LlmReply("authorized", LlmVerdict.Ok));

        var reply = await Router(tracker, badKey, goodKey).CompleteAsync([new("bad-key"), new("good-key")], Req);

        Assert.Equal("authorized", reply.Text);
        Assert.True(tracker.IsDead("bad-key")); // credentials won't fix themselves this window
    }

    [Fact]
    public async Task An_unconfigured_candidate_is_skipped_blamelessly()
    {
        // the asymmetry this closes: a consumer who LISTS a backend they have not configured had it benched
        // on cooldown for a fact the router knew before calling. NotConfigured advances with no penalty and
        // no cooldown — the same thing the generation router already does (GenerationRoutingPolicy).
        var tracker = new DeadHostTracker(threshold: 1, TimeSpan.FromMinutes(5), () => DateTimeOffset.UtcNow);
        var unset = new FakeLlmProvider("unset");
        unset.Replies.Enqueue(new LlmReply("", LlmVerdict.NotConfigured, Detail: "no api key"));
        var configured = new FakeLlmProvider("configured");
        configured.Replies.Enqueue(new LlmReply("served", LlmVerdict.Ok));

        var reply = await Router(tracker, unset, configured).CompleteAsync([new("unset"), new("configured")], Req);

        Assert.Equal("served", reply.Text);
        Assert.False(tracker.IsDead("unset")); // threshold is 1 — ANY recorded failure or cooldown benches it
    }

    [Fact]
    public async Task All_candidates_rate_limited_surfaces_the_rate_limit()
    {
        var p1 = new FakeLlmProvider("p1");
        p1.Replies.Enqueue(new LlmReply("", LlmVerdict.RateLimited, Detail: "429 p1"));
        var p2 = new FakeLlmProvider("p2");
        p2.Replies.Enqueue(new LlmReply("", LlmVerdict.RateLimited, Detail: "429 p2"));

        var reply = await Router(null, p1, p2).CompleteAsync([new("p1"), new("p2")], Req);

        Assert.Equal(LlmVerdict.RateLimited, reply.Verdict);
        Assert.Equal("429 p2", reply.Detail); // the last attempt's story, not a generic error
    }

    [Fact]
    public async Task Refused_surfaces_without_fallback()
    {
        var p1 = new FakeLlmProvider("p1");
        p1.Replies.Enqueue(new LlmReply("", LlmVerdict.Refused, Detail: "policy"));
        var p2 = new FakeLlmProvider("p2");

        var reply = await Router(null, p1, p2).CompleteAsync([new("p1"), new("p2")], Req);

        Assert.Equal(LlmVerdict.Refused, reply.Verdict);
        Assert.Empty(p2.Calls);
    }

    [Fact]
    public async Task Dead_provider_is_skipped()
    {
        var tracker = new DeadHostTracker(threshold: 1, TimeSpan.FromMinutes(5), () => DateTimeOffset.UtcNow);
        tracker.RecordFailure("p1"); // threshold 1 → dead now

        var p1 = new FakeLlmProvider("p1");
        var p2 = new FakeLlmProvider("p2");
        p2.Replies.Enqueue(new LlmReply("from p2", LlmVerdict.Ok));

        var reply = await Router(tracker, p1, p2).CompleteAsync([new("p1"), new("p2")], Req);

        Assert.Equal("from p2", reply.Text);
        Assert.Empty(p1.Calls);
    }

    [Fact]
    public async Task Unavailable_provider_is_skipped()
    {
        var p1 = new FakeLlmProvider("p1") { IsAvailable = false };
        var p2 = new FakeLlmProvider("p2");
        p2.Replies.Enqueue(new LlmReply("from p2", LlmVerdict.Ok));

        var reply = await Router(null, p1, p2).CompleteAsync([new("p1"), new("p2")], Req);

        Assert.Equal("from p2", reply.Text);
        Assert.Empty(p1.Calls);
    }

    [Fact]
    public async Task Provider_exception_is_mapped_to_failed_and_advances()
    {
        var p1 = new ThrowingProvider("p1");
        var p2 = new FakeLlmProvider("p2");
        p2.Replies.Enqueue(new LlmReply("from p2", LlmVerdict.Ok));

        var reply = await Router(null, p1, p2).CompleteAsync([new("p1"), new("p2")], Req);

        Assert.Equal("from p2", reply.Text);
        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
    }

    [Fact]
    public async Task No_live_candidate_returns_failed_reply()
    {
        var reply = await Router(null).CompleteAsync([new("ghost")], Req);

        Assert.Equal(LlmVerdict.Failed, reply.Verdict);
        Assert.Contains("no live candidate", reply.Detail);
    }

    private sealed class ThrowingProvider(string id) : ILlmProvider
    {
        public string Id => id;
        public bool IsAvailable => true;
        public Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default) =>
            throw new InvalidOperationException("kaboom");
        public IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, CancellationToken ct = default) =>
            throw new InvalidOperationException("kaboom");
    }
}
