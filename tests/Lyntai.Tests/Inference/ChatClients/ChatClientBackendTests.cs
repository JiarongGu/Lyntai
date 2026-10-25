using System.Net;
using System.Text.Json;
using Lyntai.Inference;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Inference.ChatClients;

/// <summary>An <c>IChatClient</c> reached through <c>AddBridgeProvider</c> and the mapping in
/// <see cref="ChatClientBackend"/>. Everything runs through the real router, because what is owed to a caller is
/// what ROUTING does with the mapping's verdicts, chunks and exceptions (<c>docs/extensions-ai.md</c>).</summary>
public class ChatClientBackendTests
{
    private static TextRequest Ask(string text = "hi") => new() { Messages = [TextMessage.User(text)] };

    private static ServiceProvider Build(Action<LyntaiBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddLyntai(configure);
        return services.BuildServiceProvider();
    }

    private static LyntaiBuilder Bridge(LyntaiBuilder b, string id, FakeChatClient client, ProviderCapabilities? caps = null) =>
        b.AddBridgeProvider(id,
            (req, ct) => ChatClientBackend.CompleteAsync(client, req, ct),
            (req, ct) => ChatClientBackend.StreamAsync(client, req, ct),
            caps ?? ChatClientBackend.Capabilities());

    private static LyntaiBuilder Stub(LyntaiBuilder b, string id) =>
        b.AddBridgeProvider(id, (_, _) => Task.FromResult(new TextResponse("backstop", ProviderVerdict.Ok)),
            (_, _) => Chunks(TextChunk.Content("backstop"), TextChunk.Final()));

    private static async IAsyncEnumerable<TextChunk> Chunks(params TextChunk[] chunks)
    {
        await Task.CompletedTask;
        foreach (var c in chunks) yield return c;
    }

    private static async Task<List<TextChunk>> Collect(IAsyncEnumerable<TextChunk> stream)
    {
        var all = new List<TextChunk>();
        await foreach (var c in stream) all.Add(c);
        return all;
    }

    private static string Text(IEnumerable<TextChunk> chunks) =>
        string.Concat(chunks.Where(c => c.Kind == TextChunkKind.Content).Select(c => c.Text));

    private static CancellationToken Safety() => new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

    // ---- completion: text, usage, and what reaches the client -----------------------------------------

    [Fact]
    public async Task Text_and_usage_come_back_and_the_request_reaches_the_client_as_options()
    {
        var client = new FakeChatClient
        {
            OnResponse = (_, _, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "hello"))
            {
                Usage = new UsageDetails { InputTokenCount = 12, OutputTokenCount = 3, CachedInputTokenCount = 5 },
            }),
        };
        using var sp = Build(b => Bridge(b, "api", client).UseDefaultCandidates("api"));

        var reply = await sp.GetRequiredService<ITextClient>().CompleteAsync(
            Ask() with { Model = "m-1", MaxTokens = 64, Temperature = 0.25 }, Safety());

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.Equal("hello", reply.Text);
        Assert.Equal(new TextUsage(12, 3, 5), reply.Usage);
        Assert.Equal("m-1", client.LastOptions!.ModelId);
        Assert.Equal(64, client.LastOptions.MaxOutputTokens);
        Assert.Equal(0.25f, client.LastOptions.Temperature);
    }

    [Fact]
    public async Task A_conversation_with_tool_calls_and_an_image_is_mapped_part_for_part()
    {
        var client = new FakeChatClient();
        using var sp = Build(b => Bridge(b, "api", client, ChatClientBackend.Capabilities(vision: true)).UseDefaultCandidates("api"));
        var call = new TextToolCall("c1", "weather", """{"city":"Paris"}""");

        await sp.GetRequiredService<ITextClient>().CompleteAsync(new TextRequest
        {
            Messages =
            [
                TextMessage.System("be brief"),
                TextMessage.UserWithImageUrl("what is this", "https://example.test/a.png", "image/png"),
                TextMessage.AssistantToolCalls([call], "checking"),
                TextMessage.ToolResult("c1", """{"tempC":21}"""),
            ],
        }, Safety());

        var sent = Assert.Single(client.Calls);
        Assert.Equal([ChatRole.System, ChatRole.User, ChatRole.Assistant, ChatRole.Tool], sent.Select(m => m.Role));
        Assert.Equal("https://example.test/a.png", Assert.Single(sent[1].Contents.OfType<UriContent>()).Uri.ToString());
        var made = Assert.Single(sent[2].Contents.OfType<FunctionCallContent>());
        Assert.Equal(("c1", "weather"), (made.CallId, made.Name));
        Assert.Equal("Paris", ((JsonElement)made.Arguments!["city"]!).GetString());
        var result = Assert.Single(sent[3].Contents.OfType<FunctionResultContent>());
        Assert.Equal("c1", result.CallId);
    }

    // ---- tools and schema ---------------------------------------------------------------------------

    [Fact]
    public async Task A_tool_is_declared_to_the_client_and_its_call_comes_back_whole()
    {
        var client = new FakeChatClient
        {
            OnResponse = (_, _, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("c9", "weather", new Dictionary<string, object?> { ["city"] = "Oslo" })]))),
        };
        using var sp = Build(b => Bridge(b, "api", client).UseDefaultCandidates("api"));

        var reply = await sp.GetRequiredService<ITextClient>().CompleteAsync(Ask() with
        {
            Tools = [new TextTool("weather", "Current weather", """{"type":"object","properties":{"city":{"type":"string"}}}""")],
            RequireNativeSupport = true,
        }, Safety());

        var declared = Assert.IsAssignableFrom<AIFunctionDeclaration>(Assert.Single(client.LastOptions!.Tools!));
        Assert.Equal(("weather", "Current weather"), (declared.Name, declared.Description));
        Assert.Equal("object", declared.JsonSchema.GetProperty("type").GetString());
        Assert.Equal(ProviderVerdict.Ok, reply.Verdict); // a tool turn is a reply, not an empty one
        var call = Assert.Single(reply.ToolCalls!);
        Assert.Equal(("c9", "weather"), (call.Id, call.Name));
        Assert.Equal("Oslo", JsonDocument.Parse(call.ArgumentsJson).RootElement.GetProperty("city").GetString());
    }

    [Fact]
    public async Task A_JSON_schema_is_sent_as_the_response_format_when_the_backend_declares_it()
    {
        var client = new FakeChatClient();
        using var sp = Build(b => Bridge(b, "api", client, ChatClientBackend.Capabilities(structuredOutput: true))
            .UseDefaultCandidates("api"));

        await sp.GetRequiredService<ITextClient>().CompleteAsync(
            Ask() with { JsonSchema = """{"type":"object"}""", RequireNativeSupport = true }, Safety());

        var format = Assert.IsType<ChatResponseFormatJson>(client.LastOptions!.ResponseFormat);
        Assert.Equal("object", format.Schema!.Value.GetProperty("type").GetString());
    }

    [Fact]
    public async Task A_schema_is_REFUSED_not_silently_dropped_while_the_backend_does_not_declare_it()
    {
        // the default declaration leaves structured output off: only the wrapped client knows if it honours one
        var client = new FakeChatClient();
        using var sp = Build(b => Bridge(b, "api", client).UseDefaultCandidates("api"));

        var reply = await sp.GetRequiredService<ITextClient>().CompleteAsync(
            Ask() with { JsonSchema = """{"type":"object"}""", RequireNativeSupport = true }, Safety());

        Assert.Equal(ProviderVerdict.Unsupported, reply.Verdict);
        Assert.Empty(client.Calls);
    }

    // ---- verdicts -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_content_filter_is_Refused_and_no_other_candidate_is_asked()
    {
        var client = new FakeChatClient
        {
            OnResponse = (_, _, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, ""))
            {
                FinishReason = ChatFinishReason.ContentFilter,
            }),
        };
        using var sp = Build(b => Stub(Bridge(b, "api", client), "backstop").UseDefaultCandidates("api", "backstop"));

        var reply = await sp.GetRequiredService<ITextClient>().CompleteAsync(Ask(), Safety());

        Assert.Equal(ProviderVerdict.Refused, reply.Verdict);
    }

    [Fact]
    public async Task An_empty_reply_is_Failed_and_the_router_falls_over()
    {
        var client = new FakeChatClient
        {
            OnResponse = (_, _, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, ""))),
        };
        using var sp = Build(b => Stub(Bridge(b, "api", client), "backstop").UseDefaultCandidates("api", "backstop"));

        var reply = await sp.GetRequiredService<ITextClient>().CompleteAsync(Ask(), Safety());

        Assert.Equal("backstop", reply.Text);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task A_client_that_throws_a_typed_status_is_classified_and_the_router_advances(HttpStatusCode status)
    {
        var client = new FakeChatClient
        {
            OnResponse = (_, _, _) => throw new HttpRequestException("boom", null, status),
        };
        using var sp = Build(b => Stub(Bridge(b, "api", client), "backstop").UseDefaultCandidates("api", "backstop"));

        var reply = await sp.GetRequiredService<ITextClient>().CompleteAsync(Ask(), Safety());

        Assert.Equal("backstop", reply.Text);
    }

    [Fact]
    public async Task A_context_window_error_is_classified_from_its_text()
    {
        var client = new FakeChatClient
        {
            OnResponse = (_, _, _) => throw new InvalidOperationException("the prompt is too long for this model"),
        };
        using var sp = Build(b => Bridge(b, "api", client).UseDefaultCandidates("api"));

        var reply = await sp.GetRequiredService<ITextClient>().CompleteAsync(Ask(), Safety());

        Assert.Equal(ProviderVerdict.ContextWindowExceeded, reply.Verdict);
    }

    // ---- cancellation, timeout, lifetime --------------------------------------------------------------

    [Fact]
    public async Task The_callers_cancel_reaches_the_client_and_propagates_rather_than_becoming_a_verdict()
    {
        var client = new FakeChatClient
        {
            OnResponse = async (_, _, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new ChatResponse();
            },
        };
        using var sp = Build(b => Bridge(b, "api", client).UseDefaultCandidates("api"));
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sp.GetRequiredService<ITextClient>().CompleteAsync(Ask(), caller.Token));

        Assert.True(client.LastToken.IsCancellationRequested);
    }

    [Fact]
    public async Task A_client_that_hangs_trips_the_provider_timeout_and_the_router_falls_over()
    {
        var client = new FakeChatClient
        {
            OnResponse = async (_, _, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new ChatResponse();
            },
        };
        using var sp = Build(b => Stub(Bridge(b, "api", client), "backstop")
            .Configure(o => o.ProviderTimeout = TimeSpan.FromMilliseconds(150))
            .UseDefaultCandidates("api", "backstop"));

        var reply = await sp.GetRequiredService<ITextClient>().CompleteAsync(Ask(), Safety());

        Assert.Equal("backstop", reply.Text);
        Assert.True(client.LastToken.IsCancellationRequested); // the client was told to stop, not merely abandoned
    }

    [Fact]
    public async Task Lyntai_never_disposes_a_client_it_was_handed_the_app_that_made_it_owns_it()
    {
        var client = new FakeChatClient();
        var sp = Build(b => Bridge(b, "api", client).UseDefaultCandidates("api"));
        await sp.GetRequiredService<ITextClient>().CompleteAsync(Ask(), Safety());

        await sp.DisposeAsync();

        Assert.Equal(0, client.Disposed);
        // and it is still usable by its owner afterwards
        Assert.Equal("ok", (await client.GetResponseAsync("x")).Text);
    }

    // ---- streaming ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_stream_yields_content_then_one_Final_carrying_the_reported_usage()
    {
        var client = new FakeChatClient
        {
            OnStream = (_, _, _) => FakeChatClient.Updates(
                new ChatResponseUpdate(ChatRole.Assistant, "he"),
                new ChatResponseUpdate(ChatRole.Assistant, ""),
                new ChatResponseUpdate(ChatRole.Assistant, "llo"),
                new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(new UsageDetails { InputTokenCount = 4, OutputTokenCount = 2 })])),
        };
        using var sp = Build(b => Bridge(b, "api", client).UseDefaultCandidates("api"));

        var chunks = await Collect(sp.GetRequiredService<ITextClient>().StreamAsync(Ask(), Safety()));

        Assert.Equal("hello", Text(chunks));
        Assert.Equal(["he", "llo"], chunks.Where(c => c.Kind == TextChunkKind.Content).Select(c => c.Text)); // the empty one commits nothing
        Assert.Equal(TextChunkKind.Final, chunks[^1].Kind);
        Assert.Equal(new TextUsage(4, 2), chunks[^1].Usage);
        Assert.Single(chunks, c => c.Kind == TextChunkKind.Final);
    }

    [Fact]
    public async Task A_streamed_tool_call_arrives_as_one_complete_chunk()
    {
        var client = new FakeChatClient
        {
            OnStream = (_, _, _) => FakeChatClient.Updates(new ChatResponseUpdate(ChatRole.Assistant,
                [new FunctionCallContent("c2", "weather", new Dictionary<string, object?> { ["city"] = "Rome" })])),
        };
        using var sp = Build(b => Bridge(b, "api", client).UseDefaultCandidates("api"));

        var chunks = await Collect(sp.GetRequiredService<ITextClient>().StreamAsync(Ask(), Safety()));

        var tool = Assert.Single(chunks, c => c.Kind == TextChunkKind.ToolCall).ToolCall!;
        Assert.Equal(("c2", "weather"), (tool.Id, tool.Name));
        Assert.Equal(TextChunkKind.Final, chunks[^1].Kind);
    }

    [Fact]
    public async Task A_stream_that_fails_BEFORE_content_falls_over_to_the_next_candidate()
    {
        var client = new FakeChatClient
        {
            OnStream = (_, _, _) => FakeChatClient.Breaks(new InvalidOperationException("connection reset")),
        };
        using var sp = Build(b => Stub(Bridge(b, "api", client), "backstop").UseDefaultCandidates("api", "backstop"));

        var chunks = await Collect(sp.GetRequiredService<ITextClient>().StreamAsync(Ask(), Safety()));

        Assert.Equal("backstop", Text(chunks));
    }

    [Fact]
    public async Task A_stream_that_fails_AFTER_content_surfaces_the_error_and_NEVER_falls_over()
    {
        // a second candidate would repeat what the caller has already read
        var client = new FakeChatClient
        {
            OnStream = (_, _, _) => FakeChatClient.Breaks(new InvalidOperationException("connection reset"),
                new ChatResponseUpdate(ChatRole.Assistant, "half")),
        };
        using var sp = Build(b => Stub(Bridge(b, "api", client), "backstop").UseDefaultCandidates("api", "backstop"));

        var chunks = await Collect(sp.GetRequiredService<ITextClient>().StreamAsync(Ask(), Safety()));

        Assert.Equal("half", Text(chunks));
        Assert.Equal(TextChunkKind.Error, chunks[^1].Kind);
    }

    [Fact]
    public async Task A_stream_with_no_output_is_Failed_so_the_router_falls_over()
    {
        var client = new FakeChatClient { OnStream = (_, _, _) => FakeChatClient.Updates() };
        using var sp = Build(b => Stub(Bridge(b, "api", client), "backstop").UseDefaultCandidates("api", "backstop"));

        var chunks = await Collect(sp.GetRequiredService<ITextClient>().StreamAsync(Ask(), Safety()));

        Assert.Equal("backstop", Text(chunks));
    }

    [Fact]
    public async Task A_client_stream_that_goes_silent_trips_the_inactivity_timeout_and_falls_over()
    {
        var client = new FakeChatClient { OnStream = (_, _, ct) => FakeChatClient.Silent(ct) };
        using var sp = Build(b => Stub(Bridge(b, "api", client), "backstop")
            .Configure(o => o.ProviderTimeout = TimeSpan.FromMilliseconds(150))
            .UseDefaultCandidates("api", "backstop"));

        var chunks = await Collect(sp.GetRequiredService<ITextClient>().StreamAsync(Ask(), Safety()));

        Assert.Equal("backstop", Text(chunks));
    }

    [Fact]
    public async Task A_consumer_that_stops_reading_cancels_the_clients_stream()
    {
        var cancelled = new TaskCompletionSource();
        var client = new FakeChatClient { OnStream = (_, _, ct) => Endless(ct, cancelled) };
        using var sp = Build(b => Bridge(b, "api", client).UseDefaultCandidates("api"));
        using var consumer = new CancellationTokenSource();

        try
        {
            await foreach (var chunk in sp.GetRequiredService<ITextClient>().StreamAsync(Ask(), consumer.Token))
            {
                if (chunk.Kind == TextChunkKind.Content) consumer.Cancel();
            }
        }
        catch (OperationCanceledException)
        {
            // the consumer's own cancel is the caller's, so it ends the loop rather than becoming a chunk
        }

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> Endless(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct, TaskCompletionSource cancelled)
    {
        try
        {
            while (true)
            {
                await Task.Delay(20, ct);
                yield return new ChatResponseUpdate(ChatRole.Assistant, "x");
            }
        }
        finally
        {
            cancelled.TrySetResult();
        }
    }
}
