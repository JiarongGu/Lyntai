using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Lyntai.Llm;
using Lyntai.Llm.Streaming;
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
    private readonly OpenAiFlavor _flavor = OpenAiEndpoint.ResolveFlavor(config.Flavor, config.BaseUrl);

    public string Id => id;

    public bool IsAvailable => !string.IsNullOrWhiteSpace(config.BaseUrl);

    // OpenAI-compatible endpoints support native function-calling: we send req.Tools and surface the
    // model's tool_calls on the reply. Coarse — an Ollama MODEL that ignores tools just answers in prose.
    public bool SupportsToolCalls => true;

    /// <inheritdoc/>
    /// <remarks>True since 3.0: the stream assembles a vendor's tool-call fragments and yields complete
    /// calls as <see cref="LlmChunkKind.ToolCall"/> chunks. Both dialects — OpenAI SSE, which fragments
    /// arguments across lines, and Ollama NDJSON, which sends them complete — go through the same
    /// accumulator.</remarks>
    public bool SupportsStreamingToolCalls => true;

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
                        var errorBody = await OpenAiHttp.SafeRead(response, timeoutCts.Token).ConfigureAwait(false);
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

            // The backend ANSWERED, at HTTP 200, in the other channel. Classified before the retry on
            // purpose: re-sending to a host that just reported a rate limit is the harm, not the symptom.
            if (OpenAiHttp.InBandError(body) is { } inBand) return InBandFailure(inBand);

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
                var errorBody = await OpenAiHttp.SafeRead(response, timeoutCts.Token).ConfigureAwait(false);
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
        // Tool-call fragments, folded per vendor slot as they arrive and assembled once at the end. Held
        // across the whole loop because a single call's arguments span many lines.
        var toolCalls = new StreamingToolCalls();
        // The last error the backend reported IN BAND. Remembered rather than acted on: an error line that
        // arrives AFTER content is not terminal — codex's measured `{"type":"error","message":"Reconnecting
        // ... 2/5"}` appeared in runs that went on to SUCCEED, and treating every error-ish line as fatal
        // kills healthy calls that recovered (pitfalls.md, the mirror of the trap this fix closes). So it is
        // consulted only on the zero-content path below, where the alternative is a reasonless "no output".
        string? inBandError = null;
        // the guarded loop (arm/read/stop + caller-cancel rethrow + fault→terminal) lives once in Core
        var guarded = GuardedStream.ReadAll<string, LlmChunk>(
            async () => await reader.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false),
            ex => LlmChunk.Error(
                timeoutCts.IsCancellationRequested ? LlmVerdict.Timeout : LlmVerdict.Failed,
                $"{id}: stream broke — {ex.Message}"),
            ct, new InactivityClock(timeoutCts, timeout));
        await foreach (var (line, terminal) in guarded.ConfigureAwait(false))
        {
            if (terminal is not null)
            {
                yield return terminal;
                yield break;
            }
            if (line!.Length == 0) continue;

            // SSE ("data: {...}" / "data: [DONE]") for OpenAI-style; bare NDJSON for Ollama
            var payload = line.StartsWith("data:", StringComparison.Ordinal) ? line[5..].Trim() : line.Trim();
            if (payload.Length == 0) continue;
            if (payload == "[DONE]") break;

            var (text, chunkUsage, isFinal, reason, toolCallDeltas) = ParseStreamLine(payload);
            if (chunkUsage is not null) usage = chunkUsage;
            if (reason is not null) finishReason = reason;
            if (toolCallDeltas is not null) toolCalls.Add(toolCallDeltas);
            if (OpenAiHttp.InBandError(payload) is { } streamed) inBandError = streamed;
            if (text is { Length: > 0 })
            {
                sawContent = true;
                yield return LlmChunk.Content(text);
            }
            // finish_reason terminates an NDJSON (Ollama) stream. An SSE stream instead runs on to its
            // [DONE] sentinel (or EOF) so the trailing stream_options usage chunk — sent AFTER the
            // finish_reason line, with an EMPTY choices array — is still read into `usage`.
            if (isFinal && _flavor == OpenAiFlavor.Ollama) break;
        }

        // a streamed content filter must end as Refused, not a benign Final — same verdict the
        // non-streaming path gives the identical finish_reason
        if (finishReason == "content_filter")
        {
            yield return LlmChunk.Error(LlmVerdict.Refused, $"{id}: content filter");
            yield break;
        }
        // TOOL CALLS, assembled from their fragments and delivered before the terminal chunk (3.0). Until
        // then LlmChunk had no tool-call payload, and the two shapes this replaces were both wrong: a
        // tool-call-only turn reported Unsupported and told the caller to use CompleteAsync, while a turn
        // that streamed prose ALONGSIDE a call fell through to a benign Final and SILENTLY DROPPED the call.
        // The second is why this is a fix and not only a feature.
        //
        // They are yielded here rather than as they arrive because a vendor sends arguments in pieces and
        // LlmChunk.ToolCall promises a COMPLETE call — the assembly is the provider's job so no consumer has
        // to know a vendor's fragmentation rules. `finish_reason` is not required: Ollama sends none, and a
        // stream that produced complete calls has produced them whatever it says about why it stopped.
        var assembled = toolCalls.Any ? toolCalls.Build() : [];
        foreach (var call in assembled) yield return LlmChunk.Tool(call);
        if (assembled.Count > 0)
        {
            yield return LlmChunk.Final(usage);
            yield break;
        }

        // A stream that said it stopped FOR tool calls and assembled none is a real failure rather than an
        // empty answer — the model asked for something this build could not read, and saying so beats the
        // synthetic "no output produced" below.
        if (finishReason == "tool_calls" && !sawContent)
        {
            yield return LlmChunk.Error(LlmVerdict.Failed,
                $"{id}: the stream finished for tool calls but none could be assembled from its deltas");
            yield break;
        }
        // zero-content stream = the streaming twin of CompleteAsync's empty→Failed, so the router
        // can fall over pre-content instead of reporting a clean empty answer. When the backend said WHY in
        // band, that answer outranks the synthetic one: "no output produced" is the right verdict CLASS with
        // no reason and the wrong routing (Failed advances and strikes; RateLimited cools).
        if (!sawContent)
        {
            var reply = inBandError is not null
                ? InBandFailure(inBandError)
                : new LlmReply("", LlmVerdict.Failed, Detail: $"{id}: no output produced");
            yield return LlmChunk.Error(reply.Verdict, reply.Detail);
            yield break;
        }
        yield return LlmChunk.Final(usage);
    }

    private HttpRequestMessage BuildRequest(LlmRequest req, string model, bool stream)
    {
        // the logger travels into the Ollama payload so an attachment /api/chat cannot carry is reported
        // rather than dropped in silence (its images array is inline base64 only — no remote URL form)
        var payload = _flavor == OpenAiFlavor.Ollama
            ? OllamaPayload.Build(req, model, stream, config.OllamaContextSize, _logger)
            : OpenAiPayload.Build(req, model, stream);

        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint())
        {
            Content = new StringContent(payload.ToJsonString(), new UTF8Encoding(false), "application/json"),
        };
        OpenAiEndpoint.ApplyAuth(request, config.ApiKey, _flavor);
        return request;
    }

    /// <summary>The chat endpoint — Ollama's native <c>/api/chat</c>, otherwise the OpenAI-compatible
    /// <c>chat/completions</c> route.</summary>
    private Uri Endpoint() =>
        OpenAiEndpoint.Build(config.BaseUrl, _flavor, ollamaNativePath: "/api/chat", openAiRoute: "chat/completions");

    /// <summary>Whether this provider has anything to authenticate WITH — what separates "not set up yet"
    /// from "your key was rejected" when the server answers 401/403.</summary>
    private bool HasCredentials => !string.IsNullOrWhiteSpace(config.ApiKey);

    /// <summary>Classify an error the backend reported IN BAND under a 2xx status. The status carries no
    /// information here — it said success — so the verdict comes from the backend's own words through the
    /// ONE shared corpus, never a local heuristic.
    /// <para>The <see cref="LlmVerdict.AuthFailed"/> → <see cref="LlmVerdict.NotConfigured"/> promotion is
    /// the same two-term rule <see cref="LlmVerdictClassifier.FromHttpFailure(HttpStatusCode, string, bool)"/>
    /// applies on the status path, restated here because that overload needs a FAILED status to key on and
    /// this path has none. Keeping the two in step matters: NotConfigured skips a candidate blamelessly and
    /// lets a host offer setup, while AuthFailed benches it for the cooldown window.</para></summary>
    private LlmReply InBandFailure(string error)
    {
        var verdict = LlmVerdictClassifier.FromErrorText(error);
        if (verdict == LlmVerdict.AuthFailed && !HasCredentials) verdict = LlmVerdict.NotConfigured;
        return new LlmReply("", verdict, Detail: $"{id}: {OpenAiHttp.Head(error)}");
    }

    private LlmReply MapHttpFailure(HttpStatusCode status, string body)
    {
        var detail = $"{id}: HTTP {(int)status} {OpenAiHttp.Head(body)}";
        // typed status wins; body text goes through the ONE shared classifier (never local heuristics).
        // hasCredentials separates "never set up" (NotConfigured — skipped blamelessly) from "your key was
        // rejected" (AuthFailed — benched for the cooldown window). A local OpenAI-compatible server needs no
        // key, so the missing key only means unconfigured once the server has actually demanded one.
        return new LlmReply("", LlmVerdictClassifier.FromHttpFailure(status, body, HasCredentials), Detail: detail);
    }

    /// <summary>Tolerant extraction covering both response shapes:
    /// OpenAI <c>choices[0].message.content</c> and Ollama <c>message.content</c>. Also surfaces native
    /// <c>tool_calls</c> when present (id/function.name/function.arguments — arguments as a string on
    /// OpenAI, an object on Ollama, both normalized to a JSON string).</summary>
    private static bool TryExtract(string body, out string text, out LlmUsage? usage, out string? finishReason,
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
        // WireJson.Long (package-wide) rather than a local read: a token count that is not an integral long
        // (a fractional count from a proxy, an exponent form) must not throw out of an otherwise good reply —
        // nothing here catches a FormatException, so it escaped CompleteAsync and the stream enumerator alike
        if (root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
            return new LlmUsage(WireJson.Long(u, "prompt_tokens"), WireJson.Long(u, "completion_tokens"));
        if (root.TryGetProperty("prompt_eval_count", out _) || root.TryGetProperty("eval_count", out _))
            return new LlmUsage(WireJson.Long(root, "prompt_eval_count"), WireJson.Long(root, "eval_count"));
        return null;
    }

    /// <summary>One streaming line → (delta text, usage if present, is-final, finish reason). The
    /// finish reason travels out so the stream can classify a content_filter as Refused — a string
    /// finish_reason is a stream terminator, never automatically a benign one.</summary>
    private static (string? Text, LlmUsage? Usage, bool IsFinal, string? FinishReason,
        IReadOnlyList<ToolCallDelta>? ToolCalls) ParseStreamLine(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null, false, null, null);

            // OpenAI SSE: choices[0].delta.content, finish_reason set on the last data line
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
                return (text, ExtractUsage(root), finishReason is not null, finishReason, toolCalls);
            }

            // OpenAI stream_options usage chunk: the trailing data line AFTER finish_reason carries usage
            // with an EMPTY choices array (the branch above requires a non-empty one) — usage only, not a
            // terminator ([DONE] follows it)
            if (root.TryGetProperty("usage", out var trailing) && trailing.ValueKind == JsonValueKind.Object)
                return (null, ExtractUsage(root), false, null, null);

            // Ollama NDJSON: message.content per line, done:true on the last (with eval counts). Its tool
            // calls arrive COMPLETE on one line rather than fragmented, which the assembler handles as a
            // single-fragment accumulation — one path, not a dialect branch.
            if (root.TryGetProperty("message", out var message))
            {
                string? text = null;
                if (message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                    text = c.GetString();
                var final = root.TryGetProperty("done", out var d) && d.ValueKind == JsonValueKind.True;
                return (text, final ? ExtractUsage(root) : null, final, null, StreamingToolCalls.Read(message));
            }
            return (null, null, false, null, null);
        }
        catch (JsonException)
        {
            return (null, null, false, null, null); // malformed stream line — skip it
        }
    }
}
