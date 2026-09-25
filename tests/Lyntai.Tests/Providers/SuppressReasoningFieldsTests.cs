using System.Net;
using System.Text.Json.Nodes;
using Lyntai.Inference;
using Lyntai.Providers.Http;
using Lyntai.Providers.Http.Payloads;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary><see cref="HttpModelOptions.SuppressReasoningFields"/>: the OpenAI-shaped schema has no field for
/// <see cref="TextReasoning.Suppress"/>, so the provider adds the members a deployment configured, and only
/// then (<c>docs/DECISIONS.md</c> <b>D179</b>).</summary>
public class SuppressReasoningFieldsTests
{
    private const string QwenOff = """{"chat_template_kwargs":{"enable_thinking":false}}""";

    // The body as it was before the option existed, for this request — pinned literally, so "unchanged" is
    // measured against the wire rather than against another call through the same code.
    private const string BufferedBody = """{"model":"gpt-x","messages":[{"role":"user","content":"hi"}],"stream":false}""";
    private const string StreamedBody =
        """{"model":"gpt-x","messages":[{"role":"user","content":"hi"}],"stream":true,"stream_options":{"include_usage":true}}""";

    private const string OkBody = """
        {"choices":[{"message":{"role":"assistant","content":"yes"},"finish_reason":"stop"}]}
        """;

    private const string OkStream = """
        data: {"choices":[{"delta":{"content":"yes"},"finish_reason":"stop"}]}

        data: [DONE]

        """;

    [Fact]
    public async Task Suppress_adds_the_configured_members_to_the_BUFFERED_body_after_the_wire_fields()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody);

        var reply = await Provider(handler, QwenOff).CompleteAsync(Req(TextReasoning.Suppress));

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.Equal(BufferedBody[..^1] + ""","chat_template_kwargs":{"enable_thinking":false}}""",
            handler.Requests[0].Body);
    }

    [Fact]
    public async Task Suppress_adds_the_configured_members_to_the_STREAMED_body_after_the_wire_fields()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkStream, "text/event-stream");

        var chunks = await Stream(Provider(handler, QwenOff), Req(TextReasoning.Suppress));

        Assert.Equal(TextChunkKind.Final, chunks[^1].Kind);
        Assert.Equal(StreamedBody[..^1] + ""","chat_template_kwargs":{"enable_thinking":false}}""",
            handler.Requests[0].Body);
    }

    [Fact]
    public async Task Every_top_level_member_is_added_as_given()
    {
        const string fields = """{"chat_template_kwargs":{"enable_thinking":false,"x":[1,null]},"reasoning_effort":"none"}""";
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody);

        await Provider(handler, fields).CompleteAsync(Req(TextReasoning.Suppress));

        var body = JsonNode.Parse(handler.Requests[0].Body)!.AsObject();
        Assert.Equal(JsonNode.Parse(fields)!["chat_template_kwargs"]!.ToJsonString(),
            body["chat_template_kwargs"]!.ToJsonString());
        Assert.Equal("none", (string)body["reasoning_effort"]!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Default_reasoning_sends_the_body_UNCHANGED_with_the_option_set(bool stream)
    {
        var body = await SentBody(QwenOff, TextReasoning.Default, stream);

        Assert.Equal(stream ? StreamedBody : BufferedBody, body);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(null, true)]
    [InlineData("", false)]
    [InlineData("  ", true)]
    public async Task Without_the_option_Suppress_sends_the_body_UNCHANGED(string? fields, bool stream)
    {
        var body = await SentBody(fields, TextReasoning.Suppress, stream);

        Assert.Equal(stream ? StreamedBody : BufferedBody, body);
    }

    [Fact]
    public async Task Consecutive_calls_each_carry_the_members()
    {
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, OkBody)
            .Enqueue(HttpStatusCode.OK, OkBody)
            .Enqueue(HttpStatusCode.OK, OkStream, "text/event-stream");
        var provider = Provider(handler, QwenOff);

        await provider.CompleteAsync(Req(TextReasoning.Suppress));
        await provider.CompleteAsync(Req(TextReasoning.Suppress));
        await Stream(provider, Req(TextReasoning.Suppress));

        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, r =>
            Assert.False((bool)JsonNode.Parse(r.Body)!["chat_template_kwargs"]!["enable_thinking"]!));
    }

    [Fact]
    public void No_node_is_shared_between_two_requests()
    {
        var wire = new OpenAiChatWire(new HttpModelOptions { SuppressReasoningFields = QwenOff });

        var first = wire.BuildPayload(Req(TextReasoning.Suppress), "m", stream: false);
        var second = wire.BuildPayload(Req(TextReasoning.Suppress), "m", stream: true);
        first["chat_template_kwargs"]!["enable_thinking"] = true;
        var third = wire.BuildPayload(Req(TextReasoning.Suppress), "m", stream: false);

        Assert.NotSame(first["chat_template_kwargs"], second["chat_template_kwargs"]);
        Assert.False((bool)second["chat_template_kwargs"]!["enable_thinking"]!);
        Assert.False((bool)third["chat_template_kwargs"]!["enable_thinking"]!);
    }

    [Theory]
    [InlineData("model")]
    [InlineData("messages")]
    [InlineData("stream")]
    [InlineData("stream_options")]
    [InlineData("max_tokens")]
    [InlineData("temperature")]
    [InlineData("tools")]
    [InlineData("response_format")]
    [InlineData("Model")]          // a server matching keys without case (Go's encoding/json) lets the later
    [InlineData("STREAM")]         // one win, so a case variant would override the request rather than add
    [InlineData("Messages")]
    public void A_member_the_wire_sets_itself_is_refused_at_construction_naming_it(string key)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Provider(new StubHttpHandler(), $$"""{"chat_template_kwargs":{},"{{key}}":1}"""));

        Assert.Equal(nameof(HttpModelOptions.SuppressReasoningFields), ex.ParamName);
        Assert.Contains($"\"{key}\"", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[]", "array")]
    [InlineData("\"off\"", "string")]
    [InlineData("42", "number")]
    [InlineData("null", "null")]
    [InlineData("{not json", "not valid JSON")]
    [InlineData("""{"a":1,"a":2}""", "not valid JSON")]
    public void A_value_that_is_not_one_JSON_object_is_refused_at_construction(string fields, string named)
    {
        var ex = Assert.Throws<ArgumentException>(() => Provider(new StubHttpHandler(), fields));

        Assert.Equal(nameof(HttpModelOptions.SuppressReasoningFields), ex.ParamName);
        Assert.Contains(named, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invalid_value_fails_at_REGISTRATION_not_at_first_resolve()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<ArgumentException>(() => services.AddLyntai(b => b.AddHttpProvider("llama", o =>
        {
            o.BaseUrl = "http://localhost:8080";
            o.SuppressReasoningFields = """{"model":"other"}""";
        })));

        Assert.Equal(nameof(HttpModelOptions.SuppressReasoningFields), ex.ParamName);
    }

    [Theory]
    [InlineData("vector")]
    [InlineData("score")]
    public void The_option_is_IGNORED_on_a_registration_that_does_not_produce_text(string produces)
    {
        var provider = Provider(new StubHttpHandler(), "[]", c => c.Produces = produces);

        Assert.Equal([produces], provider.Capabilities.Produces);
    }

    /// <summary>The reserved list is the wire's own member list: a member the payload gains without joining
    /// it would let the option overwrite what the request set.</summary>
    [Fact]
    public void Every_member_the_wire_can_set_is_reserved()
    {
        var everything = new TextRequest
        {
            Messages = [TextMessage.User("hi")],
            MaxTokens = 8,
            Temperature = 0.1,
            Tools = [new TextTool("t", "d", """{"type":"object"}""")],
            JsonSchema = """{"type":"object"}""",
        };

        var keys = OpenAiPayload.Build(everything, "m", stream: true).Select(p => p.Key).ToHashSet();

        Assert.Equal(keys.Order(StringComparer.Ordinal), OpenAiPayload.WireMembers.Order(StringComparer.Ordinal));
    }

    /// <summary>The backstop for drift between the two: fields that reach the merge naming a member the body
    /// already holds — in any case — fail the call rather than override the request.</summary>
    [Theory]
    [InlineData("model")]
    [InlineData("MESSAGES")]
    public void The_merge_never_overwrites_a_member_the_body_already_holds(string key)
    {
        var unreserved = new JsonObject { [key] = "configured" };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            OpenAiPayload.Build(Req(TextReasoning.Suppress), "m", stream: false, unreserved));

        Assert.Contains($"\"{key}\"", ex.Message, StringComparison.Ordinal);
    }

    private static TextRequest Req(TextReasoning reasoning) =>
        new() { Messages = [TextMessage.User("hi")], Model = "gpt-x", Reasoning = reasoning };

    private static async Task<string> SentBody(string? fields, TextReasoning reasoning, bool stream)
    {
        var handler = stream
            ? new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkStream, "text/event-stream")
            : new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody);
        var provider = Provider(handler, fields);
        if (stream) await Stream(provider, Req(reasoning));
        else await provider.CompleteAsync(Req(reasoning));
        return handler.Requests[0].Body;
    }

    private static async Task<List<TextChunk>> Stream(HttpModelProvider provider, TextRequest req)
    {
        var chunks = await provider.StreamAsync(req).ToListAsync();
        return chunks;
    }

    private static HttpModelProvider Provider(StubHttpHandler handler, string? fields,
        Action<HttpModelOptions>? configure = null)
    {
        var config = new HttpModelOptions { BaseUrl = "http://localhost:8080", SuppressReasoningFields = fields };
        configure?.Invoke(config);
        return new HttpModelProvider("llama", config, () => new HttpClient(handler, disposeHandler: false),
            new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(30) });
    }
}
