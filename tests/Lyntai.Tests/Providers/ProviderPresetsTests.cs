using Lyntai.Lifecycle;
using System.Net;
using Lyntai;
using Lyntai.Llm;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>The pre-configured provider presets set the right endpoint/id defaults and route through
/// the same OpenAI-compatible provider; a BYO-httpClient path lets each hit a scripted handler. Apps
/// wanting bespoke config keep AddHttpProvider or their own IModelProvider via AddProvider.</summary>
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
            .AddOpenAi("sk-test", model: "gpt-x", httpClient: _ => new HttpClient(handler))
            .UseDefaultCandidates("openai"));
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
            .AddOllama(model: "llama3", httpClient: _ => new HttpClient(handler))
            .UseDefaultCandidates("ollama"));
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ILlmClient>()
            .CompleteAsync(new LlmRequest { Messages = [LlmMessage.User("hi")] });

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Equal("http://localhost:11434/api/chat", handler.Requests[0].Uri!.ToString()); // Ollama dialect
        Assert.Null(handler.Requests[0].Auth); // keyless
    }

    [Fact]
    public async Task Llama_preset_defaults_to_llama_servers_own_port_and_no_key()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody);
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddLlama(model: "gemma-3-4b", httpClient: _ => new HttpClient(handler))
            .UseDefaultCandidates("llama"));
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ILlmClient>()
            .CompleteAsync(new LlmRequest { Messages = [LlmMessage.User("hi")] });

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        // llama-server speaks the OpenAI schema off its ROOT — never Ollama's native /api/chat, which is
        // the mistake a "local model server" preset invites
        Assert.Equal("http://localhost:8080/v1/chat/completions", handler.Requests[0].Uri!.ToString());
        Assert.Null(handler.Requests[0].Auth); // keyless
    }

    [Fact]
    public async Task Llama_preset_reaches_a_remote_server_on_the_base_url_it_is_given()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody);
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddLlama(baseUrl: "http://gpu-box:9001", httpClient: _ => new HttpClient(handler))
            .UseDefaultCandidates("llama"));
        using var sp = services.BuildServiceProvider();

        await sp.GetRequiredService<ILlmClient>().CompleteAsync(new LlmRequest { Messages = [LlmMessage.User("hi")] });

        Assert.Equal("http://gpu-box:9001/v1/chat/completions", handler.Requests[0].Uri!.ToString());
    }

    [Fact]
    public async Task OpenRouter_preset_targets_openrouter()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody);
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddOpenRouter("or-key", httpClient: _ => new HttpClient(handler))
            .UseDefaultCandidates("openrouter"));
        using var sp = services.BuildServiceProvider();

        await sp.GetRequiredService<ILlmClient>().CompleteAsync(new LlmRequest { Messages = [LlmMessage.User("hi")] });

        Assert.StartsWith("https://openrouter.ai/api/v1", handler.Requests[0].Uri!.ToString());
    }

    [Fact]
    public async Task Presets_compose_and_route_by_id_with_bring_your_own_provider()
    {
        // several presets + a fully custom IModelProvider, all behind one router — the BYO path stays open
        var custom = new FakeLlmProvider("custom");
        custom.Replies.Enqueue(new LlmReply("from a custom provider", LlmVerdict.Ok));
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody);

        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddOpenAi("k", httpClient: _ => new HttpClient(handler))
            .AddProvider(_ => custom)                 // BYO IModelProvider
            .UseDefaultCandidates("custom", "openai"));  // custom first
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ILlmClient>()
            .CompleteAsync(new LlmRequest { Messages = [LlmMessage.User("hi")] });

        Assert.Equal("from a custom provider", reply.Text); // the custom provider served; no HTTP call
        Assert.Empty(handler.Requests);
    }
}
