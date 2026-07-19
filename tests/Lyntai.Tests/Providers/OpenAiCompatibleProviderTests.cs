using System.Net;
using Lyntai;
using Lyntai.Llm;
using Lyntai.Providers.OpenAiCompatible;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

public class OpenAiCompatibleProviderTests
{
    private const string OkBody = """
        {"choices":[{"message":{"role":"assistant","content":"hello from http"},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":10,"completion_tokens":4}}
        """;

    private static OpenAiCompatibleProvider Provider(StubHttpHandler handler, Action<OpenAiCompatibleOptions>? configure = null)
    {
        var config = new OpenAiCompatibleOptions { BaseUrl = "https://api.openai.com", ApiKey = "test-key" };
        configure?.Invoke(config);
        return new OpenAiCompatibleProvider("openai", config, () => new HttpClient(handler, disposeHandler: false),
            new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(30) });
    }

    private static LlmRequest Req => new() { Messages = [LlmMessage.User("hi")], Model = "gpt-x" };

    [Fact]
    public async Task Http_200_maps_to_ok_with_text_and_usage()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody);

        var reply = await Provider(handler).CompleteAsync(Req);

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Equal("hello from http", reply.Text);
        Assert.Equal(10, reply.Usage!.InputTokens);
        Assert.Equal(4, reply.Usage.OutputTokens);
        Assert.Equal(new Uri("https://api.openai.com/v1/chat/completions"), handler.Requests[0].Uri);
        Assert.Equal("Bearer test-key", handler.Requests[0].Auth);
    }

    [Fact]
    public async Task Http_429_maps_to_rate_limited()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.TooManyRequests, """{"error":"slow down"}""");

        var reply = await Provider(handler).CompleteAsync(Req);

        Assert.Equal(LlmVerdict.RateLimited, reply.Verdict);
        Assert.Contains("429", reply.Detail);
    }

    [Fact]
    public async Task Http_500_maps_to_failed()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.InternalServerError, "boom");

        var reply = await Provider(handler).CompleteAsync(Req);

        Assert.Equal(LlmVerdict.Failed, reply.Verdict);
    }

    [Fact]
    public void Provider_advertises_native_tool_calls()
    {
        Assert.True(Provider(new StubHttpHandler()).SupportsToolCalls);
    }

    [Fact]
    public async Task Openai_tool_calls_response_maps_to_ok_with_parsed_calls_and_empty_text()
    {
        // content is null + finish_reason tool_calls + arguments as a STRING (OpenAI shape)
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, """
            {"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[
              {"id":"call_1","type":"function","function":{"name":"get_weather","arguments":"{\"city\":\"Paris\"}"}}
            ]},"finish_reason":"tool_calls"}]}
            """);

        var reply = await Provider(handler).CompleteAsync(Req);

        Assert.Equal(LlmVerdict.Ok, reply.Verdict); // empty text + tool calls is NOT a failure
        Assert.Equal("", reply.Text);
        var call = Assert.Single(reply.ToolCalls!);
        Assert.Equal("call_1", call.Id);
        Assert.Equal("get_weather", call.Name);
        Assert.Equal("""{"city":"Paris"}""", call.ArgumentsJson);
    }

    [Fact]
    public async Task Ollama_tool_calls_response_normalizes_object_arguments_and_synthesizes_an_id()
    {
        // Ollama shape: top-level message, arguments as an OBJECT, no id
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, """
            {"message":{"role":"assistant","content":"","tool_calls":[
              {"function":{"name":"get_weather","arguments":{"city":"Paris"}}}
            ]},"done":true}
            """);

        var reply = await Provider(handler).CompleteAsync(Req);

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        var call = Assert.Single(reply.ToolCalls!);
        Assert.Equal("get_weather", call.Name);
        Assert.False(string.IsNullOrEmpty(call.Id));                 // synthesized (Ollama gives none)
        Assert.Equal("""{"city":"Paris"}""", call.ArgumentsJson);    // object → JSON string
    }

    [Fact]
    public async Task Content_filter_maps_to_refused()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.BadRequest,
            """{"error":{"code":"content_filter","message":"nope"}}""");

        var reply = await Provider(handler).CompleteAsync(Req);

        Assert.Equal(LlmVerdict.Refused, reply.Verdict);
    }

    [Fact]
    public async Task Malformed_body_retries_once_then_fails()
    {
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, "{malformed")
            .Enqueue(HttpStatusCode.OK, "{\"still\": \"wrong shape\"}");

        var reply = await Provider(handler).CompleteAsync(Req);

        Assert.Equal(LlmVerdict.Failed, reply.Verdict);
        Assert.Equal(2, handler.Requests.Count); // exactly one retry
        Assert.Contains("malformed", reply.Detail);
    }

    [Fact]
    public async Task Malformed_then_good_recovers_on_the_retry()
    {
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, "{malformed")
            .Enqueue(HttpStatusCode.OK, OkBody);

        var reply = await Provider(handler).CompleteAsync(Req);

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Equal("hello from http", reply.Text);
    }

    [Fact]
    public async Task Ollama_flavor_hits_api_chat_and_parses_its_shape()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, """
            {"message":{"role":"assistant","content":"from ollama"},"done":true,
             "prompt_eval_count":7,"eval_count":3}
            """);

        var provider = Provider(handler, c => { c.BaseUrl = "http://localhost:11434"; c.ApiKey = null; });
        var reply = await provider.CompleteAsync(Req);

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Equal("from ollama", reply.Text);
        Assert.Equal(7, reply.Usage!.InputTokens);
        Assert.Equal(new Uri("http://localhost:11434/api/chat"), handler.Requests[0].Uri);
        Assert.Null(handler.Requests[0].Auth);
    }

    [Fact]
    public async Task Sse_stream_parses_chunks_in_order_and_done_terminates()
    {
        const string sse = """
            data: {"choices":[{"delta":{"content":"hel"}}]}

            data: {"choices":[{"delta":{"content":"lo"}}]}

            data: {"choices":[{"delta":{},"finish_reason":"stop"}]}

            data: [DONE]

            """;
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, sse, "text/event-stream");

        var chunks = new List<LlmChunk>();
        await foreach (var c in Provider(handler).StreamAsync(Req)) chunks.Add(c);

        Assert.Equal(["hel", "lo"], chunks.Where(c => c.Kind == LlmChunkKind.Content).Select(c => c.Text));
        Assert.Equal(LlmChunkKind.Final, chunks[^1].Kind);
    }

    [Fact]
    public async Task Sse_tool_calls_finish_with_no_content_surfaces_unsupported()
    {
        // a streamed tool call with NO text: streaming can't carry it, but it's not a host failure
        const string sse = """
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"name":"f"}}]}}]}

            data: {"choices":[{"delta":{},"finish_reason":"tool_calls"}]}

            data: [DONE]

            """;
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, sse, "text/event-stream");

        var chunks = new List<LlmChunk>();
        await foreach (var c in Provider(handler).StreamAsync(Req)) chunks.Add(c);

        var only = Assert.Single(chunks);
        Assert.Equal(LlmChunkKind.Error, only.Kind);
        Assert.Equal(LlmVerdict.Unsupported, only.Verdict); // NOT Failed (healthy host); NOT Refused (not policy)
    }

    [Fact]
    public async Task Sse_content_then_tool_calls_finish_keeps_content_with_a_benign_final()
    {
        // content streamed, THEN finish_reason=tool_calls — must NOT emit a trailing Error after the content
        const string sse = """
            data: {"choices":[{"delta":{"content":"partial answer"}}]}

            data: {"choices":[{"delta":{},"finish_reason":"tool_calls"}]}

            data: [DONE]

            """;
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, sse, "text/event-stream");

        var chunks = new List<LlmChunk>();
        await foreach (var c in Provider(handler).StreamAsync(Req)) chunks.Add(c);

        Assert.Equal(["partial answer"], chunks.Where(c => c.Kind == LlmChunkKind.Content).Select(c => c.Text));
        Assert.Equal(LlmChunkKind.Final, chunks[^1].Kind);            // benign Final
        Assert.DoesNotContain(chunks, c => c.Kind == LlmChunkKind.Error); // no spurious trailing Error
    }

    [Fact]
    public async Task Ollama_ndjson_stream_parses_and_final_carries_usage()
    {
        const string ndjson = """
            {"message":{"content":"a"},"done":false}
            {"message":{"content":"b"},"done":false}
            {"message":{"content":""},"done":true,"prompt_eval_count":5,"eval_count":2}
            """;
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, ndjson, "application/x-ndjson");

        var provider = Provider(handler, c => { c.BaseUrl = "http://localhost:11434"; c.ApiKey = null; });
        var chunks = new List<LlmChunk>();
        await foreach (var c in provider.StreamAsync(Req)) chunks.Add(c);

        Assert.Equal(["a", "b"], chunks.Where(c => c.Kind == LlmChunkKind.Content).Select(c => c.Text));
        Assert.Equal(LlmChunkKind.Final, chunks[^1].Kind);
        Assert.Equal(5, chunks[^1].Usage!.InputTokens);
    }

    [Fact]
    public async Task Pre_content_500_surfaces_as_error_chunk_for_router_fallback()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.InternalServerError, "boom");

        var chunks = new List<LlmChunk>();
        await foreach (var c in Provider(handler).StreamAsync(Req)) chunks.Add(c);

        Assert.Single(chunks);
        Assert.Equal(LlmChunkKind.Error, chunks[0].Kind);
        Assert.Equal(LlmVerdict.Failed, chunks[0].Verdict);
    }

    [Fact]
    public async Task Empty_content_filter_200_maps_to_refused_without_retry()
    {
        // the common shape: HTTP 200, finish_reason=content_filter, EMPTY content — must be Refused
        // (no fallback, and above all no re-submission of the flagged prompt via the retry path)
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, """
            {"choices":[{"message":{"role":"assistant","content":""},"finish_reason":"content_filter"}]}
            """);

        var reply = await Provider(handler).CompleteAsync(Req);

        Assert.Equal(LlmVerdict.Refused, reply.Verdict);
        Assert.Single(handler.Requests); // a refused prompt is never re-sent
    }

    [Fact]
    public async Task Streamed_content_filter_ends_refused_not_final()
    {
        const string sse = """
            data: {"choices":[{"delta":{"content":"par"}}]}

            data: {"choices":[{"delta":{},"finish_reason":"content_filter"}]}

            data: [DONE]

            """;
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, sse, "text/event-stream");

        var chunks = new List<LlmChunk>();
        await foreach (var c in Provider(handler).StreamAsync(Req)) chunks.Add(c);

        Assert.Equal(LlmChunkKind.Error, chunks[^1].Kind);
        Assert.Equal(LlmVerdict.Refused, chunks[^1].Verdict); // same verdict the non-streaming path gives
    }

    [Fact]
    public async Task Zero_content_stream_yields_error_so_the_router_can_fall_over()
    {
        // proxy error page / warming model: 200 with nothing usable — the streaming twin of
        // CompleteAsync's empty→Failed, instead of a clean empty Final that blocks fallback
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, "data: [DONE]\n\n", "text/event-stream");

        var chunks = new List<LlmChunk>();
        await foreach (var c in Provider(handler).StreamAsync(Req)) chunks.Add(c);

        Assert.Single(chunks);
        Assert.Equal(LlmChunkKind.Error, chunks[0].Kind);
        Assert.Equal(LlmVerdict.Failed, chunks[0].Verdict);
    }

    [Fact]
    public async Task Base_url_already_ending_in_v1_is_not_doubled()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody);

        var provider = Provider(handler, c => c.BaseUrl = "https://openrouter.ai/api/v1");
        await provider.CompleteAsync(Req);

        Assert.Equal(new Uri("https://openrouter.ai/api/v1/chat/completions"), handler.Requests[0].Uri);
    }
}
