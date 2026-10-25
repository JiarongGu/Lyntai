using System.Runtime.CompilerServices;
using System.Text.Json;
using Lyntai.Inference;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage; // Lyntai.Storage has one too

namespace Lyntai.Tests.Inference.ChatClients;

/// <summary>An <see cref="IChatClient"/> as a Lyntai backend: the mapping <c>AddBridgeProvider</c> asks you to
/// write, once. Pass the three members to the builder; Lyntai adds routing, fallback, cooldown, admission and the
/// provider timeout around them.
/// <para>Never put <c>UseFunctionInvocation()</c> under it: the client would run the tools itself, and
/// Lyntai's tool loop and approver would never see a call.</para></summary>
public static class ChatClientBackend
{
    /// <summary>What this mapping carries on the wire. Tools stream and complete; a JSON schema and images are
    /// opt-in, because only the wrapped client knows whether it honours them.</summary>
    public static ProviderCapabilities Capabilities(bool structuredOutput = false, bool vision = false) => new()
    {
        Accepts = vision ? [ProviderKinds.Text, ProviderKinds.Image] : [ProviderKinds.Text],
        SupportsToolCalls = true,
        SupportsStreamingToolCalls = true,
        SupportsStructuredOutput = structuredOutput,
    };

    public static async Task<TextResponse> CompleteAsync(IChatClient client, TextRequest req, CancellationToken ct)
    {
        try
        {
            var response = await client.GetResponseAsync(ToMessages(req), ToOptions(req), ct);
            var usage = ToUsage(response.Usage);
            var calls = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Select(ToCall).ToList();

            if (response.FinishReason == ChatFinishReason.ContentFilter)
                return new TextResponse(response.Text, ProviderVerdict.Refused, usage, "content filter");
            // a tool-call turn is a successful reply with no text
            if (calls.Count > 0)
                return new TextResponse(response.Text, ProviderVerdict.Ok, usage) { ToolCalls = calls };
            if (response.Text.Length == 0)
                return new TextResponse("", ProviderVerdict.Failed, usage, "empty reply");
            return new TextResponse(response.Text, ProviderVerdict.Ok, usage);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // add your SDK's typed status here (e.g. a 429 as RateLimited); the classifier reads text otherwise
            return new TextResponse("", ProviderVerdictClassifier.FromException(ex), Detail: ex.Message);
        }
    }

    public static async IAsyncEnumerable<TextChunk> StreamAsync(
        IChatClient client, TextRequest req, [EnumeratorCancellation] CancellationToken ct)
    {
        await using var updates = client.GetStreamingResponseAsync(ToMessages(req), ToOptions(req), ct)
            .GetAsyncEnumerator(ct);
        TextUsage? usage = null;
        var produced = false;
        while (true)
        {
            ChatResponseUpdate? update = null;
            TextChunk? failure = null;
            try
            {
                if (await updates.MoveNextAsync()) update = updates.Current;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failure = TextChunk.Error(ProviderVerdictClassifier.FromException(ex), ex.Message);
            }
            if (failure is not null)
            {
                yield return failure;
                yield break;
            }
            if (update is null) break;

            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextContent { Text.Length: > 0 } text:
                        produced = true;
                        yield return TextChunk.Content(text.Text);
                        break;
                    case FunctionCallContent call: // a client yields each call whole, never as fragments
                        produced = true;
                        yield return TextChunk.Tool(ToCall(call));
                        break;
                    case UsageContent reported:
                        usage = ToUsage(reported.Details);
                        break;
                }
            }
            if (update.FinishReason == ChatFinishReason.ContentFilter)
            {
                yield return TextChunk.Error(ProviderVerdict.Refused, "content filter");
                yield break;
            }
        }
        yield return produced ? TextChunk.Final(usage) : TextChunk.Error(ProviderVerdict.Failed, "empty stream");
    }

    private static IEnumerable<ChatMessage> ToMessages(TextRequest req) => req.Messages.Select(m => m.Role switch
    {
        "tool" => new ChatMessage(ChatRole.Tool, [new FunctionResultContent(m.ToolCallId ?? "", m.Content)]),
        _ => new ChatMessage(new ChatRole(m.Role), ToParts(m)),
    });

    private static List<AIContent> ToParts(TextMessage m)
    {
        var parts = new List<AIContent>();
        if (m.Content.Length > 0) parts.Add(new TextContent(m.Content));
        foreach (var a in m.Attachments ?? [])
            parts.Add(a.Data is { } bytes ? new DataContent(bytes, a.MediaType) : new UriContent(a.Uri!, a.MediaType));
        foreach (var c in m.ToolCalls ?? [])
            parts.Add(new FunctionCallContent(c.Id, c.Name,
                JsonSerializer.Deserialize<Dictionary<string, object?>>(c.ArgumentsJson)));
        return parts;
    }

    private static ChatOptions ToOptions(TextRequest req) => new()
    {
        ModelId = req.Model,
        MaxOutputTokens = req.MaxTokens,
        Temperature = (float?)req.Temperature,
        ResponseFormat = req.JsonSchema is { } schema
            ? ChatResponseFormat.ForJsonSchema(Schema(schema), "response")
            : null,
        Tools = req.Tools?.Select(t => (AITool)AIFunctionFactory.CreateDeclaration(
            t.Name, t.Description ?? "", Schema(t.ParametersJsonSchema ?? """{"type":"object"}"""))).ToList(),
    };

    private static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static TextToolCall ToCall(FunctionCallContent call) =>
        new(call.CallId, call.Name, JsonSerializer.Serialize(call.Arguments ?? new Dictionary<string, object?>()));

    private static TextUsage? ToUsage(UsageDetails? u) => u is null
        ? null
        : new TextUsage(u.InputTokenCount ?? 0, u.OutputTokenCount ?? 0, u.CachedInputTokenCount ?? 0);
}
