using Lyntai.Inference;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Lyntai;
using Lyntai.Providers.Ollama;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>Ollama-native is its own BACKEND, not a payload flavour of the HTTP provider
/// (<c>docs/DECISIONS.md</c> D160): <c>/api/chat</c> + <c>/api/embed</c>, NDJSON streaming, its own
/// options. The wire behaviour pinned here is the SAME behaviour the old dialect arm was measured
/// doing — these tests moved, they were not invented.</summary>
public class OllamaProviderTests
{
    private static TextRequest Req => new() { Messages = [TextMessage.User("hi")], Model = "llama3" };

    private static OllamaProvider Provider(StubHttpHandler handler, Action<OllamaOptions>? configure = null)
    {
        var config = new OllamaOptions();
        configure?.Invoke(config);
        return new OllamaProvider("ollama", config, () => new HttpClient(handler, disposeHandler: false),
            new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(30) });
    }

    [Fact]
    public async Task Complete_hits_api_chat_and_parses_the_native_shape()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, """
            {"message":{"role":"assistant","content":"from ollama"},"done":true,
             "prompt_eval_count":7,"eval_count":3}
            """);

        var reply = await Provider(handler).CompleteAsync(Req);

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.Equal("from ollama", reply.Text);
        Assert.Equal(7, reply.Usage!.InputTokens);
        Assert.Equal(3, reply.Usage.OutputTokens);
        Assert.Equal(new Uri("http://localhost:11434/api/chat"), handler.Requests[0].Uri);
        Assert.Null(handler.Requests[0].Auth); // keyless by default
    }

    [Fact]
    public async Task An_api_key_travels_as_a_bearer_token_for_a_proxied_server()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK,
            """{"message":{"content":"ok"},"done":true}""");

        await Provider(handler, o => { o.BaseUrl = "https://ollama.example.com"; o.ApiKey = "k1"; })
            .CompleteAsync(Req);

        Assert.Equal("Bearer k1", handler.Requests[0].Auth);
        Assert.Equal(new Uri("https://ollama.example.com/api/chat"), handler.Requests[0].Uri);
    }

    [Fact]
    public async Task Ndjson_stream_parses_and_final_carries_usage()
    {
        const string ndjson = """
            {"message":{"content":"a"},"done":false}
            {"message":{"content":"b"},"done":false}
            {"message":{"content":""},"done":true,"prompt_eval_count":5,"eval_count":2}
            """;
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, ndjson, "application/x-ndjson");

        var chunks = new List<TextChunk>();
        await foreach (var c in Provider(handler).StreamAsync(Req)) chunks.Add(c);

        Assert.Equal(["a", "b"], chunks.Where(c => c.Kind == TextChunkKind.Content).Select(c => c.Text));
        Assert.Equal(TextChunkKind.Final, chunks[^1].Kind);
        Assert.Equal(5, chunks[^1].Usage!.InputTokens);
    }

    [Fact]
    public async Task Tool_calls_normalize_object_arguments_and_synthesize_an_id()
    {
        // Ollama's shape: top-level message, arguments as an OBJECT, no id
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, """
            {"message":{"role":"assistant","content":"","tool_calls":[
              {"function":{"name":"get_weather","arguments":{"city":"Paris"}}}
            ]},"done":true}
            """);

        var reply = await Provider(handler).CompleteAsync(Req);

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        var call = Assert.Single(reply.ToolCalls!);
        Assert.Equal("get_weather", call.Name);
        Assert.False(string.IsNullOrEmpty(call.Id));                 // synthesized (Ollama gives none)
        Assert.Equal("""{"city":"Paris"}""", call.ArgumentsJson);    // object → JSON string
    }

    [Fact]
    public async Task ContextSize_reaches_the_wire_as_options_num_ctx()
    {
        // the knob lives on OllamaOptions and ALWAYS applies — the old cross-dialect
        // silently-ignored hazard is unrepresentable now, which is the point of the split
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK,
            """{"message":{"content":"ok"},"done":true}""");

        await Provider(handler, o => o.ContextSize = 31337).CompleteAsync(Req);

        var body = JsonNode.Parse(handler.Requests[0].Body)!;
        Assert.Equal(31337, (int)body["options"]!["num_ctx"]!);
    }

    [Fact]
    public async Task An_inline_image_reaches_the_wire_in_the_images_array()
    {
        var png = Encoding.UTF8.GetBytes("fake-png-bytes");
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK,
            """{"message":{"content":"a cat"},"done":true}""");

        var reply = await Provider(handler).CompleteAsync(new TextRequest
        {
            Messages = [TextMessage.UserWithImage("what is this?", png, "image/png")],
            Model = "llava",
        });

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.Contains("\"images\"", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.Contains(Convert.ToBase64String(png), handler.Requests[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_in_band_error_at_http_200_classifies_and_does_not_resend()
    {
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, """{"error":{"code":429,"message":"Rate limit exceeded"}}""");

        var reply = await Provider(handler).CompleteAsync(Req);

        Assert.Equal(ProviderVerdict.RateLimited, reply.Verdict);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Produces_vector_embeds_through_api_embed()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK,
            """{"embeddings":[[0.1,0.2],[0.3,0.4]]}""");
        var provider = Provider(handler, o => o.Produces = ProviderKinds.Vector);

        var reply = await provider.CallAsync(new VectorRequest(["one", "two"]));

        Assert.True(reply.IsOk);
        Assert.Equal(2, reply.Vectors.Count);
        Assert.Equal(new Uri("http://localhost:11434/api/embed"), handler.Requests[0].Uri);
        var body = JsonNode.Parse(handler.Requests[0].Body)!;
        Assert.Equal(2, body["input"]!.AsArray().Count); // batched {model, input[]}
    }

    [Fact]
    public void Capabilities_follow_Produces()
    {
        var text = Provider(new StubHttpHandler());
        Assert.Equal([ProviderKinds.Text], text.Capabilities.Produces);
        Assert.Contains(ProviderOperation.Stream, text.Capabilities.Operations);
        Assert.True(text.Capabilities.SupportsToolCalls);

        var vector = Provider(new StubHttpHandler(), o => o.Produces = ProviderKinds.Vector);
        Assert.Equal([ProviderKinds.Vector], vector.Capabilities.Produces);
        Assert.Equal([ProviderOperation.Complete], vector.Capabilities.Operations); // no partial embedding
        Assert.False(vector.Capabilities.SupportsToolCalls);
    }

    [Fact]
    public void Produces_score_is_refused_at_construction()
    {
        // Ollama serves no rerank surface, so a Score registration is a composition error heard while a
        // human is watching — not a /v1/rerank guess that 404s on the first call, which is what the old
        // dialect arm did
        var ex = Assert.Throws<NotSupportedException>(() => Provider(new StubHttpHandler(),
            o => o.Produces = ProviderKinds.Score));
        Assert.Contains("rerank", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
