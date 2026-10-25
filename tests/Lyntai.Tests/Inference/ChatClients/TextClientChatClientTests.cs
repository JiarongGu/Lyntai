using System.Text.Json;
using Lyntai.Inference;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Inference.ChatClients;

/// <summary>Lyntai handed to code that wants an <c>IChatClient</c>. The backends behind the front door are
/// bridges the test scripts, so what is asserted is the CONTRACT an Agent Framework or Microsoft.Extensions.AI
/// caller meets: what it sends arrives as a <see cref="TextRequest"/>, and what Lyntai answers comes back as
/// MEAI types (<c>docs/extensions-ai.md</c>).</summary>
public class TextClientChatClientTests
{
    private static ServiceProvider Build(Action<LyntaiBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddLyntai(configure);
        return services.BuildServiceProvider();
    }

    private static TextClientChatClient Client(ServiceProvider sp, string consumer = ProviderConsumers.Default) =>
        new(sp.GetRequiredService<ITextClient>(), consumer);

    private static LyntaiBuilder Backend(LyntaiBuilder b, string id, Func<TextRequest, TextResponse> answer,
        Func<TextRequest, IAsyncEnumerable<TextChunk>>? stream = null, bool tools = true) =>
        b.AddBridgeProvider(id, (req, _) => Task.FromResult(answer(req)),
            stream is null ? null : (req, _) => stream(req),
            new ProviderCapabilities { SupportsToolCalls = tools, SupportsStreamingToolCalls = tools });

    private static async IAsyncEnumerable<TextChunk> Chunks(params TextChunk[] chunks)
    {
        await Task.CompletedTask;
        foreach (var c in chunks) yield return c;
    }

    private static CancellationToken Safety() => new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

    private static async Task<List<ChatResponseUpdate>> Collect(IAsyncEnumerable<ChatResponseUpdate> stream)
    {
        var all = new List<ChatResponseUpdate>();
        await foreach (var u in stream) all.Add(u);
        return all;
    }

    [Fact]
    public async Task A_request_arrives_as_a_TextRequest_and_the_answer_returns_as_MEAI_types()
    {
        TextRequest? seen = null;
        using var sp = Build(b => Backend(b, "api", req =>
        {
            seen = req;
            return new TextResponse("bonjour", ProviderVerdict.Ok, new TextUsage(7, 2, 3));
        }).UseDefaultCandidates("api"));

        var response = await Client(sp, consumer: "agent-x").GetResponseAsync(
            [new ChatMessage(ChatRole.System, "be brief"), new ChatMessage(ChatRole.User, "say hi")],
            new ChatOptions { ModelId = "m-2", MaxOutputTokens = 32, Temperature = 0.5f }, Safety());

        Assert.Equal("bonjour", response.Text);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);
        Assert.Equal((7, 2, 9, 3), (response.Usage!.InputTokenCount, response.Usage.OutputTokenCount,
            response.Usage.TotalTokenCount, response.Usage.CachedInputTokenCount));
        Assert.Equal(["system", "user"], seen!.Messages.Select(m => m.Role));
        Assert.Equal(("m-2", 32, 0.5, "agent-x"), (seen.Model, seen.MaxTokens, seen.Temperature, seen.Consumer));
    }

    [Fact]
    public async Task A_function_declaration_becomes_a_TextTool_and_the_models_call_comes_back_as_a_FunctionCallContent()
    {
        TextRequest? seen = null;
        using var sp = Build(b => Backend(b, "api", req =>
        {
            seen = req;
            return new TextResponse("", ProviderVerdict.Ok) { ToolCalls = [new TextToolCall("c1", "weather", """{"city":"Oslo"}""")] };
        }).UseDefaultCandidates("api"));
        var declaration = AIFunctionFactory.CreateDeclaration("weather", "Current weather",
            JsonDocument.Parse("""{"type":"object","properties":{"city":{"type":"string"}}}""").RootElement.Clone());

        var response = await Client(sp).GetResponseAsync("weather?", new ChatOptions { Tools = [declaration] }, Safety());

        var tool = Assert.Single(seen!.Tools!);
        Assert.Equal(("weather", "Current weather"), (tool.Name, tool.Description));
        Assert.Contains("\"city\"", tool.ParametersJsonSchema);
        Assert.True(seen.RequireNativeSupport);
        var call = Assert.Single(response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>());
        Assert.Equal(("c1", "weather"), (call.CallId, call.Name));
        Assert.Equal(ChatFinishReason.ToolCalls, response.FinishReason);
    }

    [Fact]
    public async Task The_invoking_middleware_runs_the_tool_and_the_second_turn_carries_the_call_and_its_result()
    {
        // what Agent Framework does over any IChatClient: FunctionInvokingChatClient owns the loop
        var turns = new List<TextRequest>();
        using var sp = Build(b => Backend(b, "api", req =>
        {
            turns.Add(req);
            return turns.Count == 1
                ? new TextResponse("", ProviderVerdict.Ok) { ToolCalls = [new TextToolCall("c1", "add", """{"a":2,"b":3}""")] }
                : new TextResponse("the sum is 5", ProviderVerdict.Ok);
        }).UseDefaultCandidates("api"));
        var add = AIFunctionFactory.Create((int a, int b) => a + b, "add", "Add two numbers");
        using var chat = Client(sp).AsBuilder().UseFunctionInvocation().Build();

        var response = await chat.GetResponseAsync("2+3?", new ChatOptions { Tools = [add] }, Safety());

        Assert.Equal("the sum is 5", response.Text);
        Assert.Equal(2, turns.Count);
        var second = turns[1].Messages;
        Assert.Equal(["user", "assistant", "tool"], second.Select(m => m.Role));
        Assert.Equal("c1", Assert.Single(second[1].ToolCalls!).Id);
        Assert.Equal("c1", second[2].ToolCallId);
        Assert.Equal("5", second[2].Content);
    }

    [Fact]
    public async Task A_tool_request_is_REFUSED_by_a_candidate_that_cannot_call_tools_rather_than_answered_in_prose()
    {
        using var sp = Build(b => Backend(b, "prose", _ => new TextResponse("I would call a tool", ProviderVerdict.Ok), tools: false)
            .UseDefaultCandidates("prose"));
        var declaration = AIFunctionFactory.CreateDeclaration("weather", null, JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone());

        var failure = await Assert.ThrowsAsync<TextCallFailedException>(
            () => Client(sp).GetResponseAsync("weather?", new ChatOptions { Tools = [declaration] }, Safety()));

        Assert.Equal(ProviderVerdict.Unsupported, failure.Verdict);
    }

    [Fact]
    public async Task A_schema_travels_as_the_request_JSON_schema()
    {
        TextRequest? seen = null;
        using var sp = Build(b => b.AddBridgeProvider("api", (req, _) =>
        {
            seen = req;
            return Task.FromResult(new TextResponse("{}", ProviderVerdict.Ok));
        }, capabilities: new ProviderCapabilities { SupportsStructuredOutput = true }).UseDefaultCandidates("api"));
        var schema = JsonDocument.Parse("""{"type":"object","properties":{"n":{"type":"integer"}}}""").RootElement.Clone();

        await Client(sp).GetResponseAsync("count", new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(schema) }, Safety());

        Assert.Contains("\"n\"", seen!.JsonSchema);
        Assert.True(seen.RequireNativeSupport);
    }

    [Fact]
    public async Task A_failed_call_throws_with_the_verdict_and_a_refusal_is_a_content_filter_response()
    {
        using var failing = Build(b => Backend(b, "api", _ => new TextResponse("", ProviderVerdict.Failed, Detail: "down"))
            .UseDefaultCandidates("api"));
        using var refusing = Build(b => Backend(b, "api", _ => new TextResponse("no", ProviderVerdict.Refused))
            .UseDefaultCandidates("api"));

        var failure = await Assert.ThrowsAsync<TextCallFailedException>(
            () => Client(failing).GetResponseAsync("x", cancellationToken: Safety()));
        var refused = await Client(refusing).GetResponseAsync("x", cancellationToken: Safety());

        Assert.Equal(ProviderVerdict.Failed, failure.Verdict);
        Assert.Equal(ChatFinishReason.ContentFilter, refused.FinishReason);
    }

    [Fact]
    public async Task Fallback_happens_INSIDE_Lyntai_so_the_caller_never_sees_the_candidate_that_failed()
    {
        using var sp = Build(b => Backend(Backend(b, "down", _ => new TextResponse("", ProviderVerdict.Failed)),
            "up", _ => new TextResponse("served", ProviderVerdict.Ok)).UseDefaultCandidates("down", "up"));

        var response = await Client(sp).GetResponseAsync("x", cancellationToken: Safety());

        Assert.Equal("served", response.Text);
    }

    [Fact]
    public async Task The_callers_token_reaches_the_backend_and_its_cancel_propagates()
    {
        CancellationToken seen = default;
        using var sp = Build(b => b.AddBridgeProvider("api", async (_, ct) =>
        {
            seen = ct;
            await Task.Delay(Timeout.Infinite, ct);
            return new TextResponse("", ProviderVerdict.Ok);
        }).UseDefaultCandidates("api"));
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(sp).GetResponseAsync("x", cancellationToken: caller.Token));

        Assert.True(seen.IsCancellationRequested);
    }

    [Fact]
    public async Task A_stream_maps_content_a_tool_call_and_the_usage_into_updates()
    {
        using var sp = Build(b => Backend(b, "api", _ => new TextResponse("x", ProviderVerdict.Ok), _ => Chunks(
            TextChunk.Content("he"), TextChunk.Content("llo"),
            TextChunk.Tool(new TextToolCall("c1", "weather", """{"city":"Rome"}""")),
            TextChunk.Final(new TextUsage(4, 2)))).UseDefaultCandidates("api"));

        var updates = await Collect(Client(sp).GetStreamingResponseAsync("x", cancellationToken: Safety()));

        Assert.Equal("hello", string.Concat(updates.Select(u => u.Text)));
        Assert.Equal("weather", Assert.Single(updates.SelectMany(u => u.Contents).OfType<FunctionCallContent>()).Name);
        var usage = Assert.Single(updates.SelectMany(u => u.Contents).OfType<UsageContent>()).Details;
        Assert.Equal((4, 2), (usage.InputTokenCount, usage.OutputTokenCount));
        Assert.Equal(ChatFinishReason.ToolCalls, updates[^1].FinishReason);
    }

    [Fact]
    public async Task A_stream_that_fails_before_content_falls_over_inside_Lyntai_and_the_caller_sees_a_clean_stream()
    {
        using var sp = Build(b => Backend(Backend(b, "down", _ => new TextResponse("", ProviderVerdict.Failed),
                _ => Chunks(TextChunk.Error(ProviderVerdict.Failed, "reset"))),
            "up", _ => new TextResponse("x", ProviderVerdict.Ok), _ => Chunks(TextChunk.Content("served"), TextChunk.Final()))
            .UseDefaultCandidates("down", "up"));

        var updates = await Collect(Client(sp).GetStreamingResponseAsync("x", cancellationToken: Safety()));

        Assert.Equal("served", string.Concat(updates.Select(u => u.Text)));
    }

    [Fact]
    public async Task A_stream_that_fails_after_content_throws_with_the_verdict_after_the_content_already_read()
    {
        using var sp = Build(b => Backend(b, "api", _ => new TextResponse("x", ProviderVerdict.Ok), _ => Chunks(
            TextChunk.Content("half"), TextChunk.Error(ProviderVerdict.Failed, "reset"))).UseDefaultCandidates("api"));
        var read = new List<string>();

        var failure = await Assert.ThrowsAsync<TextCallFailedException>(async () =>
        {
            await foreach (var u in Client(sp).GetStreamingResponseAsync("x", cancellationToken: Safety())) read.Add(u.Text);
        });

        Assert.Equal(["half"], read);
        Assert.Equal(ProviderVerdict.Failed, failure.Verdict);
    }

    [Fact]
    public async Task Disposing_the_adapter_does_not_dispose_the_front_door_it_wraps()
    {
        using var sp = Build(b => Backend(b, "api", _ => new TextResponse("still here", ProviderVerdict.Ok)).UseDefaultCandidates("api"));
        var front = sp.GetRequiredService<ITextClient>();

        Client(sp).Dispose();

        Assert.Equal("still here", (await front.CompleteAsync(new TextRequest { Messages = [TextMessage.User("x")] }, Safety())).Text);
    }

    [Fact]
    public void It_describes_itself_and_answers_for_its_own_type()
    {
        using var sp = Build(b => Backend(b, "api", _ => new TextResponse("x", ProviderVerdict.Ok)).UseDefaultCandidates("api"));
        var client = Client(sp);

        Assert.Equal("lyntai", client.GetService<ChatClientMetadata>()!.ProviderName);
        Assert.Same(client, client.GetService<TextClientChatClient>());
        Assert.Null(client.GetService<ChatClientMetadata>("a-key"));
    }
}
