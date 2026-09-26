using System.Net;
using Lyntai.Inference;
using Lyntai.Providers.Http;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>URL-shape detection decides which PROVIDER CLASS a registration composes (D160) — an Ollama
/// server ROOT gets the native provider, everything else the OpenAI-shaped one — plus Azure's URL/auth
/// conventions. The behaviour half is pinned through <c>AddHttpProvider</c>, because composition is where
/// the decision lives; there is no per-call dialect to test.</summary>
public class ProviderDetectTests
{
    [Theory]
    [InlineData("http://localhost:11434", true)]
    [InlineData("http://192.168.1.5:11434", true)]
    [InlineData("http://gpu-box:11434", true)]
    [InlineData("http://localhost:11434/v1", false)]   // Ollama's OpenAI-shaped surface
    [InlineData("http://localhost:11434/v1/", false)]
    [InlineData("http://localhost:8080", false)]       // llama-server's own port
    [InlineData("https://api.openai.com", false)]
    [InlineData("not a url at all", false)]
    [InlineData("", false)]
    public void An_ollama_ROOT_is_its_well_known_port_without_a_v1_suffix(string baseUrl, bool expected)
    {
        Assert.Equal(expected, ProviderDetect.IsOllamaRoot(baseUrl));
    }

    [Theory]
    [InlineData("https://myres.openai.azure.com", true)]
    [InlineData("https://api.openai.com", false)]           // OpenAI itself is not Azure
    [InlineData("https://my-own-gateway.example.com", false)]
    [InlineData("", false)]
    public void An_azure_host_is_detected_from_the_resource_domain(string baseUrl, bool expected)
    {
        Assert.Equal(expected, ProviderDetect.IsAzureHost(baseUrl));
    }

    [Theory]
    [InlineData("https://api.openai.com.evil.com")]   // substring spoof — must NOT match
    [InlineData("https://openai.azure.com.evil.net")]
    public void Host_match_is_exact_or_subdomain_never_substring(string spoofed)
    {
        Assert.False(ProviderDetect.IsAzureHost(spoofed));
    }

    [Fact]
    public void Subdomain_matching_helper_guards_the_edge()
    {
        Assert.True(ProviderDetect.IsHost("api.openai.com", "openai.com"));
        Assert.True(ProviderDetect.IsHost("openai.com", "openai.com"));
        Assert.False(ProviderDetect.IsHost("openai.com.evil.com", "openai.com"));
        Assert.False(ProviderDetect.IsHost("fakeopenai.com", "openai.com"));
    }

    // ---- the behaviour the detection drives: which provider AddHttpProvider composes ------------------

    [Fact]
    public async Task AddHttpProvider_with_an_ollama_root_composes_the_native_provider()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK,
            """{"message":{"content":"ok"},"done":true}""");
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddHttpProvider("local", o => { o.BaseUrl = "http://localhost:11434"; o.Model = "llama3"; },
                httpClient: _ => new HttpClient(handler, disposeHandler: false))
            .UseDefaultCandidates("local"));
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ITextClient>()
            .CompleteAsync(new TextRequest { Messages = [TextMessage.User("hi")] });

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.Equal("http://localhost:11434/api/chat", handler.Requests[0].Uri!.ToString());
    }

    [Fact]
    public async Task AddHttpProvider_with_an_ollama_v1_base_stays_on_the_openai_shaped_wire()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"ok"}}]}""");
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddHttpProvider("local", o => { o.BaseUrl = "http://localhost:11434/v1"; o.Model = "llama3"; },
                httpClient: _ => new HttpClient(handler, disposeHandler: false))
            .UseDefaultCandidates("local"));
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ITextClient>()
            .CompleteAsync(new TextRequest { Messages = [TextMessage.User("hi")] });

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.Equal("http://localhost:11434/v1/chat/completions", handler.Requests[0].Uri!.ToString());
    }

    [Fact]
    public async Task A_score_registration_never_detours_to_ollama_because_rerank_is_openai_shaped_only()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK,
            """{"results":[{"index":0,"relevance_score":0.9}]}""");
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddHttpProvider("rerank", o =>
        {
            o.BaseUrl = "http://localhost:11434";
            o.Produces = ProviderKinds.Score;
        }, httpClient: _ => new HttpClient(handler, disposeHandler: false)));
        using var sp = services.BuildServiceProvider();

        var provider = Assert.Single(sp.GetServices<IModelProvider>());
        var scores = await ((IScoreProvider)provider).CallAsync(new ScoreRequest("q", ["d"]));

        Assert.True(scores.IsOk);
        Assert.Equal("http://localhost:11434/v1/rerank", handler.Requests[0].Uri!.ToString());
    }
}
