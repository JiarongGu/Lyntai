using Lyntai.Inference;
using Lyntai;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Inference;

/// <summary>The front-door contract: to a consumer, Lyntai behaves like ONE provider — no candidate
/// list at call sites, fallback happening invisibly behind <see cref="ITextClient"/>.</summary>
public class TextClientTests
{
    private static TextRequest Req => new() { Messages = [TextMessage.User("hi")] };

    private static ServiceProvider Build(params FakeTextProvider[] providers)
    {
        var services = new ServiceCollection();
        services.AddLyntai(b =>
        {
            foreach (var p in providers) b.AddProvider(_ => p);
            b.UseDefaultCandidates([.. providers.Select(p => new ProviderCandidate(p.Id))]);
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Complete_routes_over_default_candidates_without_passing_them()
    {
        var p = new FakeTextProvider("only");
        p.Replies.Enqueue(new TextResponse("front door", ProviderVerdict.Ok));
        using var sp = Build(p);

        var reply = await sp.GetRequiredService<ITextClient>().CompleteAsync(Req);

        Assert.Equal("front door", reply.Text);
    }

    [Fact]
    public async Task Fallback_happens_invisibly_behind_the_facade()
    {
        var p1 = new FakeTextProvider("p1");
        p1.Replies.Enqueue(new TextResponse("", ProviderVerdict.Failed, Detail: "down"));
        var p2 = new FakeTextProvider("p2");
        p2.Replies.Enqueue(new TextResponse("second served", ProviderVerdict.Ok));
        using var sp = Build(p1, p2);

        var reply = await sp.GetRequiredService<ITextClient>().CompleteAsync(Req);

        Assert.Equal("second served", reply.Text); // caller never saw a candidate list
    }

    [Fact]
    public async Task Stream_goes_through_the_facade()
    {
        var p = new FakeTextProvider("only")
        {
            StreamScript = _ => [TextChunk.Content("a"), TextChunk.Content("b"), TextChunk.Final()],
        };
        using var sp = Build(p);

        var chunks = new List<TextChunk>();
        await foreach (var c in sp.GetRequiredService<ITextClient>().StreamAsync(Req)) chunks.Add(c);

        Assert.Equal("ab", string.Concat(chunks.Where(c => c.Kind == TextChunkKind.Content).Select(c => c.Text)));
        Assert.Equal(TextChunkKind.Final, chunks[^1].Kind);
    }

    [Fact]
    public async Task No_candidates_configured_reports_failed_like_a_downed_provider()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddProvider(_ => new FakeTextProvider("unrouted")));
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ITextClient>().CompleteAsync(Req);

        Assert.Equal(ProviderVerdict.Failed, reply.Verdict);
    }

    [Fact]
    public void SupportsToolCalls_is_false_when_the_default_provider_has_no_native_support()
    {
        using var sp = Build(new FakeTextProvider("plain")); // DIM default false
        Assert.False(sp.GetRequiredService<ITextClient>().SupportsToolCalls(Req));
    }

    [Fact]
    public void SupportsToolCalls_reflects_the_first_live_candidate()
    {
        var native = new FakeTextProvider("native") { SupportsToolCalls = true };
        var plain = new FakeTextProvider("plain");
        using var sp = Build(native, plain);
        Assert.True(sp.GetRequiredService<ITextClient>().SupportsToolCalls(Req));
    }

    [Fact]
    public void SupportsToolCalls_skips_a_dead_first_candidate_to_the_next_live_one()
    {
        // first candidate is unavailable → the query falls to the next LIVE candidate, which is plain
        var down = new FakeTextProvider("down") { IsAvailable = false, SupportsToolCalls = true };
        var plain = new FakeTextProvider("plain");
        using var sp = Build(down, plain);
        Assert.False(sp.GetRequiredService<ITextClient>().SupportsToolCalls(Req)); // the reachable one isn't tool-capable
    }
}
