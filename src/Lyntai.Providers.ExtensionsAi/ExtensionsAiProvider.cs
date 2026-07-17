using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lyntai.Llm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Providers.ExtensionsAi;

/// <summary>
/// Bridges any <see cref="IChatClient"/> (the whole Microsoft.Extensions.AI ecosystem — OpenAI,
/// Azure, Ollama, Anthropic API, …) into a Lyntai <see cref="ILlmProvider"/>: request/option
/// mapping, streaming, usage, and verdict classification from exceptions/finish reasons.
/// Native tool-calling is bridged: <see cref="LlmRequest.Tools"/> map to declaration-only
/// <see cref="AIFunctionDeclaration"/>s on <see cref="ChatOptions.Tools"/>, the model's
/// <see cref="FunctionCallContent"/> surfaces on <see cref="LlmReply.ToolCalls"/>, and tool-result
/// turns map back to <see cref="FunctionResultContent"/> — Lyntai's tool loop drives execution.
/// </summary>
public sealed class ExtensionsAiProvider(
    string id,
    IChatClient client,
    LyntaiOptions options,
    ILogger<ExtensionsAiProvider>? logger = null) : ILlmProvider
{
    private readonly ILogger _logger = logger ?? NullLogger<ExtensionsAiProvider>.Instance;

    public string Id => id;

    public bool IsAvailable => true; // the client exists; real availability shows up as verdicts

    public bool SupportsToolCalls => true;

    public async Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(options.ProviderTimeout);
        try
        {
            var response = await client.GetResponseAsync(MapMessages(req), MapOptions(req), timeoutCts.Token)
                .ConfigureAwait(false);

            var text = response.Text;
            var usage = MapUsage(response.Usage);
            if (response.FinishReason == ChatFinishReason.ContentFilter)
                return new LlmReply(text, LlmVerdict.Refused, usage, $"{id}: content filter");
            // a tool-call turn is a SUCCESSFUL reply with (usually) empty text — surface it before the
            // empty→Failed branch (the tool loop drives the next turn)
            var toolCalls = ExtractToolCalls(response);
            if (toolCalls is { Count: > 0 })
                return new LlmReply(text, LlmVerdict.Ok, usage) { ToolCalls = toolCalls };
            if (string.IsNullOrEmpty(text))
                return new LlmReply("", LlmVerdict.Failed, usage, $"{id}: empty response");
            return new LlmReply(text, LlmVerdict.Ok, usage);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            return new LlmReply("", LlmVerdict.Timeout, Detail: $"{id}: no response within {options.ProviderTimeout}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Id}: chat client faulted", id);
            return new LlmReply("", LlmVerdictClassifier.FromException(ex), Detail: $"{id}: {ex.Message}");
        }
    }

    public async IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // the timeout is an INACTIVITY clock on the provider (re-armed before each MoveNextAsync,
        // stopped while we and the consumer work) — a slow-but-healthy stream or a slow reader is
        // never killed under it. The single-shot CancelAfter this replaces counted consumer dwell
        // time and killed healthy streams; ProcessRunner.StreamLinesAsync uses the same pattern.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(options.ProviderTimeout); // arm for the initial connect

        LlmUsage? usage = null;
        var sawContent = false;
        var enumerator = client.GetStreamingResponseAsync(MapMessages(req), MapOptions(req), timeoutCts.Token)
            .GetAsyncEnumerator(timeoutCts.Token);
        await using (enumerator.ConfigureAwait(false))
        {
            while (true)
            {
                ChatResponseUpdate? update = null;
                LlmChunk? error = null;
                try
                {
                    timeoutCts.CancelAfter(options.ProviderTimeout);           // arm: inactivity clock for this read
                    var moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    timeoutCts.CancelAfter(Timeout.InfiniteTimeSpan);          // stop the clock while we + the consumer work
                    update = moved ? enumerator.Current : null;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (OperationCanceledException)
                {
                    error = LlmChunk.Error(LlmVerdict.Timeout, $"{id}: no response within {options.ProviderTimeout}");
                }
                catch (Exception ex)
                {
                    error = LlmChunk.Error(LlmVerdictClassifier.FromException(ex), $"{id}: {ex.Message}");
                }
                if (error is not null)
                {
                    yield return error;
                    yield break;
                }
                if (update is null) break;

                foreach (var content in update.Contents)
                {
                    if (content is UsageContent u) usage = MapUsage(u.Details);
                }
                if (update.Text is { Length: > 0 } text)
                {
                    sawContent = true;
                    yield return LlmChunk.Content(text);
                }
            }
        }

        // the streaming twin of CompleteAsync's empty→Failed: a zero-content stream must surface an
        // error the router can fall over on, not end as a clean empty Final
        if (!sawContent)
            yield return LlmChunk.Error(LlmVerdict.Failed, $"{id}: empty response");
        else
            yield return LlmChunk.Final(usage);
    }

    private static IList<ChatMessage> MapMessages(LlmRequest req) => [.. req.Messages.Select(ToChatMessage)];

    /// <summary>One canonical message → MEAI <see cref="ChatMessage"/>. An assistant tool-call turn
    /// becomes <see cref="FunctionCallContent"/>s; a tool-result turn becomes a
    /// <see cref="FunctionResultContent"/> on the Tool role; everything else is plain text.</summary>
    private static ChatMessage ToChatMessage(LlmMessage m)
    {
        if (m.ToolCalls is { Count: > 0 })
            return new ChatMessage(ChatRole.Assistant,
                [.. m.ToolCalls.Select(tc => (AIContent)new FunctionCallContent(tc.Id, tc.Name, ParseArgs(tc.ArgumentsJson)))]);
        if (m.ToolCallId is not null)
            return new ChatMessage(ChatRole.Tool, [new FunctionResultContent(m.ToolCallId, m.Content)]);
        if (m.Attachments is { Count: > 0 })
        {
            // vision: text + one image content per attachment (DataContent for bytes, UriContent for a URL)
            var contents = new List<AIContent> { new TextContent(m.Content) };
            foreach (var a in m.Attachments)
                contents.Add(a.Uri is not null
                    ? new UriContent(a.Uri, a.MediaType)
                    : new DataContent(a.Data ?? ReadOnlyMemory<byte>.Empty, a.MediaType));
            return new ChatMessage(new ChatRole(m.Role), contents);
        }
        return new ChatMessage(new ChatRole(m.Role), m.Content);
    }

    /// <summary>Pull the model's <see cref="FunctionCallContent"/>s off a response into
    /// <see cref="LlmToolCall"/>s (arguments serialized back to a JSON string for the tool loop). Uses
    /// <see cref="JsonNode"/>, not reflection-based <c>JsonSerializer</c>, to keep the package AOT-clean.</summary>
    private static IReadOnlyList<LlmToolCall>? ExtractToolCalls(ChatResponse response)
    {
        List<LlmToolCall>? calls = null;
        foreach (var message in response.Messages)
            foreach (var content in message.Contents)
                if (content is FunctionCallContent fc)
                    (calls ??= []).Add(new LlmToolCall(fc.CallId, fc.Name, SerializeArgs(fc.Arguments)));
        return calls;
    }

    /// <summary>An argument dictionary (values are typically <see cref="JsonElement"/> as MEAI parsed
    /// them off the wire, occasionally a <see cref="JsonNode"/>) → a JSON object string.</summary>
    private static string SerializeArgs(IDictionary<string, object?>? args)
    {
        if (args is not { Count: > 0 }) return "{}";
        var obj = new JsonObject();
        foreach (var (key, value) in args)
            obj[key] = value switch
            {
                null => null,
                JsonNode n => n.DeepClone(),
                JsonElement e => JsonNode.Parse(e.GetRawText()),
                // MEAI clients can hand back boxed CLR primitives — keep their JSON type (a 3 must not
                // become "3"); typed JsonValue.Create overloads are reflection-free (stays AOT-clean)
                bool b => JsonValue.Create(b),
                int i => JsonValue.Create(i),
                long l => JsonValue.Create(l),
                double d => JsonValue.Create(d),
                decimal m => JsonValue.Create(m),
                string s => JsonValue.Create(s),
                var other => JsonValue.Create(other.ToString()), // last-resort fallback
            };
        return obj.ToJsonString();
    }

    /// <summary>A JSON arguments string → the dictionary <see cref="FunctionCallContent"/> wants. Values
    /// stay as detached <see cref="JsonNode"/>s (reflection-free); MEAI serializes them on the wire.</summary>
    private static IDictionary<string, object?>? ParseArgs(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson)) return null;
        try
        {
            return JsonNode.Parse(argumentsJson) is JsonObject obj
                ? obj.ToDictionary(kv => kv.Key, kv => (object?)kv.Value?.DeepClone())
                : null;
        }
        catch (JsonException) { return null; }
    }

    private static ChatOptions MapOptions(LlmRequest req)
    {
        var chatOptions = new ChatOptions
        {
            ModelId = req.Model,
            MaxOutputTokens = req.MaxTokens,
            Temperature = (float?)req.Temperature,
        };
        if (req.Tools is { Count: > 0 })
            chatOptions.Tools = [.. req.Tools.Select(t => (AITool)new LyntaiToolDeclaration(t))];
        if (!string.IsNullOrEmpty(req.JsonSchema))
        {
            try
            {
                using var doc = JsonDocument.Parse(req.JsonSchema);
                chatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema(doc.RootElement.Clone(), "result");
            }
            catch (JsonException)
            {
                // unparseable schema — send the request unconstrained rather than failing it
            }
        }
        return chatOptions;
    }

    private static LlmUsage? MapUsage(UsageDetails? usage) =>
        usage is null ? null : new LlmUsage(
            usage.InputTokenCount ?? 0,
            usage.OutputTokenCount ?? 0,
            usage.CachedInputTokenCount ?? 0); // cached tokens bill at discounted rates — keep them
}
