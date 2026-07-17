using System.Runtime.CompilerServices;
using Lyntai.Llm;
using Microsoft.Extensions.AI;

namespace Lyntai.Providers.ExtensionsAi;

/// <summary>
/// The reverse bridge: expose a whole Lyntai composition AS a <see cref="IChatClient"/>, so any
/// MEAI-speaking application can adopt Lyntai as its chat provider and silently gain routing,
/// fallback, dead-host cooldown, and the ops layer. Non-Ok verdicts surface as exceptions
/// (MEAI's failure idiom), except <see cref="LlmVerdict.Refused"/> which maps to a
/// <see cref="ChatFinishReason.ContentFilter"/> response.
/// </summary>
public sealed class LyntaiChatClient(ILlmClient client) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var reply = await client.CompleteAsync(MapRequest(messages, options), cancellationToken).ConfigureAwait(false);
        return reply.Verdict switch
        {
            LlmVerdict.Ok => new ChatResponse(new ChatMessage(ChatRole.Assistant, reply.Text))
            {
                Usage = MapUsage(reply.Usage),
            },
            LlmVerdict.Refused => new ChatResponse(new ChatMessage(ChatRole.Assistant, reply.Text))
            {
                FinishReason = ChatFinishReason.ContentFilter,
                Usage = MapUsage(reply.Usage),
            },
            _ => throw new InvalidOperationException($"lyntai: {reply.Verdict} — {reply.Detail}"),
        };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in client.StreamAsync(MapRequest(messages, options), cancellationToken).ConfigureAwait(false))
        {
            switch (chunk.Kind)
            {
                case LlmChunkKind.Content:
                    yield return new ChatResponseUpdate(ChatRole.Assistant, chunk.Text);
                    break;
                case LlmChunkKind.Final when chunk.Usage is not null:
                    yield return new ChatResponseUpdate
                    {
                        Contents = [new UsageContent(MapUsage(chunk.Usage)!)],
                    };
                    break;
                case LlmChunkKind.Error:
                    throw new InvalidOperationException($"lyntai: {chunk.Verdict} — {chunk.Detail}");
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { } // nothing owned — the Lyntai services belong to the DI container

    private static LlmRequest MapRequest(IEnumerable<ChatMessage> messages, ChatOptions? options) => new()
    {
        Messages = [.. messages.Select(m => new LlmMessage(m.Role.Value, m.Text))],
        Model = options?.ModelId,
        MaxTokens = options?.MaxOutputTokens,
        Temperature = options?.Temperature,
    };

    private static UsageDetails? MapUsage(LlmUsage? usage) =>
        usage is null ? null : new UsageDetails
        {
            InputTokenCount = usage.InputTokens,
            OutputTokenCount = usage.OutputTokens,
            CachedInputTokenCount = usage.CacheReadTokens,
        };
}

public static class LyntaiChatClientExtensions
{
    /// <summary>Expose this Lyntai composition as a Microsoft.Extensions.AI <see cref="IChatClient"/>.</summary>
    public static IChatClient AsChatClient(this ILlmClient client) => new LyntaiChatClient(client);
}
