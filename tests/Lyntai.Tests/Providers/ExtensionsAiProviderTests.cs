using Lyntai;
using Lyntai.Llm;
using Lyntai.Providers.ExtensionsAi;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

public class ExtensionsAiProviderTests
{
    private static ExtensionsAiProvider Provider(FakeChatClient client) =>
        new("meai", client, new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(30) });

    private static LlmRequest Req => new()
    {
        Messages = [LlmMessage.System("sys"), LlmMessage.User("hi")],
        Model = "gpt-x",
        MaxTokens = 64,
        Temperature = 0.5,
    };

    [Fact]
    public async Task Complete_maps_messages_options_text_and_usage()
    {
        var client = new FakeChatClient
        {
            Response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "bridged!"))
            {
                Usage = new UsageDetails { InputTokenCount = 11, OutputTokenCount = 3 },
            },
        };

        var reply = await Provider(client).CompleteAsync(Req);

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Equal("bridged!", reply.Text);
        Assert.Equal(11, reply.Usage!.InputTokens);
        Assert.Equal(3, reply.Usage.OutputTokens);

        var (messages, options) = client.Calls.Single();
        Assert.Equal(2, messages.Count);
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Equal("hi", messages[1].Text);
        Assert.Equal("gpt-x", options!.ModelId);
        Assert.Equal(64, options.MaxOutputTokens);
        Assert.Equal(0.5f, options.Temperature);
    }

    [Fact]
    public async Task Json_schema_maps_to_response_format()
    {
        var client = new FakeChatClient();

        await Provider(client).CompleteAsync(Req with { JsonSchema = """{"type":"object"}""" });

        Assert.IsType<ChatResponseFormatJson>(client.Calls.Single().Options!.ResponseFormat);
    }

    [Fact]
    public async Task Exception_maps_to_failed_verdict()
    {
        var client = new FakeChatClient { ThrowOnCall = new InvalidOperationException("backend down") };

        var reply = await Provider(client).CompleteAsync(Req);

        Assert.Equal(LlmVerdict.Failed, reply.Verdict);
        Assert.Contains("backend down", reply.Detail);
    }

    [Fact]
    public async Task Rate_limit_exception_maps_to_rate_limited()
    {
        var client = new FakeChatClient { ThrowOnCall = new HttpRequestException("429 Too Many Requests") };

        var reply = await Provider(client).CompleteAsync(Req);

        Assert.Equal(LlmVerdict.RateLimited, reply.Verdict);
    }

    [Fact]
    public async Task Content_filter_finish_reason_maps_to_refused()
    {
        var client = new FakeChatClient
        {
            Response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "redacted"))
            {
                FinishReason = ChatFinishReason.ContentFilter,
            },
        };

        var reply = await Provider(client).CompleteAsync(Req);

        Assert.Equal(LlmVerdict.Refused, reply.Verdict);
    }

    [Fact]
    public async Task Streaming_yields_content_then_final_with_usage()
    {
        var client = new FakeChatClient();
        client.Updates.Add(new ChatResponseUpdate(ChatRole.Assistant, "str"));
        client.Updates.Add(new ChatResponseUpdate(ChatRole.Assistant, "eamed"));
        client.Updates.Add(new ChatResponseUpdate
        {
            Contents = [new UsageContent(new UsageDetails { InputTokenCount = 9, OutputTokenCount = 2 })],
        });

        var chunks = new List<LlmChunk>();
        await foreach (var c in Provider(client).StreamAsync(Req)) chunks.Add(c);

        Assert.Equal("streamed", string.Concat(chunks.Where(c => c.Kind == LlmChunkKind.Content).Select(c => c.Text)));
        Assert.Equal(LlmChunkKind.Final, chunks[^1].Kind);
        Assert.Equal(9, chunks[^1].Usage!.InputTokens);
    }

    [Fact]
    public async Task Empty_stream_yields_error_not_final_so_the_router_can_fall_over()
    {
        var client = new FakeChatClient(); // no updates scripted → zero-content stream

        var chunks = new List<LlmChunk>();
        await foreach (var c in Provider(client).StreamAsync(Req)) chunks.Add(c);

        Assert.Single(chunks);
        Assert.Equal(LlmChunkKind.Error, chunks[0].Kind);
        Assert.Equal(LlmVerdict.Failed, chunks[0].Verdict);
    }

    [Fact]
    public async Task Typed_429_from_the_chat_client_maps_to_rate_limited()
    {
        var client = new FakeChatClient
        {
            // "Too Many Requests" carries the typed status, not "429" text — the old substring
            // heuristic mapped this to Failed and re-hammered the rate-limited account
            ThrowOnCall = new HttpRequestException("Too Many Requests", null, System.Net.HttpStatusCode.TooManyRequests),
        };

        var reply = await Provider(client).CompleteAsync(Req);

        Assert.Equal(LlmVerdict.RateLimited, reply.Verdict);
    }

    [Fact]
    public async Task Streaming_exception_surfaces_as_error_chunk()
    {
        var client = new FakeChatClient { ThrowOnCall = new InvalidOperationException("no stream for you") };

        var chunks = new List<LlmChunk>();
        await foreach (var c in Provider(client).StreamAsync(Req)) chunks.Add(c);

        Assert.Single(chunks);
        Assert.Equal(LlmChunkKind.Error, chunks[0].Kind);
        Assert.Equal(LlmVerdict.Failed, chunks[0].Verdict);
    }

    [Fact] // 6.2 — a fake IChatClient serves through the router by id
    public async Task Bridged_client_serves_through_the_router_by_id()
    {
        var client = new FakeChatClient
        {
            Response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "via the bridge")),
        };

        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddExtensionsAiProvider("my-meai", client)
            .DefaultCandidates("my-meai"));
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ILlmRouter>()
            .CompleteAsync([new("my-meai")], new LlmRequest { Messages = [LlmMessage.User("hi")] });

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Equal("via the bridge", reply.Text);
    }
}
