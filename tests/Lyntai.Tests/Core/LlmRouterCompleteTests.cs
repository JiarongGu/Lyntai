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
