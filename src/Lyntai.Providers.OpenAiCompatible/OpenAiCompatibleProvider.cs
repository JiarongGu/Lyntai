using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Lyntai.Llm;
using Lyntai.Providers.OpenAiCompatible.Payloads;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Providers.OpenAiCompatible;

/// <summary>
/// HttpClient-based provider for OpenAI-compatible endpoints (OpenAI, Ollama, OpenRouter, …).
/// Maps HTTP status → verdict (429 RateLimited, 5xx Failed, deadline Timeout, content-filter
/// Refused), extracts text tolerantly from either the OpenAI or the Ollama response shape, and
/// retries once on a malformed body (design §6).
/// </summary>
public sealed class OpenAiCompatibleProvider(
    string id,
    OpenAiCompatibleOptions config,
    Func<HttpClient> httpFactory,
    LyntaiOptions options,
    ILogger<OpenAiCompatibleProvider>? logger = null) : ILlmProvider
{
    private readonly ILogger _logger = logger ?? NullLogger<OpenAiCompatibleProvider>.Instance;
    private readonly string _flavor = config.Flavor ?? ProviderDetect.Detect(config.BaseUrl);

    public string Id => id;

    public bool IsAvailable => !string.IsNullOrWhiteSpace(config.BaseUrl);

    public async Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default)
    {
        var model = req.Model ?? config.DefaultModel ?? "";
        using var http = httpFactory();
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage response;
            string body;
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(options.ProviderTimeout);
                response = await http.SendAsync(BuildRequest(req, model, stream: false), timeoutCts.Token).ConfigureAwait(false);
                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        var errorBody = await SafeRead(response, timeoutCts.Token).ConfigureAwait(false);
                        return MapHttpFailure(response.StatusCode, errorBody);
                    }
                    body = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (OperationCanceledException)
            {
                return new LlmReply("", LlmVerdict.Timeout, Detail: $"{id}: no response within {options.ProviderTimeout}");
            }
            catch (HttpRequestException ex)
            {
                return new LlmReply("", LlmVerdict.Failed, Detail: $"{id}: {ex.Message}");
            }

            if (TryExtract(body, out var text, out var usage, out var finishReason))
            {
                if (finishReason == "content_filter")
                    return new LlmReply(text, LlmVerdict.Refused, usage, "content filter");
                return new LlmReply(text, LlmVerdict.Ok, usage);
            }

            if (attempt == 0)
            {
                _logger.LogWarning("{Id}: malformed response body; retrying once", id);
                continue; // one retry on a malformed body
            }
            return new LlmReply("", LlmVerdict.Failed, Detail: $"{id}: malformed response after retry");
        }
    }

    public async IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var model = req.Model ?? config.DefaultModel ?? "";
        using var http = httpFactory();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(options.ProviderTimeout);

        HttpResponseMessage? response = null;
        LlmChunk? startupError = null;
        try
        {
            response = await http.SendAsync(BuildRequest(req, model, stream: true),
                HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await SafeRead(response, timeoutCts.Token).ConfigureAwait(false);
                var mapped = MapHttpFailure(response.StatusCode, errorBody);
                startupError = LlmChunk.Error(mapped.Verdict, mapped.Detail);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            startupError = LlmChunk.Error(LlmVerdict.Timeout, $"{id}: no response within {options.ProviderTimeout}");
        }
        catch (HttpRequestException ex)
        {
            startupError = LlmChunk.Error(LlmVerdict.Failed, $"{id}: {ex.Message}");
        }

        if (startupError is not null)
        {
            response?.Dispose();
            yield return startupError; // pre-content error — the router may fall over
            yield break;
        }

        using var okResponse = response!;
        using var stream = await okResponse.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        LlmUsage? usage = null;
        var done = false;
        while (!done)
        {
            string? line;
            LlmChunk? error = null;
            try
            {
                line = await reader.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                error = LlmChunk.Error(
                    timeoutCts.IsCancellationRequested ? LlmVerdict.Timeout : LlmVerdict.Failed,
                    $"{id}: stream broke — {ex.Message}");
                line = null;
            }
            if (error is not null)
            {
                yield return error;
                yield break;
            }
            if (line is null) break;
            if (line.Length == 0) continue;

            // SSE ("data: {...}" / "data: [DONE]") for OpenAI-style; bare NDJSON for Ollama
            var payload = line.StartsWith("data:", StringComparison.Ordinal) ? line[5..].Trim() : line.Trim();
            if (payload.Length == 0) continue;
            if (payload == "[DONE]") { done = true; continue; }

            var (text, chunkUsage, isFinal) = ParseStreamLine(payload);
            if (chunkUsage is not null) usage = chunkUsage;
            if (text is { Length: > 0 }) yield return LlmChunk.Content(text);
            if (isFinal) done = true;
        }

        yield return LlmChunk.Final(usage);
    }

    private HttpRequestMessage BuildRequest(LlmRequest req, string model, bool stream)
    {
        var payload = _flavor == ProviderDetect.Ollama
            ? OllamaPayload.Build(req, model, stream, config.NumCtx)
            : OpenAiPayload.Build(req, model, stream);

        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint())
        {
            Content = new StringContent(payload.ToJsonString(), new UTF8Encoding(false), "application/json"),
        };
        if (!string.IsNullOrEmpty(config.ApiKey))
            request.Headers.Authorization = new("Bearer", config.ApiKey);
        return request;
    }

    internal Uri Endpoint()
    {
        var baseUrl = config.BaseUrl.TrimEnd('/');
        var path = _flavor == ProviderDetect.Ollama
            ? "/api/chat"
            : baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                ? "/chat/completions"
                : "/v1/chat/completions";
        return new Uri(baseUrl + path);
    }

    private LlmReply MapHttpFailure(HttpStatusCode status, string body)
    {
        var detail = $"{id}: HTTP {(int)status} {Tail(body)}";
        return status switch
        {
            HttpStatusCode.TooManyRequests => new LlmReply("", LlmVerdict.RateLimited, Detail: detail),
            _ when body.Contains("content_filter", StringComparison.OrdinalIgnoreCase) ||
                   body.Contains("content_policy", StringComparison.OrdinalIgnoreCase)
                => new LlmReply("", LlmVerdict.Refused, Detail: detail),
            _ => new LlmReply("", LlmVerdict.Failed, Detail: detail),
        };
    }

    /// <summary>Tolerant extraction covering both response shapes:
    /// OpenAI <c>choices[0].message.content</c> and Ollama <c>message.content</c>.</summary>
    internal static bool TryExtract(string body, out string text, out LlmUsage? usage, out string? finishReason)
    {
        text = "";
        usage = null;
        finishReason = null;
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
            else if (root.TryGetProperty("message", out message))
            {
                found = true; // Ollama shape
            }
            if (!found || message.ValueKind != JsonValueKind.Object) return false;

            if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                text = content.GetString() ?? "";

            usage = ExtractUsage(root);
            return text.Length > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static LlmUsage? ExtractUsage(JsonElement root)
    {
        if (root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
            return new LlmUsage(GetLong(u, "prompt_tokens"), GetLong(u, "completion_tokens"));
        if (root.TryGetProperty("prompt_eval_count", out _) || root.TryGetProperty("eval_count", out _))
            return new LlmUsage(GetLong(root, "prompt_eval_count"), GetLong(root, "eval_count"));
        return null;
    }

    /// <summary>One streaming line → (delta text, usage if present, is-final).</summary>
    internal static (string? Text, LlmUsage? Usage, bool IsFinal) ParseStreamLine(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null, false);

            // OpenAI SSE: choices[0].delta.content, finish_reason set on the last data line
            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                string? text = null;
                if (choice.TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                    text = c.GetString();
                var final = choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String;
                return (text, ExtractUsage(root), final);
            }

            // Ollama NDJSON: message.content per line, done:true on the last (with eval counts)
            if (root.TryGetProperty("message", out var message))
            {
                string? text = null;
                if (message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                    text = c.GetString();
                var final = root.TryGetProperty("done", out var d) && d.ValueKind == JsonValueKind.True;
                return (text, final ? ExtractUsage(root) : null, final);
            }
            return (null, null, false);
        }
        catch (JsonException)
        {
            return (null, null, false); // malformed stream line — skip it
        }
    }

    private static async Task<string> SafeRead(HttpResponseMessage response, CancellationToken ct)
    {
        try { return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false); }
        catch { return ""; }
    }

    private static long GetLong(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetInt64() : 0;

    private static string Tail(string text, int max = 300)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
