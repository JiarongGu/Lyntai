using Lyntai.Inference;
using System.Net;
using System.Text.Json.Nodes;
using Lyntai;
using Lyntai.Providers.Http;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>The pre-configured provider presets set the right endpoint/id defaults and route through
/// the same OpenAI-shaped provider; a BYO-httpClient path lets each hit a scripted handler. Each preset's
/// options overload seeds those defaults, then runs the caller's configure.</summary>
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
            .AddOpenAiProvider("sk-test", model: "gpt-x", httpClient: _ => new HttpClient(handler))
            .UseDefaultCandidates("openai"));
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ITextClient>()
            .CompleteAsync(new TextRequest { Messages = [TextMessage.User("hi")] });

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
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
            .AddOllamaProvider(model: "llama3", httpClient: _ => new HttpClient(handler))
            .UseDefaultCandidates("ollama"));
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ITextClient>()
            .CompleteAsync(new TextRequest { Messages = [TextMessage.User("hi")] });

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.Equal("http://localhost:11434/api/chat", handler.Requests[0].Uri!.ToString()); // Ollama dialect
        Assert.Null(handler.Requests[0].Auth); // keyless
    }

    [Fact]
    public async Task Llama_preset_defaults_to_llama_servers_own_port_and_no_key()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody);
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddLlamaProvider(model: "gemma-3-4b", httpClient: _ => new HttpClient(handler))
            .UseDefaultCandidates("llama"));
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ITextClient>()
            .CompleteAsync(new TextRequest { Messages = [TextMessage.User("hi")] });

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
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
            .AddLlamaProvider(baseUrl: "http://gpu-box:9001", httpClient: _ => new HttpClient(handler))
            .UseDefaultCandidates("llama"));
        using var sp = services.BuildServiceProvider();

        await sp.GetRequiredService<ITextClient>().CompleteAsync(new TextRequest { Messages = [TextMessage.User("hi")] });

        Assert.Equal("http://gpu-box:9001/v1/chat/completions", handler.Requests[0].Uri!.ToString());
    }

    [Fact]
    public async Task OpenRouter_preset_targets_openrouter()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody);
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddOpenRouterProvider("or-key", httpClient: _ => new HttpClient(handler))
            .UseDefaultCandidates("openrouter"));
        using var sp = services.BuildServiceProvider();

        await sp.GetRequiredService<ITextClient>().CompleteAsync(new TextRequest { Messages = [TextMessage.User("hi")] });

        Assert.StartsWith("https://openrouter.ai/api/v1", handler.Requests[0].Uri!.ToString());
    }

    // ---- the options overloads: the preset's SEED, then configure, then the OpenAI-shaped registration ----

    private const string QwenOff = """{"chat_template_kwargs":{"enable_thinking":false}}""";

    [Fact]
    public async Task Llama_options_overload_seeds_llama_servers_port_not_the_options_default()
    {
        // HttpModelOptions.BaseUrl defaults to api.openai.com: without the seed this would post there
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody);
        using var sp = Compose(b => b.AddLlamaProvider("llama", o => o.Model = "gemma-3-4b", Client(handler)), "llama");

        await Complete(sp);

        Assert.Equal("http://localhost:8080/v1/chat/completions", handler.Requests[0].Uri!.ToString());
        Assert.Null(handler.Requests[0].Auth);
        Assert.Equal("gemma-3-4b", (string)JsonNode.Parse(handler.Requests[0].Body)!["model"]!);
    }

    [Fact]
    public async Task Llama_options_overload_sends_SuppressReasoningFields_to_llama_server()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody);
        using var sp = Compose(b => b.AddLlamaProvider("llama", o => o.SuppressReasoningFields = QwenOff,
            Client(handler)), "llama");

        await Complete(sp, TextReasoning.Suppress);

        Assert.Equal("http://localhost:8080/v1/chat/completions", handler.Requests[0].Uri!.ToString());
        Assert.False((bool)JsonNode.Parse(handler.Requests[0].Body)!["chat_template_kwargs"]!["enable_thinking"]!);
    }

    [Fact]
    public async Task Llama_options_overload_composes_a_bounded_reranker()
    {
        var handler = new StubHttpHandler().Enqueue(request =>
        {
            var sent = JsonNode.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult())!["documents"]!.AsArray();
            var results = new JsonArray([.. sent.Select((_, i) =>
                (JsonNode)new JsonObject { ["index"] = i, ["relevance_score"] = 0.5 })]);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(new JsonObject { ["results"] = results }.ToJsonString()),
            };
        });
        using var sp = Compose(b => b.AddLlamaProvider("rerank", o =>
        {
            o.Produces = ProviderKinds.Score;
            o.MaxInputChars = 40;
        }, Client(handler)));
        var provider = sp.GetServices<IModelProvider>().OfType<HttpModelProvider>().Single();

        var reply = await provider.CallAsync(new ScoreRequest("q",
            [string.Join(' ', Enumerable.Range(0, 30).Select(i => $"w{i:00}"))]));

        Assert.Equal([ProviderKinds.Score], provider.Capabilities.Produces);
        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.Single(reply.Scores);
        Assert.Equal("http://localhost:8080/v1/rerank", handler.Requests[0].Uri!.ToString());
        var pieces = JsonNode.Parse(handler.Requests[0].Body)!["documents"]!.AsArray();
        Assert.True(pieces.Count > 1);
        Assert.All(pieces, p => Assert.True(p!.GetValue<string>().Length <= 40));
    }

    [Theory]
    [InlineData("openai", "https://api.openai.com/v1/chat/completions")]
    [InlineData("openrouter", "https://openrouter.ai/api/v1/chat/completions")]
    public async Task A_hosted_options_overload_seeds_its_endpoint(string preset, string expected)
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody);
        using var sp = Compose(b => Register(b, preset, o => o.ApiKey = "k", Client(handler)), preset);

        await Complete(sp);

        Assert.Equal(expected, handler.Requests[0].Uri!.ToString());
        Assert.Equal("Bearer k", handler.Requests[0].Auth);
    }

    [Fact]
    public async Task Azure_options_overload_seeds_Azure_conventions_so_any_host_takes_them()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody);
        using var sp = Compose(b => b.AddAzureOpenAiProvider("azure-openai", o =>
        {
            o.BaseUrl = "https://llm.contoso.example";   // a custom domain: nothing in it says Azure
            o.ApiKey = "az-key";
        }, Client(handler)), "azure-openai");

        await Complete(sp);

        Assert.Equal("https://llm.contoso.example/openai/v1/chat/completions", handler.Requests[0].Uri!.ToString());
        Assert.Equal("az-key", handler.Requests[0].ApiKeyHeader);
    }

    [Fact]
    public async Task Azure_options_overload_without_a_BaseUrl_never_posts_its_key_elsewhere()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody);
        using var sp = Compose(b => b.AddAzureOpenAiProvider("azure-openai", o => o.ApiKey = "az-key",
            Client(handler)), "azure-openai");

        var reply = await Complete(sp);

        Assert.False(sp.GetServices<IModelProvider>().Single().IsAvailable);
        Assert.NotEqual(ProviderVerdict.Ok, reply.Verdict);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("llama")]
    [InlineData("openrouter")]
    [InlineData("azure-openai")]
    public async Task Every_options_overload_lets_configure_override_the_seed_and_set_knobs(string preset)
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody);
        using var sp = Compose(b => Register(b, preset, o =>
        {
            o.BaseUrl = "https://proxy.example/v1";
            o.AzureConventions = false;
            o.ApiKey = "k";
            o.SuppressReasoningFields = QwenOff;
        }, Client(handler)), preset);

        await Complete(sp, TextReasoning.Suppress);

        Assert.Equal("https://proxy.example/v1/chat/completions", handler.Requests[0].Uri!.ToString());
        Assert.Null(handler.Requests[0].ApiKeyHeader);   // Azure's api-key header goes with its conventions
        Assert.NotNull(JsonNode.Parse(handler.Requests[0].Body)!["chat_template_kwargs"]);
    }

    [Fact]
    public async Task A_llama_options_registration_on_Ollamas_port_stays_OpenAI_shaped()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody);
        using var sp = Compose(b => b.AddLlamaProvider("llama", o => o.BaseUrl = "http://localhost:11434",
            Client(handler)), "llama");

        await Complete(sp);

        Assert.Equal("http://localhost:11434/v1/chat/completions", handler.Requests[0].Uri!.ToString());
    }

    private static LyntaiBuilder Register(LyntaiBuilder b, string preset, Action<HttpModelOptions> configure,
        Func<IServiceProvider, HttpClient> client) => preset switch
    {
        "openai" => b.AddOpenAiProvider(preset, configure, client),
        "llama" => b.AddLlamaProvider(preset, configure, client),
        "openrouter" => b.AddOpenRouterProvider(preset, configure, client),
        "azure-openai" => b.AddAzureOpenAiProvider(preset, configure, client),
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
    };

    private static Func<IServiceProvider, HttpClient> Client(StubHttpHandler handler) =>
        _ => new HttpClient(handler, disposeHandler: false);

    private static ServiceProvider Compose(Func<LyntaiBuilder, LyntaiBuilder> register, string? candidate = null)
    {
        var services = new ServiceCollection();
        services.AddLyntai(b =>
        {
            register(b);
            if (candidate is not null) b.UseDefaultCandidates(candidate);
        });
        return services.BuildServiceProvider();
    }

    private static Task<TextResponse> Complete(IServiceProvider sp, TextReasoning reasoning = TextReasoning.Default) =>
        sp.GetRequiredService<ITextClient>().CompleteAsync(
            new TextRequest { Messages = [TextMessage.User("hi")], Reasoning = reasoning });

    [Fact]
    public async Task Presets_compose_and_route_by_id_with_bring_your_own_provider()
    {
        // several presets + a fully custom IModelProvider, all behind one router — the BYO path stays open
        var custom = new FakeTextProvider("custom");
        custom.Replies.Enqueue(new TextResponse("from a custom provider", ProviderVerdict.Ok));
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody);

        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddOpenAiProvider("k", httpClient: _ => new HttpClient(handler))
            .AddProvider(_ => custom)                 // BYO IModelProvider
            .UseDefaultCandidates("custom", "openai"));  // custom first
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ITextClient>()
            .CompleteAsync(new TextRequest { Messages = [TextMessage.User("hi")] });

        Assert.Equal("from a custom provider", reply.Text); // the custom provider served; no HTTP call
        Assert.Empty(handler.Requests);
    }
}
