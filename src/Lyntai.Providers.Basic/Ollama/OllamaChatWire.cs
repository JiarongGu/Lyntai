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
            if (WireJson.Object(doc.RootElement, "message") is not { } message) return false;

            text = WireJson.String(message, "content") ?? "";
            toolCalls = WireToolCalls.Read(message);
            usage = ExtractUsage(doc.RootElement);
            return true;
        }
        catch (Exception ex) when (WireJson.IsShapeFault(ex))
        {
            return false;
        }
    }

    private static TextUsage? ExtractUsage(JsonElement root) =>
        root.TryGetProperty("prompt_eval_count", out _) || root.TryGetProperty("eval_count", out _)
            ? new TextUsage(WireJson.Long(root, "prompt_eval_count"), WireJson.Long(root, "eval_count"))
            : null;

    /// <summary>One NDJSON line → delta text, <c>done:true</c> as the final marker (with the eval counts on
    /// that same line). Each tool call arrives COMPLETE, one per line (measured, Ollama 0.34.2), with its
    /// <c>index</c> inside <c>function</c> — so every call is read as its own slot rather than joined by an
    /// index, and calls on separate lines can never merge.</summary>
    public HttpStreamLine ParseStreamLine(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (WireJson.Object(root, "message") is not { } message) return default;

            var final = root.TryGetProperty("done", out var d) && d.ValueKind == JsonValueKind.True;
            return new HttpStreamLine(WireJson.String(message, "content"), final ? ExtractUsage(root) : null,
                final, null, StreamingToolCalls.Read(message, complete: true));
        }
        catch (Exception ex) when (WireJson.IsShapeFault(ex))
        {
            return default; // malformed stream line — skip it
        }
    }
}
