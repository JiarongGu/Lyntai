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
    public async Task Base_url_already_ending_in_v1_is_not_doubled()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OkBody);

        var provider = Provider(handler, c => c.BaseUrl = "https://openrouter.ai/api/v1");
        await provider.CompleteAsync(Req);

        Assert.Equal(new Uri("https://openrouter.ai/api/v1/chat/completions"), handler.Requests[0].Uri);
    }
}
