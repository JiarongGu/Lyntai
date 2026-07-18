using Lyntai;
using Lyntai.Llm;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Llm;

public class RefusalScreeningTests
{
    private static LlmRequest Req(string? refusalPattern = null) => new()
    {
        Messages = [LlmMessage.User("hi")],
        RefusalPattern = refusalPattern,
    };

    [Fact]
    public async Task Reply_matching_the_per_request_pattern_is_refused()
    {
        var inner = new FakeLlmClient();
        inner.Replies.Enqueue(new LlmReply("Lo siento, no puedo ayudar con eso.", LlmVerdict.Ok));
        var screened = new RefusalScreeningLlmClient(inner);

        var reply = await screened.CompleteAsync(Req(refusalPattern: "no puedo ayudar"));

        Assert.Equal(LlmVerdict.Refused, reply.Verdict);
        Assert.Contains("refusal pattern", reply.Detail);
    }

    [Fact]
    public async Task Reply_not_matching_stays_ok()
    {
        var inner = new FakeLlmClient();
        inner.Replies.Enqueue(new LlmReply("Sure, here is the answer.", LlmVerdict.Ok));
        var screened = new RefusalScreeningLlmClient(inner);

        var reply = await screened.CompleteAsync(Req(refusalPattern: "no puedo ayudar"));
        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
    }

    [Fact]
    public async Task No_pattern_passes_through()
    {
        var inner = new FakeLlmClient();
        inner.Replies.Enqueue(new LlmReply("no puedo ayudar", LlmVerdict.Ok)); // would match, but no pattern set
        var screened = new RefusalScreeningLlmClient(inner);

        var reply = await screened.CompleteAsync(Req(refusalPattern: null));
        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
    }

    [Fact]
    public async Task Malformed_pattern_is_ignored_fail_open()
    {
        var inner = new FakeLlmClient();
        inner.Replies.Enqueue(new LlmReply("anything", LlmVerdict.Ok));
        var screened = new RefusalScreeningLlmClient(inner);

        var reply = await screened.CompleteAsync(Req(refusalPattern: "(unclosed[")); // invalid regex
        Assert.Equal(LlmVerdict.Ok, reply.Verdict);                                   // passes through, no throw
    }

    [Fact]
    public async Task A_non_ok_reply_is_left_untouched()
    {
        var inner = new FakeLlmClient();
        inner.Replies.Enqueue(new LlmReply("", LlmVerdict.RateLimited, Detail: "429"));
        var screened = new RefusalScreeningLlmClient(inner);

        var reply = await screened.CompleteAsync(Req(refusalPattern: "429"));
        Assert.Equal(LlmVerdict.RateLimited, reply.Verdict); // screening only downgrades Ok replies
    }

    [Fact]
    public async Task Wired_through_AddLyntai_the_front_door_screens_the_reply()
    {
        var provider = new FakeLlmProvider("p");
        provider.Replies.Enqueue(new LlmReply("I cannot help with that request.", LlmVerdict.Ok));

        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddProvider(_ => provider).DefaultCandidates("p"));
        using var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<ILlmClient>();

        var reply = await client.CompleteAsync(Req(refusalPattern: "cannot help"));
        Assert.Equal(LlmVerdict.Refused, reply.Verdict);
    }
}
