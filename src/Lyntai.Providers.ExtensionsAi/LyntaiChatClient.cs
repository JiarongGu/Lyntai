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
            LlmVerdict.Ok => new ChatResponse(AssistantMessage(reply))
            {
                Usage = MapUsage(reply.Usage),
                FinishReason = reply.ToolCalls is { Count: > 0 } ? ChatFinishReason.ToolCalls : null,
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
        Messages = [.. messages.Select(ToLlmMessage)],
        Model = options?.ModelId,
        MaxTokens = options?.MaxOutputTokens,
        Temperature = options?.Temperature,
        // parity with the forward bridge: carry the app's declared tools + structured-output schema through
        Tools = MapTools(options?.Tools),
        JsonSchema = (options?.ResponseFormat as ChatResponseFormatJson)?.Schema?.GetRawText(),
    };

    /// <summary>One MEAI <see cref="ChatMessage"/> → a canonical <see cref="LlmMessage"/>, the inverse of the
    /// forward bridge's mapping: an assistant turn carrying <see cref="FunctionCallContent"/>s becomes a
    /// tool-call turn; a <see cref="FunctionResultContent"/> becomes a tool-result turn; images
    /// (<see cref="DataContent"/>/<see cref="UriContent"/>) on a user turn become attachments; else plain text.</summary>
    private static LlmMessage ToLlmMessage(ChatMessage m)
    {
        var calls = m.Contents.OfType<FunctionCallContent>().ToList();
        if (calls.Count > 0)
            return LlmMessage.AssistantToolCalls(
                [.. calls.Select(fc => new LlmToolCall(fc.CallId, fc.Name, Lyntai.Text.JsonArgs.Serialize(fc.Arguments)))],
                m.Text);

        if (m.Contents.OfType<FunctionResultContent>().FirstOrDefault() is { } frc)
            return LlmMessage.ToolResult(frc.CallId, frc.Result as string ?? frc.Result?.ToString() ?? "");

        var attachments = m.Contents
            .Select(c => c switch
            {
                UriContent u => new LlmAttachment(u.MediaType, Uri: u.Uri.ToString()),
                DataContent d => new LlmAttachment(d.MediaType, d.Data.ToArray()),
                _ => null,
            })
            .Where(a => a is not null)
            .ToList();
        return attachments.Count > 0
            ? new LlmMessage(m.Role.Value, m.Text) { Attachments = [.. attachments!] }
            : new LlmMessage(m.Role.Value, m.Text);
    }

    private static IReadOnlyList<LlmTool>? MapTools(IList<AITool>? tools)
    {
        if (tools is not { Count: > 0 }) return null;
        return [.. tools.Select(t => new LlmTool(
            t.Name,
            string.IsNullOrEmpty(t.Description) ? null : t.Description,
            (t as AIFunction)?.JsonSchema.GetRawText()))];
    }

    /// <summary>Build the assistant reply message — carrying the model's native tool calls as
    /// <see cref="FunctionCallContent"/>s (completing the tool-calling round-trip) when the reply made any,
    /// else just the text.</summary>
    private static ChatMessage AssistantMessage(LlmReply reply)
    {
        if (reply.ToolCalls is not { Count: > 0 })
            return new ChatMessage(ChatRole.Assistant, reply.Text);
        var contents = new List<AIContent>();
        if (!string.IsNullOrEmpty(reply.Text)) contents.Add(new TextContent(reply.Text));
        foreach (var tc in reply.ToolCalls)
            contents.Add(new FunctionCallContent(tc.Id, tc.Name, Lyntai.Text.JsonArgs.Parse(tc.ArgumentsJson)));
        return new ChatMessage(ChatRole.Assistant, contents);
    }

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
