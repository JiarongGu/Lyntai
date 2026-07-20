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
        var timeout = options.ResolveTimeout(req);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
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
            return new LlmReply("", LlmVerdict.Timeout, Detail: $"{id}: no response within {timeout}");
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
        var timeout = options.ResolveTimeout(req);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout); // arm for the initial connect

        LlmUsage? usage = null;
        var sawContent = false;
        var sawToolCall = false;
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
                    timeoutCts.CancelAfter(timeout);                           // arm: inactivity clock for this read
                    var moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    timeoutCts.CancelAfter(Timeout.InfiniteTimeSpan);          // stop the clock while we + the consumer work
                    update = moved ? enumerator.Current : null;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (OperationCanceledException)
                {
                    error = LlmChunk.Error(LlmVerdict.Timeout, $"{id}: no response within {timeout}");
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
                    else if (content is FunctionCallContent) sawToolCall = true; // a streamed native tool call
                }
                if (update.Text is { Length: > 0 } text)
                {
                    sawContent = true;
                    yield return LlmChunk.Content(text);
                }
            }
        }

        if (!sawContent)
        {
            // a streamed native tool call (no text) is NOT a host failure — the streaming contract (LlmChunk)
            // just can't carry tool calls (deferred). Surface Unsupported (a capability gap — no fallback/
            // cooldown), not Failed.
            yield return sawToolCall
                ? LlmChunk.Error(LlmVerdict.Unsupported, $"{id}: streaming does not deliver native tool calls — use CompleteAsync for tool-calling")
                // the streaming twin of CompleteAsync's empty→Failed: a genuinely empty stream must surface
                // an error the router can fall over on, not end as a clean empty Final
                : LlmChunk.Error(LlmVerdict.Failed, $"{id}: empty response");
        }
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
        if (m.Role == "user" && m.Attachments is { Count: > 0 }) // images only make sense on a user turn
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
    /// them off the wire, occasionally a <see cref="JsonNode"/>) → a JSON object string. Reflection-free via
    /// the shared <see cref="Lyntai.Text.JsonArgs"/> (kept in sync with the MCP tool-host).</summary>
    private static string SerializeArgs(IDictionary<string, object?>? args) => Lyntai.Text.JsonArgs.Serialize(args);

    /// <summary>A JSON arguments string → the dictionary <see cref="FunctionCallContent"/> wants (values stay
    /// as detached <see cref="JsonNode"/>s, reflection-free) — via the shared <see cref="Lyntai.Text.JsonArgs"/>.</summary>
    private static IDictionary<string, object?>? ParseArgs(string argumentsJson) => Lyntai.Text.JsonArgs.Parse(argumentsJson);

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
