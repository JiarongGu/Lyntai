using System.Net;
using Lyntai;
using Lyntai.Llm;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>The BYO HttpClient seam: an app supplies its own configured client (here one wired to a
/// scripted handler) and the OpenAI-compatible provider uses it instead of a Lyntai-created client.</summary>
public class ByoHttpClientTests
{
    private const string OkBody = """
        {"choices":[{"message":{"content":"served via my client"},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":5,"completion_tokens":2}}
        """;

    [Fact]
    public async Task App_supplied_httpclient_is_used()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody);
        var appClient = new HttpClient(handler); // the app's own client + handler pipeline

        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddOpenAiCompatibleProvider("openai",
                c => { c.BaseUrl = "https://api.openai.com"; c.ApiKey = "k"; },
                httpClient: _ => appClient) // BYO
            .DefaultCandidates("openai"));
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ILlmClient>()
            .CompleteAsync(new LlmRequest { Messages = [LlmMessage.User("hi")], Model = "gpt-x" });

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Equal("served via my client", reply.Text);
        Assert.Single(handler.Requests);                         // the app's handler saw the request
        Assert.Equal("Bearer k", handler.Requests[0].Auth);
    }

    [Fact]
    public async Task Default_path_still_creates_a_lyntai_client()
    {
        // no httpClient passed → Lyntai wires its own named client. Point at a closed local port so the
        // call fails fast with a connection error (proving the client existed and tried) — no network.
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddOpenAiCompatibleProvider("local", c => c.BaseUrl = "http://127.0.0.1:1")
            .DefaultCandidates("local")
            .Configure(o => o.ProviderTimeout = TimeSpan.FromSeconds(5)));
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ILlmClient>()
            .CompleteAsync(new LlmRequest { Messages = [LlmMessage.User("hi")] });
        Assert.Equal(LlmVerdict.Failed, reply.Verdict); // connection refused — but DI resolved a real client
    }
}
