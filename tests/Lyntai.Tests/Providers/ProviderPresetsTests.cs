using System.Net;
using Lyntai;
using Lyntai.Llm;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>The pre-configured provider presets set the right endpoint/id defaults and route through
/// the same OpenAI-compatible provider; a BYO-httpClient path lets each hit a scripted handler. Apps
/// wanting bespoke config keep AddOpenAiCompatibleProvider or their own ILlmProvider via AddProvider.</summary>
public class ProviderPresetsTests
{
    private const string OkBody = """
        {"choices":[{"message":{"content":"ok"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}
        """;

    [Fact]
    public async Task OpenAi_preset_targets_openai_and_sends_the_key()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody);
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddOpenAiProvider("sk-test", defaultModel: "gpt-x", httpClient: _ => new HttpClient(handler))
            .DefaultCandidates("openai"));
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ILlmClient>()
            .CompleteAsync(new LlmRequest { Messages = [LlmMessage.User("hi")] });

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.StartsWith("https://api.openai.com", handler.Requests[0].Uri!.ToString());
        Assert.Equal("Bearer sk-test", handler.Requests[0].Auth);
    }

    [Fact]
    public async Task Ollama_preset_defaults_to_localhost_and_no_key()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK,
            """{"message":{"content":"ok"},"done":true}""");
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddOllamaProvider(defaultModel: "llama3", httpClient: _ => new HttpClient(handler))
            .DefaultCandidates("ollama"));
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ILlmClient>()
            .CompleteAsync(new LlmRequest { Messages = [LlmMessage.User("hi")] });

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Equal("http://localhost:11434/api/chat", handler.Requests[0].Uri!.ToString()); // Ollama flavor
        Assert.Null(handler.Requests[0].Auth); // keyless
    }

    [Fact]
    public async Task OpenRouter_preset_targets_openrouter()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody);
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddOpenRouterProvider("or-key", httpClient: _ => new HttpClient(handler))
            .DefaultCandidates("openrouter"));
        using var sp = services.BuildServiceProvider();

        await sp.GetRequiredService<ILlmClient>().CompleteAsync(new LlmRequest { Messages = [LlmMessage.User("hi")] });

        Assert.StartsWith("https://openrouter.ai/api/v1", handler.Requests[0].Uri!.ToString());
    }

    [Fact]
    public async Task Presets_compose_and_route_by_id_with_bring_your_own_provider()
    {
        // several presets + a fully custom ILlmProvider, all behind one router — the BYO path stays open
        var custom = new FakeLlmProvider("custom");
        custom.Replies.Enqueue(new LlmReply("from a custom provider", LlmVerdict.Ok));
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody);

        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddOpenAiProvider("k", httpClient: _ => new HttpClient(handler))
            .AddProvider(_ => custom)                 // BYO ILlmProvider
            .DefaultCandidates("custom", "openai"));  // custom first
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ILlmClient>()
            .CompleteAsync(new LlmRequest { Messages = [LlmMessage.User("hi")] });

        Assert.Equal("from a custom provider", reply.Text); // the custom provider served; no HTTP call
        Assert.Empty(handler.Requests);
    }
}
