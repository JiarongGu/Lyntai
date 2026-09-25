using Lyntai.Inference;
using Lyntai;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Inference;

public class RefusalScreeningTests
{
    private static TextRequest Req(string? refusalPattern = null) => new()
    {
        Messages = [TextMessage.User("hi")],
        RefusalPattern = refusalPattern,
    };

    [Fact]
    public async Task Reply_matching_the_per_request_pattern_is_refused()
    {
        var inner = new FakeTextClient();
        inner.Replies.Enqueue(new TextResponse("Lo siento, no puedo ayudar con eso.", ProviderVerdict.Ok));
        var screened = new RefusalScreeningTextClient(inner);

        var reply = await screened.CompleteAsync(Req(refusalPattern: "no puedo ayudar"));

        Assert.Equal(ProviderVerdict.Refused, reply.Verdict);
        Assert.Contains("refusal pattern", reply.Detail);
    }

    [Fact]
    public async Task Reply_not_matching_stays_ok()
    {
        var inner = new FakeTextClient();
        inner.Replies.Enqueue(new TextResponse("Sure, here is the answer.", ProviderVerdict.Ok));
        var screened = new RefusalScreeningTextClient(inner);

        var reply = await screened.CompleteAsync(Req(refusalPattern: "no puedo ayudar"));
        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
    }

    [Fact]
    public async Task No_pattern_passes_through()
    {
        var inner = new FakeTextClient();
        inner.Replies.Enqueue(new TextResponse("no puedo ayudar", ProviderVerdict.Ok)); // would match, but no pattern set
        var screened = new RefusalScreeningTextClient(inner);

        var reply = await screened.CompleteAsync(Req(refusalPattern: null));
        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
    }

    [Fact]
    public async Task Malformed_pattern_is_ignored_fail_open()
    {
        var inner = new FakeTextClient();
        inner.Replies.Enqueue(new TextResponse("anything", ProviderVerdict.Ok));
        var screened = new RefusalScreeningTextClient(inner);

        var reply = await screened.CompleteAsync(Req(refusalPattern: "(unclosed[")); // invalid regex
        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);                                   // passes through, no throw
    }

    [Fact]
    public async Task A_non_ok_reply_is_left_untouched()
    {
        var inner = new FakeTextClient();
        // the text MATCHES, so only the verdict clause keeps it from being re-labelled a refusal
        inner.Replies.Enqueue(new TextResponse("I cannot help with that", ProviderVerdict.RateLimited, Detail: "429"));
        var screened = new RefusalScreeningTextClient(inner);

        var reply = await screened.CompleteAsync(Req(refusalPattern: "cannot help"));
        Assert.Equal(ProviderVerdict.RateLimited, reply.Verdict); // screening only downgrades Ok replies
    }

    [Fact]
    public async Task Wired_through_AddLyntai_the_front_door_screens_the_reply()
    {
        var provider = new FakeTextProvider("p");
        provider.Replies.Enqueue(new TextResponse("I cannot help with that request.", ProviderVerdict.Ok));

        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddProvider(_ => provider).UseDefaultCandidates("p"));
        using var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<ITextClient>();

        var reply = await client.CompleteAsync(Req(refusalPattern: "cannot help"));
        Assert.Equal(ProviderVerdict.Refused, reply.Verdict);
    }

    // --- typed IRefusalMatcher seam (R21b) -------------------------------------------------------

    private sealed class ContainsMatcher(string needle) : IRefusalMatcher
    {
        public bool IsRefusal(TextRequest request, string replyText) => replyText.Contains(needle, StringComparison.Ordinal);
    }

    private sealed class ThrowingMatcher : IRefusalMatcher
    {
        public bool IsRefusal(TextRequest request, string replyText) => throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task A_registered_matcher_downgrades_an_ok_reply_to_refused()
    {
        var inner = new FakeTextClient();
        inner.Replies.Enqueue(new TextResponse("well, NOPE, not doing that", ProviderVerdict.Ok));
        var screened = new RefusalScreeningTextClient(inner, [new ContainsMatcher("NOPE")]);

        var reply = await screened.CompleteAsync(Req());
        Assert.Equal(ProviderVerdict.Refused, reply.Verdict);
    }

    [Fact]
    public async Task A_matcher_that_does_not_match_leaves_the_reply_ok()
    {
        var inner = new FakeTextClient();
        inner.Replies.Enqueue(new TextResponse("sure thing", ProviderVerdict.Ok));
        var screened = new RefusalScreeningTextClient(inner, [new ContainsMatcher("NOPE")]);

        var reply = await screened.CompleteAsync(Req());
        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
    }

    [Fact]
    public async Task A_throwing_matcher_fails_open()
    {
        var inner = new FakeTextClient();
        inner.Replies.Enqueue(new TextResponse("anything", ProviderVerdict.Ok));
        var screened = new RefusalScreeningTextClient(inner, [new ThrowingMatcher()]);

        var reply = await screened.CompleteAsync(Req());
        Assert.Equal(ProviderVerdict.Ok, reply.Verdict); // matcher blew up → reply passes through unchanged
    }

    [Fact] // A matcher registered against a PRE-REGISTERED ITextClient would be silently ignored — guard it
    public void A_pre_registered_front_door_with_a_refusal_matcher_throws()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITextClient>(new FakeTextClient()); // BYO ITextClient before AddLyntai
        Assert.Throws<InvalidOperationException>(() => services.AddLyntai(b => b
            .AddProvider(_ => new FakeTextProvider("p"))
            .AddRefusalMatcher(new ContainsMatcher("NOPE")))); // screening wraps Lyntai's client → dropped → guarded
    }

    [Fact]
    public async Task Matchers_registered_via_AddRefusalMatcher_screen_at_the_front_door()
    {
        var provider = new FakeTextProvider("p");
        provider.Replies.Enqueue(new TextResponse("here is my NOPE answer", ProviderVerdict.Ok));

        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => provider).UseDefaultCandidates("p")
            .AddRefusalMatcher(new ContainsMatcher("NOPE")));
        using var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<ITextClient>();

        var reply = await client.CompleteAsync(Req());
        Assert.Equal(ProviderVerdict.Refused, reply.Verdict);
    }
}
