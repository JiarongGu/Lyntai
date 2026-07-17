using Lyntai;
using Lyntai.Llm;
using Lyntai.Providers.ExtensionsAi;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>The reverse bridge: a whole Lyntai composition consumed AS an IChatClient.</summary>
public class LyntaiChatClientTests
{
    private static (IChatClient Client, FakeLlmProvider Provider) Build()
    {
        var provider = new FakeLlmProvider("fake");
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddProvider(_ => provider).DefaultCandidates("fake"));
        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<ILlmClient>().AsChatClient(), provider);
    }

    [Fact]
    public async Task Chat_response_round_trips_text_and_usage()
    {
        var (chat, provider) = Build();
        provider.Replies.Enqueue(new LlmReply("lyntai as a provider", LlmVerdict.Ok, new LlmUsage(20, 5)));

        var response = await chat.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions { ModelId = "m1", MaxOutputTokens = 99, Temperature = 0.7f });

        Assert.Equal("lyntai as a provider", response.Text);
        Assert.Equal(20, response.Usage!.InputTokenCount);

        var req = provider.Calls.Single();
        Assert.Equal("m1", req.Model);
        Assert.Equal(99, req.MaxTokens);
        Assert.Equal("hello", req.Messages.Single().Content);
    }

    [Fact]
    public async Task Failed_verdict_surfaces_as_exception_meai_idiom()
    {
        var (chat, provider) = Build();
        provider.Replies.Enqueue(new LlmReply("", LlmVerdict.Failed, Detail: "all candidates down"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            chat.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]));

        Assert.Contains("all candidates down", ex.Message);
    }

    [Fact]
    public async Task Refused_maps_to_content_filter_finish_reason()
    {
        var (chat, provider) = Build();
        provider.Replies.Enqueue(new LlmReply("", LlmVerdict.Refused, Detail: "policy"));

        var response = await chat.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal(ChatFinishReason.ContentFilter, response.FinishReason);
    }

    [Fact]
    public async Task Streaming_yields_updates_then_usage()
    {
        var (chat, provider) = Build();
        provider.StreamScript = _ =>
            [LlmChunk.Content("str"), LlmChunk.Content("eam"), LlmChunk.Final(new LlmUsage(7, 2))];

        var texts = new List<string>();
        UsageDetails? usage = null;
        await foreach (var update in chat.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
        {
            if (update.Text is { Length: > 0 }) texts.Add(update.Text);
            usage = update.Contents.OfType<UsageContent>().FirstOrDefault()?.Details ?? usage;
        }

        Assert.Equal(["str", "eam"], texts);
        Assert.Equal(7, usage!.InputTokenCount);
    }

    [Fact]
    public async Task Streaming_error_chunk_surfaces_as_exception()
    {
        var (chat, provider) = Build();
        provider.StreamScript = _ => [LlmChunk.Error(LlmVerdict.Timeout, "too slow")];

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in chat.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")])) { }
        });
    }
}
