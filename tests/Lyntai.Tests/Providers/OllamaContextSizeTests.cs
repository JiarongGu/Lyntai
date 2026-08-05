using System.Net;
using System.Text.Json.Nodes;
using Lyntai;
using Lyntai.Llm;
using Lyntai.Providers.OpenAiCompatible;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>The context-window override on <see cref="OpenAiCompatibleOptions"/> is a NATIVE-Ollama wire
/// option (<c>options.num_ctx</c>) — it is built into the Ollama payload only, and the OpenAI-shaped
/// payload has no knob to put it in. That is why the member carries the backend in its NAME: under the old
/// generic name a consumer on the OpenAI flavor reasonably set it and it did nothing, silently.
/// Both halves are pinned here, because the name is only honest while the second one holds.</summary>
public class OllamaContextSizeTests
{
    // deliberately distinctive: no other numeric field in either payload can collide with it, so
    // "absent from the body" can be asserted on the raw JSON text rather than on a shape
    private const int Distinctive = 31337;

    [Fact]
    public async Task Ollama_flavor_sends_the_context_size_as_options_num_ctx()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK,
            """{"message":{"role":"assistant","content":"from ollama"},"done":true}""");
        var provider = Provider(handler, o =>
        {
            o.BaseUrl = "http://localhost:11434"; // native surface — /api/chat, no key
            o.ApiKey = null;
            o.OllamaContextSize = Distinctive;
        });

        var reply = await provider.CompleteAsync(Req);

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        var body = JsonNode.Parse(handler.Requests[0].Body)!;
        Assert.Equal(Distinctive, (int)body["options"]!["num_ctx"]!);
    }

    // Every flavor other than native Ollama builds the OpenAI payload, whose builder never receives the
    // option at all. The /v1 case is the one that actually bit: the SAME Ollama server on its
    // OpenAI-COMPATIBLE surface, where the setting looks like it must apply and does not.
    [Theory]
    [InlineData("https://api.openai.com")]           // plain OpenAI
    [InlineData("https://openrouter.ai/api/v1")]     // OpenRouter
    [InlineData("https://my-res.openai.azure.com")]  // Azure OpenAI
    [InlineData("http://localhost:11434/v1")]        // Ollama's OpenAI-compatible surface, NOT its native one
    public async Task Every_other_flavor_ignores_it_entirely(string baseUrl)
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody);
        var provider = Provider(handler, o => { o.BaseUrl = baseUrl; o.OllamaContextSize = Distinctive; });

        var reply = await provider.CompleteAsync(Req);

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        var body = handler.Requests[0].Body;
        Assert.Null(JsonNode.Parse(body)!["options"]);       // no Ollama options bag to hang a knob off…
        Assert.DoesNotContain("num_ctx", body);              // …no knob anywhere else either…
        Assert.DoesNotContain(Distinctive.ToString(), body); // …and the value never reaches the wire
    }

    private const string OkBody = """
        {"choices":[{"message":{"role":"assistant","content":"hello"},"finish_reason":"stop"}]}
        """;

    private static OpenAiCompatibleProvider Provider(StubHttpHandler handler, Action<OpenAiCompatibleOptions> configure)
    {
        var config = new OpenAiCompatibleOptions { BaseUrl = "https://api.openai.com", ApiKey = "test-key" };
        configure(config);
        return new OpenAiCompatibleProvider("openai-compat", config, () => new HttpClient(handler, disposeHandler: false),
            new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(30) });
    }

    private static LlmRequest Req => new() { Messages = [LlmMessage.User("hi")], Model = "gpt-x" };
}
