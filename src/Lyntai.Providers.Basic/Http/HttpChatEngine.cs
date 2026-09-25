using Lyntai.Inference;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Lyntai.Inference.Streaming;
using Microsoft.Extensions.Logging;

namespace Lyntai.Providers.Http;

/// <summary>The generic engine behind every HTTP chat backend. Give it an <see cref="IHttpChatWire"/>
/// (where to POST, how to shape the body, how to read what comes back) and it supplies everything that must
/// not be re-derived per backend:
///
/// <list type="bullet">
/// <item>HTTP status → verdict through the one <see cref="ProviderVerdictClassifier"/> (429 RateLimited,
///   5xx Failed, deadline Timeout), with the credentials-aware NotConfigured/AuthFailed split,</item>
/// <item>an error the backend reports IN BAND under HTTP 200, classified BEFORE the retry — re-sending to a
///   host that just said it was rate-limited is the harm, not the symptom,</item>
/// <item>exactly one retry on a malformed or empty body,</item>
/// <item>a content filter classified <see cref="ProviderVerdict.Refused"/> on both paths,</item>
/// <item>the streaming timeout as an INACTIVITY clock re-armed per read, never one wall clock,</item>
/// <item>streamed tool calls assembled from their fragments and delivered complete,</item>
/// <item>exactly one terminal chunk — a Final or an Error, never both and never neither.</item>
/// </list>
///
/// The same composition the CLI providers use (<see cref="Lyntai.Inference.Cli.CliProviderEngine"/>): a
/// provider is the public backend, its wire is the varying half, and the rules live once here.</summary>
internal sealed class HttpChatEngine(
    string id,
    IHttpChatWire wire,
    Func<HttpClient> httpFactory,
    LyntaiOptions options,
    ILogger logger,
    bool disposeHttpClient)
{
    /// <summary>Get the per-call HttpClient. Lyntai-created clients (from the named IHttpClientFactory
    /// client) are disposed after each call; an APP-supplied (BYO) client is NEVER disposed — the app
    /// owns its lifetime, so disposing it would break every call after the first.</summary>
    private HttpClient? OwnedClient() => disposeHttpClient ? httpFactory() : null;

    private readonly bool _hasCredentials = HttpEndpoint.HasCredentials(wire.ApiKey);

    public async Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default)
    {
        var model = req.Model ?? wire.DefaultModel ?? "";
        var timeout = options.ResolveTimeout(req);
        using var owned = OwnedClient();       // disposed only when Lyntai owns it
        var http = owned ?? httpFactory();     // BYO client: fetched, not disposed
        for (var attempt = 0; ; attempt++)
        {
            var reply = await HttpJsonCall.SendAsync(http, BuildRequest(req, model, stream: false), timeout,
                _hasCredentials, id, what: null, ct).ConfigureAwait(false);
            if (reply.Body is not { } body) return new TextResponse("", reply.Verdict, Detail: reply.Detail);

            if (wire.TryExtract(body, out var text, out var usage, out var finishReason, out var toolCalls))
            {
                // a content filter often arrives as HTTP 200 + finish_reason with EMPTY content —
                // it must classify as Refused (no fallback) before any empty-text handling
                if (finishReason == "content_filter")
                    return new TextResponse(text, ProviderVerdict.Refused, usage, $"{id}: content filter");
                // a tool-call turn is a SUCCESSFUL reply with empty text — surface it before the
                // empty-text→Failed/retry path (the tool loop drives the next turn)
                if (toolCalls is { Count: > 0 })
                    return new TextResponse(text, ProviderVerdict.Ok, usage) { ToolCalls = toolCalls };
                if (text.Length > 0)
                    return new TextResponse(text, ProviderVerdict.Ok, usage);
                // well-formed but empty and not filtered — same retry-once as a malformed body
            }

            // The backend ANSWERED, at HTTP 200, in the other channel. Classified before the retry on
            // purpose: re-sending to a host that just reported a rate limit is the harm, not the symptom.
            if (HttpBody.InBandError(body) is { } inBand) return InBandFailure(inBand);

            if (attempt == 0)
            {
                logger.LogWarning("{Id}: malformed or empty response body; retrying once", id);
                continue; // one retry on a malformed/empty body
            }
            return new TextResponse("", ProviderVerdict.Failed, Detail: $"{id}: malformed or empty response after retry");
        }
    }

    public async IAsyncEnumerable<TextChunk> StreamAsync(TextRequest req, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var model = req.Model ?? wire.DefaultModel ?? "";
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
        var guarded = GuardedStream.ReadAll<string, TextChunk>(
            async () => await reader.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false),
            ex => TextChunk.Error(
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

            // SSE ("data: {...}" / "data: [DONE]") for the OpenAI-shaped wire; bare NDJSON for Ollama's
            var payload = line.StartsWith("data:", StringComparison.Ordinal) ? line[5..].Trim() : line.Trim();
            if (payload.Length == 0) continue;
            if (payload == "[DONE]") break;

            var parsed = wire.ParseStreamLine(payload);
            if (parsed.Usage is not null) progress.Usage = parsed.Usage;
            if (parsed.FinishReason is not null) progress.FinishReason = parsed.FinishReason;
            if (parsed.ToolCalls is not null) progress.ToolCalls.Add(parsed.ToolCalls);
            if (parsed.InBandError is { } streamed) progress.InBandError = streamed;
            if (parsed.Text is { Length: > 0 })
            {
                progress.SawContent = true;
                yield return TextChunk.Content(parsed.Text);
            }
            if (parsed.IsFinal && wire.EndsStreamOnFinal) break;
        }

        foreach (var chunk in TerminalChunks(progress)) yield return chunk;
    }

    /// <summary>Open the streaming response: the connect plus the status check, both still under the armed
    /// inactivity clock. Exactly one half of the pair is non-null — a failure disposes the response itself,
    /// so the caller only ever owns one it can read.</summary>
    private async Task<(HttpResponseMessage? Response, TextChunk? Error)> OpenStreamAsync(
        TextRequest req, string model, TimeSpan timeout, HttpClient http,
        CancellationTokenSource timeoutCts, CancellationToken ct)
    {
        HttpResponseMessage? response = null;
        TextChunk? startupError = null;
        try
        {
            response = await http.SendAsync(BuildRequest(req, model, stream: true),
                HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await HttpBody.SafeRead(response, timeoutCts.Token).ConfigureAwait(false);
                startupError = TextChunk.Error(
                    ProviderVerdictClassifier.FromHttpFailure(response.StatusCode, errorBody, _hasCredentials),
                    $"{id}: HTTP {(int)response.StatusCode} {HttpBody.Head(errorBody)}");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            startupError = TextChunk.Error(ProviderVerdict.Timeout, $"{id}: no response within {timeout}");
        }
        catch (HttpRequestException ex)
        {
            startupError = TextChunk.Error(ProviderVerdict.Failed, $"{id}: {ex.Message}");
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
        public TextUsage? Usage { get; set; }

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
    /// <see cref="TextChunkKind.Final"/> or an <see cref="TextChunkKind.Error"/>, never both and never
    /// neither.</summary>
    private IEnumerable<TextChunk> TerminalChunks(StreamProgress progress)
    {
        // a streamed content filter must end as Refused, not a benign Final — same verdict the
        // non-streaming path gives the identical finish_reason
        if (progress.FinishReason == "content_filter")
        {
            yield return TextChunk.Error(ProviderVerdict.Refused, $"{id}: content filter");
            yield break;
        }
        // TOOL CALLS, assembled from their fragments and delivered before the terminal chunk — never dropped
        // for a benign Final when prose streamed alongside them. Yielded here rather than as they arrive
        // because TextChunk.ToolCall promises a COMPLETE call. `finish_reason` is not required: Ollama sends
        // none, and a stream that produced complete calls has produced them whatever it says about stopping.
        var assembled = progress.ToolCalls.Any ? progress.ToolCalls.Build() : [];
        foreach (var call in assembled) yield return TextChunk.Tool(call);
        if (assembled.Count > 0)
        {
            yield return TextChunk.Final(progress.Usage);
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
            yield return TextChunk.Error(ProviderVerdict.Failed,
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
                : new TextResponse("", ProviderVerdict.Failed, Detail: $"{id}: no output produced");
            yield return TextChunk.Error(reply.Verdict, reply.Detail);
            yield break;
        }
        yield return TextChunk.Final(progress.Usage);
    }

    private HttpRequestMessage BuildRequest(TextRequest req, string model, bool stream) =>
        HttpJsonCall.Post(wire.Endpoint, wire.BuildPayload(req, model, stream), wire.ApiKey, wire.AzureConventions);

    /// <summary>Classify an error the backend reported IN BAND under a 2xx status. The status carries no
    /// information here — it said success — so the verdict comes from the backend's own words through the
    /// ONE shared corpus, never a local heuristic.
    /// <para>The <see cref="ProviderVerdict.AuthFailed"/> → <see cref="ProviderVerdict.NotConfigured"/> promotion is
    /// the same two-term rule <see cref="ProviderVerdictClassifier.FromHttpFailure(HttpStatusCode, string, bool)"/>
    /// applies on the status path, restated here because that overload needs a FAILED status to key on and
    /// this path has none. Keeping the two in step matters: NotConfigured skips a candidate blamelessly and
    /// lets a host offer setup, while AuthFailed benches it for the cooldown window.</para></summary>
    private TextResponse InBandFailure(string error)
    {
        var verdict = ProviderVerdictClassifier.FromErrorText(error);
        if (verdict == ProviderVerdict.AuthFailed && !_hasCredentials) verdict = ProviderVerdict.NotConfigured;
        return new TextResponse("", verdict, Detail: $"{id}: {HttpBody.Head(error)}");
    }

}
