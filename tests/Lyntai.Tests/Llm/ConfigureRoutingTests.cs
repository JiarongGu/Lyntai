using Lyntai.Inference;
using Lyntai;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Llm;

public class ConfigureRoutingTests
{
    [Fact]
    public void ConfigureRouting_reaches_the_router_through_di()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .ConfigureRouting(r =>
            {
                r.Retry(ProviderVerdict.Failed, 3);
                r.CooldownScope = CooldownScope.ProviderAndModel;
                r.ExemptSoleCandidate = false;
            }));
        using var sp = services.BuildServiceProvider();

        var options = sp.GetRequiredService<LyntaiOptions>();
        Assert.Equal(3, options.Routing.RetriesFor(ProviderVerdict.Failed));
        Assert.Equal(CooldownScope.ProviderAndModel, options.Routing.CooldownScope);
        Assert.False(options.Routing.ExemptSoleCandidate);
    }

    [Fact]
    public async Task ConfigureRouting_retry_takes_effect_end_to_end()
    {
        var flaky = new FakeLlmProvider("flaky");
        flaky.Replies.Enqueue(new TextResponse("", ProviderVerdict.Failed, Detail: "blip"));
        flaky.Replies.Enqueue(new TextResponse("recovered", ProviderVerdict.Ok));

        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => flaky)
            .UseDefaultCandidates("flaky")
            .ConfigureRouting(r => r.Retry(ProviderVerdict.Failed, 1)));
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ITextClient>()
            .CompleteAsync(new TextRequest { Messages = [TextMessage.User("hi")] });

        Assert.Equal("recovered", reply.Text);
        Assert.Equal(2, flaky.Calls.Count);
    }
}
