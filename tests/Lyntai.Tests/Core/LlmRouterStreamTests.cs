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

        var chunks = await Router(p1, p2).StreamAsync([new("p1"), new("p2")], Req).ToListAsync();

        Assert.Equal(["hello ", "world"], chunks.Where(c => c.Kind == LlmChunkKind.Content).Select(c => c.Text));
        Assert.Equal(LlmChunkKind.Final, chunks[^1].Kind);
        Assert.Equal(1, p2.StreamCalls);
    }

    [Fact] // T8: a PROVIDER's own OperationCanceledException (caller ct not cancelled) falls over, not aborts
    public async Task Provider_side_cancellation_pre_content_falls_over_to_next_candidate()
    {
        var p1 = new FakeLlmProvider("p1") { StreamThrow = new OperationCanceledException("provider gave up") };
        var p2 = new FakeLlmProvider("p2") { StreamScript = _ => [LlmChunk.Content("hi"), LlmChunk.Final()] };

        // no caller cancellation → the provider's OWN OCE must not abort the router; it falls over to p2
        var chunks = await Router(p1, p2).StreamAsync([new("p1"), new("p2")], Req).ToListAsync();

        Assert.Equal(["hi"], chunks.Where(c => c.Kind == LlmChunkKind.Content).Select(c => c.Text));
        Assert.Equal(1, p2.StreamCalls);
    }

    [Fact]
    public async Task Empty_first_content_chunk_does_not_commit_so_a_following_error_falls_over()
    {
        // the router is the trust boundary: an empty/role-only Content chunk must NOT disable fallback
        // (shipped providers guard this, but a third-party ILlmProvider may yield an empty first chunk)
        var p1 = new FakeLlmProvider("p1")
        {
            StreamScript = _ => [LlmChunk.Content(""), LlmChunk.Error(LlmVerdict.Failed, "empty then died")],
        };
        var p2 = new FakeLlmProvider("p2")
        {
            StreamScript = _ => [LlmChunk.Content("recovered"), LlmChunk.Final()],
        };

        var chunks = await Router(p1, p2).StreamAsync([new("p1"), new("p2")], Req).ToListAsync();

        Assert.Equal("recovered",
            string.Concat(chunks.Where(c => c.Kind == LlmChunkKind.Content && c.Text.Length > 0).Select(c => c.Text)));
        Assert.Equal(1, p2.StreamCalls); // the empty chunk didn't commit, so it fell over
    }

    [Fact] // L4: zero chunks = a contract-violating empty stream → Failed + fall over (not a silent end)
    public async Task Zero_chunk_stream_falls_over_to_the_next_candidate()
    {
        var p1 = new FakeLlmProvider("p1") { StreamScript = _ => [] };
        var p2 = new FakeLlmProvider("p2") { StreamScript = _ => [LlmChunk.Content("recovered"), LlmChunk.Final()] };

        var chunks = await Router(p1, p2).StreamAsync([new("p1"), new("p2")], Req).ToListAsync();

        Assert.Equal("recovered", string.Concat(chunks.Where(c => c.Kind == LlmChunkKind.Content).Select(c => c.Text)));
        Assert.Equal(1, p2.StreamCalls);
    }

    [Fact] // L4: with no fallback left, the empty stream still ends with a terminal Error chunk (never silence)
    public async Task Zero_chunk_stream_with_no_fallback_yields_a_terminal_error()
    {
        var p1 = new FakeLlmProvider("p1") { StreamScript = _ => [] };

        var chunks = await Router(p1).StreamAsync([new("p1")], Req).ToListAsync();

        var only = Assert.Single(chunks);
        Assert.Equal(LlmChunkKind.Error, only.Kind);
        Assert.Equal(LlmVerdict.Failed, only.Verdict);
    }

    [Fact] // L4: a Final with NO preceding content is the empty-reply trap at the trust boundary → falls over
    public async Task Pre_content_final_falls_over_instead_of_passing_an_empty_end_through()
    {
        var p1 = new FakeLlmProvider("p1") { StreamScript = _ => [LlmChunk.Final()] };
        var p2 = new FakeLlmProvider("p2") { StreamScript = _ => [LlmChunk.Content("recovered"), LlmChunk.Final()] };

        var chunks = await Router(p1, p2).StreamAsync([new("p1"), new("p2")], Req).ToListAsync();

        Assert.Equal("recovered", string.Concat(chunks.Where(c => c.Kind == LlmChunkKind.Content).Select(c => c.Text)));
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

        var chunks = await Router(p1, p2).StreamAsync([new("p1"), new("p2")], Req).ToListAsync();

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

        var chunks = await Router(p1).StreamAsync([new("p1")], Req).ToListAsync();

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

        var chunks = await Router(p1, p2).StreamAsync([new("p1"), new("p2")], Req).ToListAsync();

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

        var chunks = await Router(p1, p2).StreamAsync([new("p1"), new("p2")], Req).ToListAsync();

        Assert.Single(chunks);
        Assert.Equal(LlmVerdict.Refused, chunks[0].Verdict);
        Assert.Equal(0, p2.StreamCalls); // a refused prompt must never be re-submitted elsewhere
    }

    [Fact]
    public async Task All_candidates_fail_pre_content_yields_last_error()
    {
        var p1 = new FakeLlmProvider("p1") { StreamScript = _ => [LlmChunk.Error(LlmVerdict.Failed, "one")] };
        var p2 = new FakeLlmProvider("p2") { StreamScript = _ => [LlmChunk.Error(LlmVerdict.Timeout, "two")] };

        var chunks = await Router(p1, p2).StreamAsync([new("p1"), new("p2")], Req).ToListAsync();

        Assert.Single(chunks);
        Assert.Equal(LlmVerdict.Timeout, chunks[0].Verdict);
        Assert.Equal("two", chunks[0].Detail);
    }
}
