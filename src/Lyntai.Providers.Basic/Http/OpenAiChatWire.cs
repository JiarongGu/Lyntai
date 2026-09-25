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

    public string? ApiKey => config.ApiKey;

    public bool AzureConventions => _azure;

    /// <summary>SSE runs on to its <c>[DONE]</c> sentinel so the trailing usage chunk — sent AFTER the
    /// finish reason, with an EMPTY choices array — is still read.</summary>
    public bool EndsStreamOnFinal => false;

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

            JsonElement? message = null;
            if (FirstChoice(root) is { } choice)
            {
                message = WireJson.Object(choice, "message");
                finishReason = WireJson.String(choice, "finish_reason");
            }
            if (message is { } m)
            {
                text = WireJson.String(m, "content") ?? "";
                toolCalls = WireToolCalls.Read(m);
            }

            usage = ExtractUsage(root);
            return message is not null || finishReason is not null;
        }
        catch (Exception ex) when (WireJson.IsShapeFault(ex))
        {
            return false;
        }
    }

    /// <summary><c>choices[0]</c>, or null when there is none or it is not an object.</summary>
    private static JsonElement? FirstChoice(JsonElement root) =>
        WireJson.Array(root, "choices") is { } choices && choices.GetArrayLength() > 0 &&
        choices[0].ValueKind == JsonValueKind.Object
            ? choices[0]
            : null;

    private static TextUsage? ExtractUsage(JsonElement root) =>
        WireJson.Object(root, "usage") is { } u
            ? new TextUsage(WireJson.Long(u, "prompt_tokens"), WireJson.Long(u, "completion_tokens"))
            : null;

    /// <summary>One SSE data line → delta text, usage if present, finish reason. A string finish_reason is
    /// the wire's end-of-answer signal, never automatically a benign one — the engine classifies a
    /// <c>content_filter</c> as Refused.</summary>
    public HttpStreamLine ParseStreamLine(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var inBand = HttpBody.InBandError(root);

            // choices[0].delta.content, finish_reason set on the last data line
            if (FirstChoice(root) is { } choice)
            {
                string? text = null;
                IReadOnlyList<ToolCallDelta>? toolCalls = null;
                if (WireJson.Object(choice, "delta") is { } delta)
                {
                    text = WireJson.String(delta, "content");
                    toolCalls = StreamingToolCalls.Read(delta);
                }
                var finishReason = WireJson.String(choice, "finish_reason");
                return new HttpStreamLine(text, ExtractUsage(root), finishReason is not null, finishReason, toolCalls)
                    { InBandError = inBand };
            }

            // the stream_options usage chunk: the trailing data line AFTER finish_reason carries usage
            // with an EMPTY choices array (the branch above requires a non-empty one) — usage only, not a
            // terminator ([DONE] follows it)
            if (ExtractUsage(root) is { } trailing)
                return new HttpStreamLine(null, trailing, false, null, null) { InBandError = inBand };

            return new HttpStreamLine { InBandError = inBand };
        }
        catch (Exception ex) when (WireJson.IsShapeFault(ex))
        {
            return default; // malformed stream line — skip it
        }
    }
}
