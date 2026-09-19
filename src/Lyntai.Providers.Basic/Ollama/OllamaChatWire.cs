using System.Text.Json;
using System.Text.Json.Nodes;
using Lyntai.Inference;
using Lyntai.Providers.Basic;
using Lyntai.Providers.Http;
using Lyntai.Providers.Http.Payloads;
using Microsoft.Extensions.Logging;

namespace Lyntai.Providers.Ollama;

/// <summary>Ollama's native chat wire: <c>/api/chat</c>, NDJSON streaming terminated by <c>done:true</c>,
/// usage as <c>prompt_eval_count</c>/<c>eval_count</c>, tool calls complete on one line (arguments as an
/// OBJECT, no id), images as a sibling base64 array on a user turn.</summary>
/// <param name="config">The registration's options.</param>
/// <param name="logger">Where an attachment this schema cannot carry is REPORTED rather than dropped —
/// its <c>images</c> array is inline base64 only, with no remote-URL form.</param>
internal sealed class OllamaChatWire(OllamaOptions config, ILogger logger) : IHttpChatWire
{
    public Uri Endpoint => new(config.BaseUrl.TrimEnd('/') + "/api/chat");

    public string? DefaultModel => config.Model;

    public bool HasCredentials => !string.IsNullOrWhiteSpace(config.ApiKey);

    /// <summary>NDJSON's <c>done:true</c> line is the last one — usage rides on it, so there is nothing
    /// after it to wait for.</summary>
    public bool EndsStreamOnFinal => true;

    public void ApplyAuth(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(config.ApiKey))
            request.Headers.Authorization = new("Bearer", config.ApiKey);
    }

    public JsonObject BuildPayload(TextRequest req, string model, bool stream) =>
        OllamaPayload.Build(req, model, stream, config.ContextSize, logger);

    /// <summary>Reads the native shape: a top-level <c>message.content</c>, <c>tool_calls</c> with object
    /// arguments and no id, eval-count usage. Ollama sends no <c>finish_reason</c>, so that slot is always
    /// null and the engine's content-filter check simply never fires here.</summary>
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
            if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
                return false;

            if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                text = content.GetString() ?? "";
            toolCalls = WireToolCalls.Read(message);
            usage = ExtractUsage(root);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static TextUsage? ExtractUsage(JsonElement root)
    {
        // WireJson.Long tolerates a count that is not an integral long (a fractional count from a proxy,
        // an exponent form) — nothing here catches a FormatException, so a strict read would throw out of
        // an otherwise good reply
        if (root.TryGetProperty("prompt_eval_count", out _) || root.TryGetProperty("eval_count", out _))
            return new TextUsage(WireJson.Long(root, "prompt_eval_count"), WireJson.Long(root, "eval_count"));
        return null;
    }

    /// <summary>One NDJSON line → delta text, <c>done:true</c> as the final marker (with the eval counts on
    /// that same line). Tool calls arrive COMPLETE on one line, which the shared assembler handles as a
    /// single-fragment accumulation — one path, not a per-wire branch.</summary>
    public HttpStreamLine ParseStreamLine(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return default;
            if (!root.TryGetProperty("message", out var message)) return default;

            string? text = null;
            if (message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                text = c.GetString();
            var final = root.TryGetProperty("done", out var d) && d.ValueKind == JsonValueKind.True;
            return new HttpStreamLine(text, final ? ExtractUsage(root) : null, final, null,
                StreamingToolCalls.Read(message));
        }
        catch (JsonException)
        {
            return default; // malformed stream line — skip it
        }
    }
}
