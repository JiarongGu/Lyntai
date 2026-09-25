using Lyntai.Inference;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Inference;

/// <summary>The response cache keys on the model a call would actually be served by, and a candidate that PINS a
/// model decides that (the router resolves <c>candidate.Model ?? request.Model</c>). So two named clients over
/// one backend whose candidates pin different models must never serve each other's replies, while clients whose
/// candidates pin nothing still share a hit.</summary>
public class ResponseCacheCandidateTests
{
    private static readonly TextRequest Ask = new() { Messages = [TextMessage.User("summarize this")] };

    /// <summary>One backend echoing the model it was asked for, a cache, and two named clients.</summary>
    private static ServiceProvider Build(ProviderCandidate fast, ProviderCandidate best, List<string?> served)
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddBridgeProvider("claude", (req, _) =>
            {
                served.Add(req.Model);
                return Task.FromResult(new TextResponse($"answered by {req.Model ?? "(default)"}", ProviderVerdict.Ok));
            })
            .UseDefaultCandidates("claude")
            .AddResponseCache()
            .AddTextClient("fast", c => c.UseProviders("claude").UseCandidates(fast))
            .AddTextClient("best", c => c.UseProviders("claude").UseCandidates(best)));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Clients_pinning_different_models_do_not_cross_serve()
    {
        var served = new List<string?>();
        using var sp = Build(new("claude", "haiku"), new("claude", "opus"), served);
        var factory = sp.GetRequiredService<ITextClientFactory>();

        var fast = await factory.Get("fast").CompleteAsync(Ask);
        var best = await factory.Get("best").CompleteAsync(Ask);

        Assert.Equal("answered by haiku", fast.Text);
        Assert.Equal("answered by opus", best.Text);
        Assert.Equal(["haiku", "opus"], served);
    }

    [Fact]
    public async Task Clients_whose_candidates_pin_nothing_still_share_a_hit()
    {
        var served = new List<string?>();
        using var sp = Build(new("claude"), new("claude"), served);
        var factory = sp.GetRequiredService<ITextClientFactory>();

        await factory.Get("fast").CompleteAsync(Ask);
        var second = await factory.Get("best").CompleteAsync(Ask);

        Assert.Equal("answered by (default)", second.Text);
        Assert.Single(served); // the second client read the first one's entry
    }
}
