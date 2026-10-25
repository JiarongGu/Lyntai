using System.Runtime.CompilerServices;
using System.Text.Json;
using Lyntai.Inference;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage; // Lyntai.Storage has one too

namespace Lyntai.Tests.Inference.ChatClients;

/// <summary>Lyntai as an <see cref="IChatClient"/>, for code built on Microsoft.Extensions.AI or Agent
/// Framework: every call goes through the front door, so candidates, fallback, cooldown, budget and cache apply.
/// <para>A tool or a schema is sent with <c>RequireNativeSupport</c>, so a candidate that cannot carry it is
/// skipped and the call fails with <see cref="ProviderVerdict.Unsupported"/> rather than answering in prose.
/// <b>Tools stay the caller's</b>: this returns the model's call, and the invoking middleware on top of it
/// (<c>UseFunctionInvocation</c>) runs it.</para>
/// <para>It does not own the <see cref="ITextClient"/>: <see cref="Dispose"/> releases nothing.</para></summary>
public sealed class TextClientChatClient(ITextClient text, string consumer = ProviderConsumers.Default) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var reply = await text.CompleteAsync(ToRequest(messages, options), cancellationToken);
        if (reply.Verdict is not (ProviderVerdict.Ok or ProviderVerdict.Refused))
            throw new TextCallFailedException(reply.Verdict, reply.Detail);

        var contents = new List<AIContent>();
        if (reply.Text.Length > 0) contents.Add(new TextContent(reply.Text));
        foreach (var call in reply.ToolCalls ?? []) contents.Add(ToContent(call));
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, contents))
        {
            Usage = ToUsage(reply.Usage),
            FinishReason = reply.Verdict == ProviderVerdict.Refused ? ChatFinishReason.ContentFilter
                : reply.ToolCalls is { Count: > 0 } ? ChatFinishReason.ToolCalls : ChatFinishReason.Stop,
        };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var calledTool = false;
        await foreach (var chunk in text.StreamAsync(ToRequest(messages, options), cancellationToken))
        {
            switch (chunk.Kind)
            {
                case TextChunkKind.Content:
                    yield return new ChatResponseUpdate(ChatRole.Assistant, chunk.Text);
                    break;
                case TextChunkKind.ToolCall:
                    calledTool = true;
                    yield return new ChatResponseUpdate(ChatRole.Assistant, [ToContent(chunk.ToolCall!)]);
                    break;
                case TextChunkKind.Final:
                    var done = new ChatResponseUpdate(ChatRole.Assistant, [])
                    {
                        FinishReason = calledTool ? ChatFinishReason.ToolCalls : ChatFinishReason.Stop,
                    };
                    if (ToUsage(chunk.Usage) is { } usage) done.Contents.Add(new UsageContent(usage));
                    yield return done;
                    break;
                case TextChunkKind.Error when chunk.Verdict == ProviderVerdict.Refused:
                    yield return new ChatResponseUpdate(ChatRole.Assistant, []) { FinishReason = ChatFinishReason.ContentFilter };
                    yield break;
                case TextChunkKind.Error:
                    throw new TextCallFailedException(chunk.Verdict, chunk.Detail);
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is not null ? null
        : serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("lyntai")
        : serviceType.IsInstanceOfType(this) ? this
        : null;

    public void Dispose()
    {
    }

    private TextRequest ToRequest(IEnumerable<ChatMessage> messages, ChatOptions? options) => new()
    {
        Messages = messages.SelectMany(ToMessages).ToList(),
        Model = options?.ModelId,
        MaxTokens = options?.MaxOutputTokens,
        Temperature = options?.Temperature,
        JsonSchema = options?.ResponseFormat is ChatResponseFormatJson { Schema: { } schema } ? schema.GetRawText() : null,
        Tools = options?.Tools?.OfType<AIFunctionDeclaration>()
            .Select(t => new TextTool(t.Name, t.Description, t.JsonSchema.GetRawText())).ToList(),
        Consumer = consumer,
        RequireNativeSupport = options?.Tools is { Count: > 0 } || options?.ResponseFormat is ChatResponseFormatJson { Schema: not null },
    };

    private static IEnumerable<TextMessage> ToMessages(ChatMessage m)
    {
        // a tool turn can carry several results; each becomes its own message, answering its own call
        foreach (var result in m.Contents.OfType<FunctionResultContent>())
            yield return TextMessage.ToolResult(result.CallId, result.Result?.ToString() ?? "");
        if (m.Role == ChatRole.Tool) yield break;

        var calls = m.Contents.OfType<FunctionCallContent>()
            .Select(c => new TextToolCall(c.CallId, c.Name, JsonSerializer.Serialize(c.Arguments ?? new Dictionary<string, object?>())))
            .ToList();
        var attachments = m.Contents.Select(c => c switch
        {
            DataContent { Data.IsEmpty: false } d => new TextAttachment(d.MediaType, d.Data.ToArray()),
            UriContent u => new TextAttachment(u.MediaType, Uri: u.Uri.ToString()),
            _ => null,
        }).OfType<TextAttachment>().ToList();
        yield return new TextMessage(m.Role.Value, m.Text)
        {
            ToolCalls = calls.Count > 0 ? calls : null,
            Attachments = attachments.Count > 0 ? attachments : null,
        };
    }

    private static FunctionCallContent ToContent(TextToolCall call) => new(call.Id, call.Name,
        JsonSerializer.Deserialize<Dictionary<string, object?>>(call.ArgumentsJson));

    private static UsageDetails? ToUsage(TextUsage? u) => u is null ? null : new UsageDetails
    {
        InputTokenCount = u.InputTokens,
        OutputTokenCount = u.OutputTokens,
        TotalTokenCount = u.InputTokens + u.OutputTokens,
        CachedInputTokenCount = u.CacheReadTokens,
    };
}

/// <summary>A Lyntai call that did not answer, carrying the verdict an <see cref="IChatClient"/> has no field for.</summary>
public sealed class TextCallFailedException(ProviderVerdict verdict, string? detail)
    : Exception($"{verdict}: {detail}")
{
    public ProviderVerdict Verdict { get; } = verdict;
}
