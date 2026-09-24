using System.Text.Json;
using System.Text.Json.Nodes;
using Lyntai.Inference;
using Lyntai.Providers.Basic;
using Lyntai.Providers.Http.Payloads;

namespace Lyntai.Providers.Http;

/// <summary>The OpenAI-shaped chat wire: <c>chat/completions</c> under the base URL's <c>/v1</c>
/// convention, SSE streaming with the trailing <c>stream_options</c> usage chunk, tool-call fragments
/// joined by index. The one per-host variation left in the family is Azure's resource conventions
/// (<see cref="HttpModelOptions.AzureConventions"/>) — a URL/auth adjustment, not a different schema.</summary>
internal sealed class OpenAiChatWire(HttpModelOptions config) : IHttpChatWire
{
    private readonly bool _azure = HttpEndpoint.AzureFor(config);

    private readonly JsonObject? _suppressReasoningFields =
        OpenAiPayload.ParseSuppressReasoningFields(config.SuppressReasoningFields);

    public Uri Endpoint => HttpEndpoint.Build(config.BaseUrl, _azure, "chat/completions");

    public string? DefaultModel => config.Model;

    public bool HasCredentials => !string.IsNullOrWhiteSpace(config.ApiKey);

    /// <summary>SSE runs on to its <c>[DONE]</c> sentinel so the trailing usage chunk — sent AFTER the
    /// finish reason, with an EMPTY choices array — is still read.</summary>
    public bool EndsStreamOnFinal => false;

    public void ApplyAuth(HttpRequestMessage request) => HttpEndpoint.ApplyAuth(request, config.ApiKey, _azure);

    public JsonObject BuildPayload(TextRequest req, string model, bool stream) =>
        OpenAiPayload.Build(req, model, stream, _suppressReasoningFields);

    /// <summary>Reads <c>choices[0].message.content</c>, <c>finish_reason</c>, native <c>tool_calls</c> and
    /// <c>usage</c>. A recognized message OR a finish_reason is a well-formed reply, even with empty content
    /// (a content-filtered 200 has exactly that shape) — verdicts are the engine's job.</summary>
    public bool TryExtract(string body, out string text, out TextUsage? usage, out string? finishReason,
        out IReadOnlyList<TextToolCall>? toolCalls)
    {
        text = "";
        usage = null;
        finishReason = null;
        toolCalls = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            JsonElement message = default;
            var found = false;
            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("message", out message)) found = true;
                if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
                    finishReason = fr.GetString();
            }
            if (message.ValueKind == JsonValueKind.Object &&
                message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                text = content.GetString() ?? "";

            if (message.ValueKind == JsonValueKind.Object)
                toolCalls = WireToolCalls.Read(message);

            usage = ExtractUsage(root);
            return (found && message.ValueKind == JsonValueKind.Object) || finishReason is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static TextUsage? ExtractUsage(JsonElement root)
    {
        // WireJson.Long (package-wide) rather than a local read: a token count that is not an integral long
        // (a fractional count from a proxy, an exponent form) must not throw out of an otherwise good reply —
        // nothing here catches a FormatException, so it escaped CompleteAsync and the stream enumerator alike
        if (root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
            return new TextUsage(WireJson.Long(u, "prompt_tokens"), WireJson.Long(u, "completion_tokens"));
        return null;
    }

    /// <summary>One SSE data line → delta text, usage if present, finish reason. A string finish_reason is
    /// the wire's end-of-answer signal, never automatically a benign one — the engine classifies a
    /// <c>content_filter</c> as Refused.</summary>
    public HttpStreamLine ParseStreamLine(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return default;

            // choices[0].delta.content, finish_reason set on the last data line
            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                string? text = null;
                IReadOnlyList<ToolCallDelta>? toolCalls = null;
                if (choice.TryGetProperty("delta", out var delta))
                {
                    if (delta.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                        text = c.GetString();
                    toolCalls = StreamingToolCalls.Read(delta);
                }
                string? finishReason = null;
                if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
                    finishReason = fr.GetString();
                return new HttpStreamLine(text, ExtractUsage(root), finishReason is not null, finishReason, toolCalls);
            }

            // the stream_options usage chunk: the trailing data line AFTER finish_reason carries usage
            // with an EMPTY choices array (the branch above requires a non-empty one) — usage only, not a
            // terminator ([DONE] follows it)
            if (root.TryGetProperty("usage", out var trailing) && trailing.ValueKind == JsonValueKind.Object)
                return new HttpStreamLine(null, ExtractUsage(root), false, null, null);

            return default;
        }
        catch (JsonException)
        {
            return default; // malformed stream line — skip it
        }
    }
}
