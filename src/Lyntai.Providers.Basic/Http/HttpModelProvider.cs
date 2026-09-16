using Lyntai.Lifecycle;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Lyntai.Llm;
using Lyntai.Llm.Streaming;
using Lyntai.Providers.Http.Payloads;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Providers.Http;

/// <summary>A model served over HTTP, in one of several wire DIALECTS —
/// <see cref="HttpModelOptions.Dialect"/> picks the routes and the payload shape.
///
/// <para><b>It is not named for a vendor because it is not one.</b> Three dialects post the OpenAI schema to
/// <c>/v1/…</c> and are fairly called OpenAI-compatible; <see cref="HttpDialect.Ollama"/> posts Ollama's own
/// <c>/api/chat</c> and <c>/api/embed</c>, which by its own documentation are NOT. A name claiming otherwise
/// was false for that dialect and misleading for the rest (<c>docs/DECISIONS.md</c> D135). It is the same
/// shape the CLI side already has: one engine, a dialect per backend.</para>
///
/// <para>Maps HTTP status → verdict (429 RateLimited, 5xx Failed, deadline Timeout, content-filter
/// Refused), extracts text tolerantly from either response shape, and retries once on a malformed body
/// (design §6).</para></summary>
public sealed class HttpModelProvider(
    string id,
    HttpModelOptions config,
    Func<HttpClient> httpFactory,
    LyntaiOptions options,
    ILogger<HttpModelProvider>? logger = null,
    bool disposeHttpClient = true) : IModelProvider
{
    private readonly ILogger _logger = logger ?? NullLogger<HttpModelProvider>.Instance;
    private readonly HttpDialect _dialect = HttpEndpoint.ResolveDialect(config.Dialect, config.BaseUrl);

    public string Id => id;

    /// <summary>What this backend serves, DERIVED from <see cref="HttpModelOptions.Produces"/>: text
    /// is buffered or streamed with native tool calls on both paths; vector is one batched call, since there
    /// is no such thing as a partially delivered embedding.
    /// <para>Models are NOT enumerated — an aggregator fronts hundreds behind one id, which is exactly the
    /// case an empty Models list means "any" for.</para></summary>
    public ProviderCapabilities Capabilities { get; } = new()
    {
        Accepts = [ProviderKinds.Text],
        Produces = [config.Produces],
        Operations = ServesText(config)
            ? [ProviderOperation.Complete, ProviderOperation.Stream]
            : [ProviderOperation.Complete],
        SupportsToolCalls = ServesText(config),
        SupportsStreamingToolCalls = ServesText(config),
    };

    /// <summary>Which kind this registration serves — one field decides the route, the wire shape, and which
    /// methods answer. Only text streams: there is no partially delivered embedding and no partial ranking,
    /// so those two serve <see cref="ProviderOperation.Complete"/> alone.</summary>
    private static bool Serves(HttpModelOptions c, string kind) =>
        string.Equals(c.Produces, kind, StringComparison.OrdinalIgnoreCase);

    private static bool ServesText(HttpModelOptions c) => Serves(c, ProviderKinds.Text);
    private static bool ServesVectors(HttpModelOptions c) => Serves(c, ProviderKinds.Vector);
    private static bool ServesScores(HttpModelOptions c) => Serves(c, ProviderKinds.Score);

    /// <summary>The <c>/embeddings</c> wire shape, or null when this backend produces something else. It is
    /// composed rather than inherited: an embeddings call has nothing in common with a completion beyond the
    /// host it is posted to.</summary>
    private readonly HttpEmbeddingsTransport? _embeddings = !ServesVectors(config) ? null
        : new HttpEmbeddingsTransport(id, config, httpFactory, options, logger, disposeHttpClient);

    /// <summary>The <c>/v1/rerank</c> wire shape, or null when this backend produces something else.</summary>
    private readonly HttpRerankTransport? _rerank = !ServesScores(config) ? null
        : new HttpRerankTransport(id, config, httpFactory, options, logger, disposeHttpClient);

    public bool IsAvailable => !string.IsNullOrWhiteSpace(config.BaseUrl);

    // OpenAI-compatible endpoints support native function-calling: we send req.Tools and surface the
    // model's tool_calls on the reply. Coarse — an Ollama MODEL that ignores tools just answers in prose.

    /// <inheritdoc/>
    /// <remarks>True since 3.0: the stream assembles a vendor's tool-call fragments and yields complete
    /// calls as <see cref="LlmChunkKind.ToolCall"/> chunks. Both dialects — OpenAI SSE, which fragments
    /// arguments across lines, and Ollama NDJSON, which sends them complete — go through the same
    /// accumulator.</remarks>

    /// <summary>Get the per-call HttpClient. Lyntai-created clients (from the named IHttpClientFactory
    /// client) are disposed after each call; an APP-supplied (BYO) client is NEVER disposed — the app
    /// owns its lifetime, so disposing it would break every call after the first.</summary>
    private HttpClient? OwnedClient() => disposeHttpClient ? httpFactory() : null;

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">This registration does not produce vectors.</exception>
    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
        Vectors().EmbedAsync(texts, ct);

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">This registration does not produce vectors.</exception>
    public Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts, EmbeddingRole role, CancellationToken ct = default) =>
        Vectors().EmbedAsync(texts, role, ct);

    /// <summary>The embeddings transport, or the refusal <see cref="IModelProvider"/>'s own default gives.
    /// Reached only by a caller that ignored <see cref="Capabilities"/>, since a router checks first.</summary>
    private HttpEmbeddingsTransport Vectors() => _embeddings
        ?? throw new NotSupportedException(
            $"{id} produces {config.Produces}, not {ProviderKinds.Vector} — an embedding model is its own "
            + "backend, registered with Produces = ProviderKinds.Vector.");


    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">This registration does not produce scores.</exception>
    public Task<IReadOnlyList<double>> ScoreAsync(
        string query, IReadOnlyList<string> documents, CancellationToken ct = default) =>
        (_rerank ?? throw new NotSupportedException(
            $"{id} produces {config.Produces}, not {ProviderKinds.Score} — a reranker is its own backend, "
            + "registered with Produces = ProviderKinds.Score."))
        .ScoreAsync(query, documents, ct);

    public async Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default)
    {
        var model = req.Model ?? config.Model ?? "";
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
                        var errorBody = await HttpBody.SafeRead(response, timeoutCts.Token).ConfigureAwait(false);
                        return MapHttpFailure(response.StatusCode, errorBody);
                    }
                    body = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (OperationCanceledException)
            {
                return new LlmReply("", ProviderVerdict.Timeout, Detail: $"{id}: no response within {timeout}");
            }
            catch (HttpRequestException ex)
            {
                return new LlmReply("", ProviderVerdict.Failed, Detail: $"{id}: {ex.Message}");
            }

            if (TryExtract(body, out var text, out var usage, out var finishReason, out var toolCalls))
            {
                // a content filter often arrives as HTTP 200 + finish_reason with EMPTY content —
                // it must classify as Refused (no fallback) before any empty-text handling
                if (finishReason == "content_filter")
                    return new LlmReply(text, ProviderVerdict.Refused, usage, $"{id}: content filter");
                // a tool-call turn is a SUCCESSFUL reply with empty text — surface it before the
                // empty-text→Failed/retry path (the tool loop drives the next turn)
                if (toolCalls is { Count: > 0 })
                    return new LlmReply(text, ProviderVerdict.Ok, usage) { ToolCalls = toolCalls };
                if (text.Length > 0)
                    return new LlmReply(text, ProviderVerdict.Ok, usage);
                // well-formed but empty and not filtered — same retry-once as a malformed body
            }

            // The backend ANSWERED, at HTTP 200, in the other channel. Classified before the retry on
            // purpose: re-sending to a host that just reported a rate limit is the harm, not the symptom.
            if (HttpBody.InBandError(body) is { } inBand) return InBandFailure(inBand);

            if (attempt == 0)
            {
                _logger.LogWarning("{Id}: malformed or empty response body; retrying once", id);
                continue; // one retry on a malformed/empty body
            }
            return new LlmReply("", ProviderVerdict.Failed, Detail: $"{id}: malformed or empty response after retry");
        }
    }

    public async IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var model = req.Model ?? config.Model ?? "";
        var timeout = options.ResolveTimeout(req);
        using var owned = OwnedClient();       // disposed only when Lyntai owns it
        var http = owned ?? httpFactory();     // BYO client: fetched, not disposed

        // the timeout is an INACTIVITY clock: it covers the connect and each line read, but is stopped
        // while we and the consumer process a line — a slow-but-healthy stream isn't killed under a
        // slow reader. Re-armed per read below (mirrors ProcessRunner.StreamLinesAsync).
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout); // arm for the connect (ResponseHeadersRead)

        var (response, startupError) = await OpenStreamAsync(req, model, timeout, http, timeoutCts, ct)
            .ConfigureAwait(false);
        if (startupError is not null)
        {
            yield return startupError; // pre-content error — the router may fall over
            yield break;
        }

        using var okResponse = response!;
        using var stream = await okResponse.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var progress = new StreamProgress();
        // the guarded loop (arm/read/stop + caller-cancel rethrow + fault→terminal) lives once in Core
        var guarded = GuardedStream.ReadAll<string, LlmChunk>(
            async () => await reader.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false),
            ex => LlmChunk.Error(
                timeoutCts.IsCancellationRequested ? ProviderVerdict.Timeout : ProviderVerdict.Failed,
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
            if (chunkUsage is not null) progress.Usage = chunkUsage;
            if (reason is not null) progress.FinishReason = reason;
            if (toolCallDeltas is not null) progress.ToolCalls.Add(toolCallDeltas);
            if (HttpBody.InBandError(payload) is { } streamed) progress.InBandError = streamed;
            if (text is { Length: > 0 })
            {
                progress.SawContent = true;
                yield return LlmChunk.Content(text);
            }
            // finish_reason terminates an NDJSON (Ollama) stream. An SSE stream instead runs on to its
            // [DONE] sentinel (or EOF) so the trailing stream_options usage chunk — sent AFTER the
            // finish_reason line, with an EMPTY choices array — is still read into `usage`.
            if (isFinal && _dialect == HttpDialect.Ollama) break;
        }

        foreach (var chunk in TerminalChunks(progress)) yield return chunk;
    }

    /// <summary>Open the streaming response: the connect plus the status check, both still under the armed
    /// inactivity clock. Exactly one half of the pair is non-null — a failure disposes the response itself,
    /// so the caller only ever owns one it can read.</summary>
    private async Task<(HttpResponseMessage? Response, LlmChunk? Error)> OpenStreamAsync(
        LlmRequest req, string model, TimeSpan timeout, HttpClient http,
        CancellationTokenSource timeoutCts, CancellationToken ct)
    {
        HttpResponseMessage? response = null;
        LlmChunk? startupError = null;
        try
        {
            response = await http.SendAsync(BuildRequest(req, model, stream: true),
                HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await HttpBody.SafeRead(response, timeoutCts.Token).ConfigureAwait(false);
                var mapped = MapHttpFailure(response.StatusCode, errorBody);
                startupError = LlmChunk.Error(mapped.Verdict, mapped.Detail);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            startupError = LlmChunk.Error(ProviderVerdict.Timeout, $"{id}: no response within {timeout}");
        }
        catch (HttpRequestException ex)
        {
            startupError = LlmChunk.Error(ProviderVerdict.Failed, $"{id}: {ex.Message}");
        }

        if (startupError is null) return (response, null);
        response?.Dispose();
        return (null, startupError);
    }

    /// <summary>What a stream reported as it ran, folded line by line and read once at the end by
    /// <see cref="TerminalChunks"/>.</summary>
    private sealed class StreamProgress
    {
        /// <summary>Whether any line carried real content. A zero-content stream is the streaming twin of
        /// <see cref="CompleteAsync"/>'s empty→Failed, so this decides between Final and Error.</summary>
        public bool SawContent { get; set; }

        /// <summary>The last usage any line carried — on SSE that is the trailing
        /// <c>stream_options</c> chunk, which arrives AFTER the finish reason.</summary>
        public LlmUsage? Usage { get; set; }

        /// <summary>The backend's own reason for stopping, or null where it gave none (Ollama sends none).</summary>
        public string? FinishReason { get; set; }

        /// <summary>Tool-call fragments, folded per vendor slot as they arrive and assembled once at the end.
        /// Held across the whole loop because a single call's arguments span many lines.</summary>
        public StreamingToolCalls ToolCalls { get; } = new();

        /// <summary>The last error the backend reported IN BAND. Remembered rather than acted on: an error
        /// line that arrives AFTER content is not terminal — codex's measured
        /// <c>{"type":"error","message":"Reconnecting ... 2/5"}</c> appeared in runs that went on to SUCCEED,
        /// and treating every error-ish line as fatal kills healthy calls that recovered (pitfalls.md, the
        /// mirror of the trap this fix closes). So it is consulted only on the zero-content path, where the
        /// alternative is a reasonless "no output".</summary>
        public string? InBandError { get; set; }
    }

    /// <summary>How a stream ENDS: the assembled tool calls, then exactly one terminal chunk — a
    /// <see cref="LlmChunkKind.Final"/> or an <see cref="LlmChunkKind.Error"/>, never both and never
    /// neither.</summary>
    private IEnumerable<LlmChunk> TerminalChunks(StreamProgress progress)
    {
        // a streamed content filter must end as Refused, not a benign Final — same verdict the
        // non-streaming path gives the identical finish_reason
        if (progress.FinishReason == "content_filter")
        {
            yield return LlmChunk.Error(ProviderVerdict.Refused, $"{id}: content filter");
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
        var assembled = progress.ToolCalls.Any ? progress.ToolCalls.Build() : [];
        foreach (var call in assembled) yield return LlmChunk.Tool(call);
        if (assembled.Count > 0)
        {
            yield return LlmChunk.Final(progress.Usage);
            yield break;
        }

        // A stream that said it stopped FOR tool calls and assembled none is a real failure rather than an
        // empty answer — the model asked for something this build could not read, and saying so beats the
        // synthetic "no output produced" below. Content having ALSO streamed does not soften it: the prose
        // was never the answer (the model stopped for tools), and a benign Final here is the silent
        // tool-call discard D71 exists to eliminate. The prose already streamed and stays; the Error is the
        // terminal chunk, which the router passes through unchanged post-commit.
        if (progress.FinishReason == "tool_calls")
        {
            yield return LlmChunk.Error(ProviderVerdict.Failed,
                $"{id}: the stream finished for tool calls but none could be assembled from its deltas");
            yield break;
        }
        // zero-content stream = the streaming twin of CompleteAsync's empty→Failed, so the router
        // can fall over pre-content instead of reporting a clean empty answer. When the backend said WHY in
        // band, that answer outranks the synthetic one: "no output produced" is the right verdict CLASS with
        // no reason and the wrong routing (Failed advances and strikes; RateLimited cools).
        if (!progress.SawContent)
        {
            var reply = progress.InBandError is not null
                ? InBandFailure(progress.InBandError)
                : new LlmReply("", ProviderVerdict.Failed, Detail: $"{id}: no output produced");
            yield return LlmChunk.Error(reply.Verdict, reply.Detail);
            yield break;
        }
        yield return LlmChunk.Final(progress.Usage);
    }

    private HttpRequestMessage BuildRequest(LlmRequest req, string model, bool stream)
    {
        // the logger travels into the Ollama payload so an attachment /api/chat cannot carry is reported
        // rather than dropped in silence (its images array is inline base64 only — no remote URL form)
        var payload = _dialect == HttpDialect.Ollama
            ? OllamaPayload.Build(req, model, stream, config.OllamaContextSize, _logger)
            : OpenAiPayload.Build(req, model, stream);

        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint())
        {
            Content = new StringContent(payload.ToJsonString(), new UTF8Encoding(false), "application/json"),
        };
        HttpEndpoint.ApplyAuth(request, config.ApiKey, _dialect);
        return request;
    }

    /// <summary>The chat endpoint — Ollama's native <c>/api/chat</c>, otherwise the OpenAI-compatible
    /// <c>chat/completions</c> route.</summary>
    private Uri Endpoint() =>
        HttpEndpoint.Build(config.BaseUrl, _dialect, ollamaNativePath: "/api/chat", openAiRoute: "chat/completions");

    /// <summary>Whether this provider has anything to authenticate WITH — what separates "not set up yet"
    /// from "your key was rejected" when the server answers 401/403.</summary>
    private bool HasCredentials => !string.IsNullOrWhiteSpace(config.ApiKey);

    /// <summary>Classify an error the backend reported IN BAND under a 2xx status. The status carries no
    /// information here — it said success — so the verdict comes from the backend's own words through the
    /// ONE shared corpus, never a local heuristic.
    /// <para>The <see cref="ProviderVerdict.AuthFailed"/> → <see cref="ProviderVerdict.NotConfigured"/> promotion is
    /// the same two-term rule <see cref="ProviderVerdictClassifier.FromHttpFailure(HttpStatusCode, string, bool)"/>
    /// applies on the status path, restated here because that overload needs a FAILED status to key on and
    /// this path has none. Keeping the two in step matters: NotConfigured skips a candidate blamelessly and
    /// lets a host offer setup, while AuthFailed benches it for the cooldown window.</para></summary>
    private LlmReply InBandFailure(string error)
    {
        var verdict = ProviderVerdictClassifier.FromErrorText(error);
        if (verdict == ProviderVerdict.AuthFailed && !HasCredentials) verdict = ProviderVerdict.NotConfigured;
        return new LlmReply("", verdict, Detail: $"{id}: {HttpBody.Head(error)}");
    }

    private LlmReply MapHttpFailure(HttpStatusCode status, string body)
    {
        var detail = $"{id}: HTTP {(int)status} {HttpBody.Head(body)}";
        // typed status wins; body text goes through the ONE shared classifier (never local heuristics).
        // hasCredentials separates "never set up" (NotConfigured — skipped blamelessly) from "your key was
        // rejected" (AuthFailed — benched for the cooldown window). A local OpenAI-compatible server needs no
        // key, so the missing key only means unconfigured once the server has actually demanded one.
        return new LlmReply("", ProviderVerdictClassifier.FromHttpFailure(status, body, HasCredentials), Detail: detail);
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
