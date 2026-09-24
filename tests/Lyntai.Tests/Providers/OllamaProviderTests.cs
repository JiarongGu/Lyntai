using Lyntai.Inference;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Lyntai;
using Lyntai.Providers.Ollama;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

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

    // ---- MaxInputChars + Segmentation: the same bound the HTTP provider takes (D177) --------------------

    // "w00 w01 … w29", 119 characters
    private static readonly string Words30 = string.Join(' ', Enumerable.Range(0, 30).Select(i => $"w{i:00}"));

    private static List<string> SentInputs(string body) =>
        [.. JsonNode.Parse(body)!["input"]!.AsArray().Select(t => t!.GetValue<string>())];

    /// <summary>An <c>/api/embed</c> server answering <c>[1, 0]</c> for every input it is sent.</summary>
    internal static StubHttpHandler Embedder() => new StubHttpHandler().Enqueue(request =>
    {
        var sent = SentInputs(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
        var embeddings = new JsonArray([.. sent.Select(_ => (JsonNode)new JsonArray(1.0, 0.0))]);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new JsonObject { ["embeddings"] = embeddings }.ToJsonString()),
        };
    });

    [Fact]
    public async Task Without_MaxInputChars_the_input_goes_whole_and_the_servers_own_truncation_stands()
    {
        var handler = Embedder();

        await Provider(handler, o => o.Produces = ProviderKinds.Vector).CallAsync(new VectorRequest([Words30]));

        Assert.Equal([Words30], SentInputs(handler.Requests[0].Body));
        Assert.False(JsonNode.Parse(handler.Requests[0].Body)!.AsObject().ContainsKey("truncate"));
    }

    [Fact]
    public async Task MaxInputChars_SEGMENTS_an_over_long_input_and_turns_the_servers_truncation_OFF()
    {
        var handler = Embedder();

        var response = await Provider(handler, o => { o.Produces = ProviderKinds.Vector; o.MaxInputChars = 40; })
            .CallAsync(new VectorRequest([Words30, "short"]));

        Assert.Equal(2, response.Vectors.Count);                // one per INPUT
        var sent = SentInputs(handler.Requests[0].Body);
        Assert.True(sent.Count >= 4, $"the long input should travel as pieces; sent {sent.Count}");
        Assert.All(sent, t => Assert.True(t.Length <= 40, $"a {t.Length}-character input was sent"));
        // a bound set here governs: a piece that still overflows must be reported, not cut behind it
        Assert.False(JsonNode.Parse(handler.Requests[0].Body)!["truncate"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Overflow_TRUNCATE_cuts_each_input_on_the_client()
    {
        var handler = Embedder();

        await Provider(handler, o =>
        {
            o.Produces = ProviderKinds.Vector;
            o.MaxInputChars = 40;
            o.Segmentation = new InputSegmentation { Overflow = InputOverflow.Truncate };
        }).CallAsync(new VectorRequest([Words30]));

        var sent = Assert.Single(SentInputs(handler.Requests[0].Body));
        Assert.Equal(Words30[..39], sent);
    }

    [Fact]
    public async Task Under_TRUNCATE_the_servers_own_cut_stands_rather_than_failing_the_call()
    {
        // the deployment already accepted loss: refusing an input that still overflows would bring back the
        // whole-call rejection segmenting exists to remove
        var handler = Embedder();

        await Provider(handler, o =>
        {
            o.Produces = ProviderKinds.Vector;
            o.MaxInputChars = 40;
            o.Segmentation = new InputSegmentation { Overflow = InputOverflow.Truncate };
        }).CallAsync(new VectorRequest([Words30]));

        Assert.False(JsonNode.Parse(handler.Requests[0].Body)!.AsObject().ContainsKey("truncate"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_MaxInputChars_that_is_not_positive_is_refused_at_construction(int max)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Provider(new StubHttpHandler(), o =>
        {
            o.Produces = ProviderKinds.Vector;
            o.MaxInputChars = max;
        }));

        Assert.Contains(nameof(OllamaOptions.MaxInputChars), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_prefix_that_leaves_a_piece_no_room_for_text_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Provider(new StubHttpHandler(), o =>
        {
            o.Produces = ProviderKinds.Vector;
            o.MaxInputChars = 17;
            o.DocumentPrefix = "search_document: ";   // 17 characters
        }));
    }

    [Fact]
    public void MaxInputChars_is_IGNORED_on_a_text_registration()
    {
        Assert.Equal([ProviderKinds.Text], Provider(new StubHttpHandler(), o => o.MaxInputChars = 0).Capabilities.Produces);
    }

    [Fact]
    public void An_invalid_MaxInputChars_fails_at_REGISTRATION_not_at_first_resolve()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCollection()
            .AddLyntai(b => b.AddOllamaProvider("embed", o =>
            {
                o.Produces = ProviderKinds.Vector;
                o.MaxInputChars = 0;
            })));
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
