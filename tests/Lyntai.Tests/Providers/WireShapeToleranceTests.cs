using System.Net;
using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Providers.Http;
using Lyntai.Providers.Ollama;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>Valid JSON of the WRONG SHAPE never throws out of a wire reader. <c>TryGetProperty</c> throws
/// <see cref="InvalidOperationException"/> on an element that is not an object, and <c>GetInt32</c>
/// throws <see cref="FormatException"/> on <c>1.5</c>, while every reader here catches only
/// <c>JsonException</c> — so a line such as <c>{"message":"x"}</c> escaped a "never throws" reader, out of
/// <c>CompleteAsync</c> or straight to an agent session's consumer.</summary>
public class WireShapeToleranceTests
{
    // ── claude stream-json: the provider reader ─────────────────────────────

    [Theory]
    [InlineData("""{"type":"assistant","message":"x"}""")]
    [InlineData("""{"type":"assistant","message":{"content":["x",1,null]}}""")]
    [InlineData("""{"type":"assistant","message":[1]}""")]
    public void The_provider_reader_reads_a_wrong_shape_as_other(string line)
    {
        Assert.Equal(StreamJsonEventKind.Other, StreamJsonParser.Parse(line).Kind);
    }

    [Fact]
    public void The_provider_reader_keeps_the_text_blocks_around_a_non_object_block()
    {
        var evt = StreamJsonParser.Parse(
            """{"type":"assistant","message":{"content":["x",{"type":"text","text":"hi"}]}}""");

        Assert.Equal(StreamJsonEventKind.AssistantText, evt.Kind);
        Assert.Equal("hi", evt.Text);
    }

    // ── claude stream-json: the agent reader ────────────────────────────────

    [Theory]
    [InlineData("""{"type":"assistant","message":{"content":["x",2]}}""")]
    [InlineData("""{"type":"user","message":{"content":[1,"y",null]}}""")]
    [InlineData("""{"type":"stream_event","event":{"type":"content_block_delta","delta":"x"}}""")]
    public void The_agent_reader_yields_nothing_for_a_wrong_shape(string line)
    {
        Assert.Empty(new StreamJsonAgentReader().Read(line).ToList());
    }

    [Fact]
    public void The_agent_reader_reads_a_tool_result_whose_content_holds_non_objects_as_empty()
    {
        var events = new StreamJsonAgentReader().Read(
            """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"c1","content":["z",3]}]}}""")
            .ToList();

        var result = Assert.IsType<ToolResult>(Assert.Single(events));
        Assert.Equal("c1", result.CallId);
        Assert.Equal("", result.Content);
    }

    // ── the OpenAI-shaped HTTP wire ─────────────────────────────────────────

    [Theory]
    [InlineData("""{"choices":[null]}""")]
    [InlineData("""{"choices":["x"]}""")]
    [InlineData("""{"choices":[{"message":"x"}]}""")]
    public async Task A_buffered_reply_of_the_wrong_shape_is_a_failed_verdict(string body)
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, body);

        var reply = await Http(handler).CompleteAsync(Req);

        Assert.Equal(ProviderVerdict.Failed, reply.Verdict);
    }

    [Fact]
    public async Task A_streamed_line_of_the_wrong_shape_is_skipped()
    {
        const string sse = """
            data: {"choices":[null]}

            data: {"choices":[{"delta":"x"}]}

            data: {"choices":[{"delta":{"content":"hi","tool_calls":"x"}}]}

            data: {"choices":[{"delta":{},"finish_reason":"stop"}]}

            data: [DONE]

            """;
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, sse, "text/event-stream");

        var chunks = await Http(handler).StreamAsync(Req).ToListAsync();

        Assert.Equal(["hi"], chunks.Where(c => c.Kind == TextChunkKind.Content).Select(c => c.Text));
        Assert.Equal(TextChunkKind.Final, chunks[^1].Kind);
    }

    // ── the Ollama-native wire ──────────────────────────────────────────────

    [Fact]
    public async Task A_streamed_ollama_line_whose_message_is_not_an_object_is_skipped()
    {
        const string ndjson = """
            {"message":"x","done":false}
            {"message":{"role":"assistant","content":"hi"},"done":false}
            {"message":{"role":"assistant","content":""},"done":true,"prompt_eval_count":1,"eval_count":1}

            """;
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, ndjson, "application/x-ndjson");
        var provider = new OllamaProvider("ollama", new OllamaOptions(),
            () => new HttpClient(handler, disposeHandler: false),
            new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(30) });

        var chunks = await provider.StreamAsync(Req).ToListAsync();

        Assert.Equal(["hi"], chunks.Where(c => c.Kind == TextChunkKind.Content).Select(c => c.Text));
        Assert.Equal(TextChunkKind.Final, chunks[^1].Kind);
    }

    // ── the rerank and vector transports ────────────────────────────────────

    [Theory]
    [InlineData("[]")]
    [InlineData("\"x\"")]
    [InlineData("""{"results":[{"index":1.5,"relevance_score":0.9}]}""")]
    [InlineData("""{"results":[{"index":1e3,"relevance_score":0.9}]}""")]
    public async Task A_rerank_body_of_the_wrong_shape_is_a_failed_verdict(string body)
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, body);
        var scorer = Http(handler, o => o.Produces = ProviderKinds.Score);

        var response = await scorer.CallAsync(new ScoreRequest("q", ["a"]));

        Assert.Equal(ProviderVerdict.Failed, response.Verdict);
    }

    [Theory]
    [InlineData("""{"data":[{"index":0.5,"embedding":[1,2]}]}""")]
    [InlineData("""{"data":[{"index":1e3,"embedding":[1,2]}]}""")]
    public async Task An_embedding_index_that_is_not_an_integer_is_a_failed_verdict(string body)
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, body);
        var embedder = Http(handler, o => o.Produces = ProviderKinds.Vector);

        var response = await embedder.CallAsync(new VectorRequest(["a"]));

        Assert.Equal(ProviderVerdict.Failed, response.Verdict);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static HttpModelProvider Http(StubHttpHandler handler, Action<HttpModelOptions>? configure = null)
    {
        var config = new HttpModelOptions { BaseUrl = "http://localhost:8080" };
        configure?.Invoke(config);
        return new HttpModelProvider("http", config, () => new HttpClient(handler, disposeHandler: false),
            new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(30) });
    }

    private static TextRequest Req => new() { Messages = [TextMessage.User("hi")], Model = "m" };
}
