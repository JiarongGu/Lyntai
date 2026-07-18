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
    ILogger<OpenAiCompatibleProvider>? logger = null,
    bool disposeHttpClient = true) : ILlmProvider
{
    private readonly ILogger _logger = logger ?? NullLogger<OpenAiCompatibleProvider>.Instance;
    private readonly string _flavor = config.Flavor ?? ProviderDetect.Detect(config.BaseUrl);

    public string Id => id;

    public bool IsAvailable => !string.IsNullOrWhiteSpace(config.BaseUrl);

    // OpenAI-compatible endpoints support native function-calling: we send req.Tools and surface the
    // model's tool_calls on the reply. Coarse — an Ollama MODEL that ignores tools just answers in prose.
    public bool SupportsToolCalls => true;

    /// <summary>Get the per-call HttpClient. Lyntai-created clients (from the named IHttpClientFactory
    /// client) are disposed after each call; an APP-supplied (BYO) client is NEVER disposed — the app
    /// owns its lifetime, so disposing it would break every call after the first.</summary>
    private HttpClient? OwnedClient() => disposeHttpClient ? httpFactory() : null;

    public async Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default)
    {
        var model = req.Model ?? config.DefaultModel ?? "";
        var timeout = options.ResolveTimeout(req);
        using var owned = OwnedClient();       // disposed only when Lyntai owns it
        var http = owned ?? httpFactory();     // BYO client: fetched, not disposed
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage response;
            string body;
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(timeout);
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
                return new LlmReply("", LlmVerdict.Timeout, Detail: $"{id}: no response within {timeout}");
            }
            catch (HttpRequestException ex)
            {
                return new LlmReply("", LlmVerdict.Failed, Detail: $"{id}: {ex.Message}");
            }

            if (TryExtract(body, out var text, out var usage, out var finishReason, out var toolCalls))
            {
                // a content filter often arrives as HTTP 200 + finish_reason with EMPTY content —
                // it must classify as Refused (no fallback) before any empty-text handling
                if (finishReason == "content_filter")
                    return new LlmReply(text, LlmVerdict.Refused, usage, $"{id}: content filter");
                // a tool-call turn is a SUCCESSFUL reply with empty text — surface it before the
                // empty-text→Failed/retry path (the tool loop drives the next turn)
                if (toolCalls is { Count: > 0 })
                    return new LlmReply(text, LlmVerdict.Ok, usage) { ToolCalls = toolCalls };
                if (text.Length > 0)
                    return new LlmReply(text, LlmVerdict.Ok, usage);
                // well-formed but empty and not filtered — same retry-once as a malformed body
            }

            if (attempt == 0)
            {
                _logger.LogWarning("{Id}: malformed or empty response body; retrying once", id);
                continue; // one retry on a malformed/empty body
            }
            return new LlmReply("", LlmVerdict.Failed, Detail: $"{id}: malformed or empty response after retry");
        }
    }

    public async IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var model = req.Model ?? config.DefaultModel ?? "";
        var timeout = options.ResolveTimeout(req);
        using var owned = OwnedClient();       // disposed only when Lyntai owns it
        var http = owned ?? httpFactory();     // BYO client: fetched, not disposed

        // the timeout is an INACTIVITY clock: it covers the connect and each line read, but is stopped
        // while we and the consumer process a line — a slow-but-healthy stream isn't killed under a
        // slow reader. Re-armed per read below (mirrors ProcessRunner.StreamLinesAsync).
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout); // arm for the connect (ResponseHeadersRead)

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
            startupError = LlmChunk.Error(LlmVerdict.Timeout, $"{id}: no response within {timeout}");
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
        string? finishReason = null;
        var sawContent = false;
        var done = false;
        while (!done)
        {
            string? line;
            LlmChunk? error = null;
            try
            {
                timeoutCts.CancelAfter(timeout);                           // arm: inactivity clock for this read
                line = await reader.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
                timeoutCts.CancelAfter(Timeout.InfiniteTimeSpan);          // stop the clock while we + the consumer work
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

            var (text, chunkUsage, isFinal, reason) = ParseStreamLine(payload);
            if (chunkUsage is not null) usage = chunkUsage;
            if (reason is not null) finishReason = reason;
            if (text is { Length: > 0 })
            {
                sawContent = true;
                yield return LlmChunk.Content(text);
            }
            if (isFinal) done = true;
        }

        // a streamed content filter must end as Refused, not a benign Final — same verdict the
        // non-streaming path gives the identical finish_reason
        if (finishReason == "content_filter")
        {
            yield return LlmChunk.Error(LlmVerdict.Refused, $"{id}: content filter");
            yield break;
        }
        // the streaming contract (LlmChunk) carries no tool-call payload — streaming native tool-calls is
        // deferred. A model that STREAMED a tool call with NO content (finish_reason=tool_calls) is NOT a
        // host failure: surface Refused (no fallback/cooldown) pointing the caller at CompleteAsync, rather
        // than the empty→Failed below which would penalize a healthy host. But if content ALSO streamed,
        // don't clobber it — fall through to a benign Final (the tool call is dropped, not the answer).
        if (finishReason == "tool_calls" && !sawContent)
        {
            yield return LlmChunk.Error(LlmVerdict.Refused,
                $"{id}: streaming does not deliver native tool calls — use CompleteAsync for tool-calling");
            yield break;
        }
        // zero-content stream = the streaming twin of CompleteAsync's empty→Failed, so the router
        // can fall over pre-content instead of reporting a clean empty answer
        if (!sawContent)
        {
            yield return LlmChunk.Error(LlmVerdict.Failed, $"{id}: no output produced");
            yield break;
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
        // typed status wins; body text goes through the ONE shared classifier (never local heuristics)
        return new LlmReply("", LlmVerdictClassifier.FromHttpFailure(status, body), Detail: detail);
    }

    /// <summary>Tolerant extraction covering both response shapes:
    /// OpenAI <c>choices[0].message.content</c> and Ollama <c>message.content</c>. Also surfaces native
    /// <c>tool_calls</c> when present (id/function.name/function.arguments — arguments as a string on
    /// OpenAI, an object on Ollama, both normalized to a JSON string).</summary>
    internal static bool TryExtract(string body, out string text, out LlmUsage? usage, out string? finishReason,
        out IReadOnlyList<LlmToolCall>? toolCalls)
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
            else if (root.TryGetProperty("message", out message))
            {
                found = true; // Ollama shape
            }
            if (message.ValueKind == JsonValueKind.Object &&
                message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                text = content.GetString() ?? "";

            if (message.ValueKind == JsonValueKind.Object)
                toolCalls = ExtractToolCalls(message);

            usage = ExtractUsage(root);
            // a recognized message OR a finish_reason is a well-formed reply, even with empty
            // content (a content-filtered 200 has exactly that shape) — verdicts are the caller's job
            return (found && message.ValueKind == JsonValueKind.Object) || finishReason is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Parse <c>message.tool_calls</c> (both dialects). OpenAI carries an id and string
    /// arguments; Ollama carries no id (synthesize one) and object arguments (serialize to a string).</summary>
    private static IReadOnlyList<LlmToolCall>? ExtractToolCalls(JsonElement message)
    {
        if (!message.TryGetProperty("tool_calls", out var calls) || calls.ValueKind != JsonValueKind.Array || calls.GetArrayLength() == 0)
            return null;

        var result = new List<LlmToolCall>();
        var index = 0;
        foreach (var call in calls.EnumerateArray())
        {
            if (call.ValueKind != JsonValueKind.Object ||
                !call.TryGetProperty("function", out var fn) || fn.ValueKind != JsonValueKind.Object ||
                !fn.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
            {
                index++;
                continue; // not a function tool call we can act on — skip
            }
            var id = call.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString()! : $"call_{index}"; // Ollama gives no id — synthesize a stable one
            var args = "{}";
            if (fn.TryGetProperty("arguments", out var argEl))
                args = argEl.ValueKind switch
                {
                    JsonValueKind.String => argEl.GetString() is { Length: > 0 } s ? s : "{}", // OpenAI: already a JSON string
                    JsonValueKind.Object => argEl.GetRawText(),                                // Ollama: an object → its JSON text
                    _ => "{}",
                };
            result.Add(new LlmToolCall(id, nameEl.GetString()!, args));
            index++;
        }
        return result.Count > 0 ? result : null;
    }

    private static LlmUsage? ExtractUsage(JsonElement root)
    {
        if (root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
            return new LlmUsage(GetLong(u, "prompt_tokens"), GetLong(u, "completion_tokens"));
        if (root.TryGetProperty("prompt_eval_count", out _) || root.TryGetProperty("eval_count", out _))
            return new LlmUsage(GetLong(root, "prompt_eval_count"), GetLong(root, "eval_count"));
        return null;
    }

    /// <summary>One streaming line → (delta text, usage if present, is-final, finish reason). The
    /// finish reason travels out so the stream can classify a content_filter as Refused — a string
    /// finish_reason is a stream terminator, never automatically a benign one.</summary>
    internal static (string? Text, LlmUsage? Usage, bool IsFinal, string? FinishReason) ParseStreamLine(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null, false, null);

            // OpenAI SSE: choices[0].delta.content, finish_reason set on the last data line
            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                string? text = null;
                if (choice.TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                    text = c.GetString();
                string? finishReason = null;
                if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
                    finishReason = fr.GetString();
                return (text, ExtractUsage(root), finishReason is not null, finishReason);
            }

            // Ollama NDJSON: message.content per line, done:true on the last (with eval counts)
            if (root.TryGetProperty("message", out var message))
            {
                string? text = null;
                if (message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                    text = c.GetString();
                var final = root.TryGetProperty("done", out var d) && d.ValueKind == JsonValueKind.True;
                return (text, final ? ExtractUsage(root) : null, final, null);
            }
            return (null, null, false, null);
        }
        catch (JsonException)
        {
            return (null, null, false, null); // malformed stream line — skip it
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
