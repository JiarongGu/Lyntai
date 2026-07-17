using Lyntai;
using Lyntai.Llm;
using Lyntai.Llm.Routing;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Core;

public class LlmRouterStreamTests
{
    private static LlmRequest Req => new() { Messages = [LlmMessage.User("hi")] };

    private static LlmRouter Router(params ILlmProvider[] providers) =>
        new(providers, new DeadHostTracker(), new LyntaiOptions());

    private static async Task<List<LlmChunk>> Collect(IAsyncEnumerable<LlmChunk> stream)
    {
        var chunks = new List<LlmChunk>();
        await foreach (var c in stream) chunks.Add(c);
        return chunks;
    }

    [Fact]
    public async Task Pre_content_failure_falls_over_to_next_candidate()
    {
        var p1 = new FakeLlmProvider("p1")
        {
            StreamScript = _ => [LlmChunk.Error(LlmVerdict.Failed, "cold start")],
        };
        var p2 = new FakeLlmProvider("p2")
        {
            StreamScript = _ => [LlmChunk.Content("hello "), LlmChunk.Content("world"), LlmChunk.Final()],
        };

        var chunks = await Collect(Router(p1, p2).StreamAsync([new("p1"), new("p2")], Req));

        Assert.Equal(["hello ", "world"], chunks.Where(c => c.Kind == LlmChunkKind.Content).Select(c => c.Text));
        Assert.Equal(LlmChunkKind.Final, chunks[^1].Kind);
        Assert.Equal(1, p2.StreamCalls);
    }

    [Fact]
    public async Task Mid_stream_error_after_a_token_passes_through_no_second_candidate()
    {
        var p1 = new FakeLlmProvider("p1")
        {
            StreamScript = _ => [LlmChunk.Content("partial"), LlmChunk.Error(LlmVerdict.Failed, "died mid-stream")],
        };
        var p2 = new FakeLlmProvider("p2");

        var chunks = await Collect(Router(p1, p2).StreamAsync([new("p1"), new("p2")], Req));

        Assert.Equal(2, chunks.Count);
        Assert.Equal("partial", chunks[0].Text);
        Assert.Equal(LlmChunkKind.Error, chunks[1].Kind);   // error passed through unchanged
        Assert.Equal(0, p2.StreamCalls);                    // never falls back after the first token
    }

    [Fact]
    public async Task Success_streams_straight_through_in_order()
    {
        var p1 = new FakeLlmProvider("p1")
        {
            StreamScript = _ => [LlmChunk.Content("a"), LlmChunk.Content("b"), LlmChunk.Content("c"),
                LlmChunk.Final(new LlmUsage(10, 3))],
        };

        var chunks = await Collect(Router(p1).StreamAsync([new("p1")], Req));

        Assert.Equal(["a", "b", "c"], chunks.Where(c => c.Kind == LlmChunkKind.Content).Select(c => c.Text));
        Assert.Equal(10, chunks[^1].Usage!.InputTokens);
    }

    [Fact]
    public async Task Pre_content_rate_limit_cools_the_host_and_falls_over()
    {
        // amended §6: RateLimited advances like Failed/Timeout (the host cools, the fleet serves)
        var p1 = new FakeLlmProvider("p1")
        {
            StreamScript = _ => [LlmChunk.Error(LlmVerdict.RateLimited, "429")],
        };
        var p2 = new FakeLlmProvider("p2")
        {
            StreamScript = _ => [LlmChunk.Content("fallback stream"), LlmChunk.Final()],
        };

        var chunks = await Collect(Router(p1, p2).StreamAsync([new("p1"), new("p2")], Req));

        Assert.Equal("fallback stream",
            string.Concat(chunks.Where(c => c.Kind == LlmChunkKind.Content).Select(c => c.Text)));
        Assert.Equal(1, p2.StreamCalls);
    }

    [Fact]
    public async Task Pre_content_refusal_surfaces_without_fallback()
    {
        var p1 = new FakeLlmProvider("p1")
        {
            StreamScript = _ => [LlmChunk.Error(LlmVerdict.Refused, "content policy")],
        };
        var p2 = new FakeLlmProvider("p2");

        var chunks = await Collect(Router(p1, p2).StreamAsync([new("p1"), new("p2")], Req));

        Assert.Single(chunks);
        Assert.Equal(LlmVerdict.Refused, chunks[0].Verdict);
        Assert.Equal(0, p2.StreamCalls); // a refused prompt must never be re-submitted elsewhere
    }

    [Fact]
    public async Task All_candidates_fail_pre_content_yields_last_error()
    {
        var p1 = new FakeLlmProvider("p1") { StreamScript = _ => [LlmChunk.Error(LlmVerdict.Failed, "one")] };
        var p2 = new FakeLlmProvider("p2") { StreamScript = _ => [LlmChunk.Error(LlmVerdict.Timeout, "two")] };

        var chunks = await Collect(Router(p1, p2).StreamAsync([new("p1"), new("p2")], Req));

        Assert.Single(chunks);
        Assert.Equal(LlmVerdict.Timeout, chunks[0].Verdict);
        Assert.Equal("two", chunks[0].Detail);
    }
}
