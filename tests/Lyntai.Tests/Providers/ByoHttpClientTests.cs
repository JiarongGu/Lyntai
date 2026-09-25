using Lyntai.Inference;
using System.Net;
using Lyntai;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>The BYO HttpClient seam: an app supplies its own configured client (here one wired to a
/// scripted handler) and the OpenAI-shaped provider uses it instead of a Lyntai-created client.</summary>
public class ByoHttpClientTests
{
    private const string OkBody = """
        {"choices":[{"message":{"content":"served via my client"},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":5,"completion_tokens":2}}
        """;

    [Fact]
    public async Task App_supplied_httpclient_is_used_and_survives_repeated_calls()
    {
        // two responses + two calls on ONE shared client — before the ownership fix, the 1st call
        // disposed the app's client and the 2nd threw ObjectDisposedException (the old single-call
        // test masked this)
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody).Enqueue(HttpStatusCode.OK, OkBody);
        using var appClient = new HttpClient(handler); // the app's own client + handler pipeline; app owns disposal

        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddHttpProvider("openai",
                c => { c.BaseUrl = "https://api.openai.com"; c.ApiKey = "k"; },
                httpClient: _ => appClient) // BYO
            .UseDefaultCandidates("openai"));
        using var sp = services.BuildServiceProvider();
        var llm = sp.GetRequiredService<ITextClient>();

        var first = await llm.CompleteAsync(new TextRequest { Messages = [TextMessage.User("one")], Model = "gpt-x" });
        var second = await llm.CompleteAsync(new TextRequest { Messages = [TextMessage.User("two")], Model = "gpt-x" });

        Assert.Equal(ProviderVerdict.Ok, first.Verdict);
        Assert.Equal(ProviderVerdict.Ok, second.Verdict);      // client was NOT disposed after the first call
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("Bearer k", handler.Requests[1].Auth);
    }

    [Fact]
    public async Task Default_path_still_creates_a_lyntai_client()
    {
        // no httpClient passed → Lyntai wires its own NAMED client from IHttpClientFactory. Rerouting that
        // name's primary handler to a script is what proves the provider used it: the request arrives there.
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody);
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddHttpProvider("local", c => c.BaseUrl = "http://127.0.0.1:1")
            .UseDefaultCandidates("local"));
        services.AddHttpClient(HttpProviderBuilderExtensions.HttpClientName("local"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ITextClient>()
            .CompleteAsync(new TextRequest { Messages = [TextMessage.User("hi")] });

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.Equal("127.0.0.1:1", Assert.Single(handler.Requests).Uri!.Authority);   // the configured base
    }
}
