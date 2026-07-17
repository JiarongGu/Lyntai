using Lyntai;
using Lyntai.Llm;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Core;

/// <summary>The front-door contract: to a consumer, Lyntai behaves like ONE provider — no candidate
/// list at call sites, fallback happening invisibly behind <see cref="ILlmClient"/>.</summary>
public class LlmClientTests
{
    private static LlmRequest Req => new() { Messages = [LlmMessage.User("hi")] };

    private static ServiceProvider Build(params FakeLlmProvider[] providers)
    {
        var services = new ServiceCollection();
        services.AddLyntai(b =>
        {
            foreach (var p in providers) b.AddProvider(_ => p);
            b.DefaultCandidates([.. providers.Select(p => new LlmCandidate(p.Id))]);
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Complete_routes_over_default_candidates_without_passing_them()
    {
        var p = new FakeLlmProvider("only");
        p.Replies.Enqueue(new LlmReply("front door", LlmVerdict.Ok));
        using var sp = Build(p);

        var reply = await sp.GetRequiredService<ILlmClient>().CompleteAsync(Req);

        Assert.Equal("front door", reply.Text);
    }

    [Fact]
    public async Task Fallback_happens_invisibly_behind_the_facade()
    {
        var p1 = new FakeLlmProvider("p1");
        p1.Replies.Enqueue(new LlmReply("", LlmVerdict.Failed, Detail: "down"));
        var p2 = new FakeLlmProvider("p2");
        p2.Replies.Enqueue(new LlmReply("second served", LlmVerdict.Ok));
        using var sp = Build(p1, p2);

        var reply = await sp.GetRequiredService<ILlmClient>().CompleteAsync(Req);

        Assert.Equal("second served", reply.Text); // caller never saw a candidate list
    }

    [Fact]
    public async Task Stream_goes_through_the_facade()
    {
        var p = new FakeLlmProvider("only")
        {
            StreamScript = _ => [LlmChunk.Content("a"), LlmChunk.Content("b"), LlmChunk.Final()],
        };
        using var sp = Build(p);

        var chunks = new List<LlmChunk>();
        await foreach (var c in sp.GetRequiredService<ILlmClient>().StreamAsync(Req)) chunks.Add(c);

        Assert.Equal("ab", string.Concat(chunks.Where(c => c.Kind == LlmChunkKind.Content).Select(c => c.Text)));
        Assert.Equal(LlmChunkKind.Final, chunks[^1].Kind);
    }

    [Fact]
    public async Task No_candidates_configured_reports_failed_like_a_downed_provider()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddProvider(_ => new FakeLlmProvider("unrouted")));
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ILlmClient>().CompleteAsync(Req);

        Assert.Equal(LlmVerdict.Failed, reply.Verdict);
    }
}
