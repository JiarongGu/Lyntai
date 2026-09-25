using Lyntai.Inference;
using System.Text;
using System.Text.Json;
using Lyntai.Agents;
using Lyntai.Providers.Basic;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>Stateful, per-run translator: feed it each <c>claude --output-format stream-json
/// --include-partial-messages</c> line via <see cref="Read"/>; it yields 0..N <see cref="AgentStreamEvent"/>s.
/// Tolerant — an unknown/malformed line yields nothing, never throws. Remembers the model id across lines
/// so the terminal <see cref="UsageFinal"/> carries it. Line-translation ONLY: it does NOT set
/// <see cref="SessionEnded.Diagnostic"/> (no stderr knowledge — the session runner fills that).</summary>
internal sealed class StreamJsonAgentReader
{
    private string? _model;
    private string? _sessionId;
    private string? _lastAssistantText;

    /// <summary>Translates one stream-json line into 0..N events. Never throws.</summary>
    public IReadOnlyList<AgentStreamEvent> Read(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return [];
        try
        {
            // materialized inside the using: every element read belongs to the document
            using var doc = JsonDocument.Parse(line);
            return [.. ReadLine(doc.RootElement)];
        }
        catch (Exception ex) when (WireJson.IsShapeFault(ex))
        {
            return [];
        }
    }

    private IEnumerable<AgentStreamEvent> ReadLine(JsonElement root) => WireJson.String(root, "type") switch
    {
        "system" => ReadSystem(root),
        "stream_event" => ReadStreamEvent(root),
        "assistant" => ReadAssistant(root),
        "user" => ReadUser(root),
        "result" => ReadResult(root),
        // Any other type → nothing. That includes `rate_limit_event`, whose `rate_limit_info` is not
        // surfaced: no AgentStreamEvent carries it, and adding one is a public-surface decision.
        _ => [],
    };

    // ── system/init ──────────────────────────────────────────────────────────

    private IEnumerable<AgentStreamEvent> ReadSystem(JsonElement root)
    {
        // Capture model early; yield SessionStarted
        if (root.TryGetProperty("model", out var modelEl) && modelEl.ValueKind == JsonValueKind.String)
            _model = modelEl.GetString();

        // Every `system` line carries the session_id — `init`, and then each `thinking_tokens` PROGRESS tick —
        // so announcing per line announced one session start per tick. Announce an id once, when it is new.
        // Keyed on the id rather than on `subtype == "init"`, which not every emitter sets.
        if (root.TryGetProperty("session_id", out var sidEl) && sidEl.ValueKind == JsonValueKind.String &&
            sidEl.GetString() is { } sessionId && sessionId != _sessionId)
        {
            _sessionId = sessionId;
            yield return new SessionStarted(sessionId);
        }
    }

    // ── stream_event (partial content deltas) ────────────────────────────────

    private static IEnumerable<AgentStreamEvent> ReadStreamEvent(JsonElement root)
    {
        if (!root.TryGetProperty("event", out var eventEl) || eventEl.ValueKind != JsonValueKind.Object)
            yield break;

        if (!eventEl.TryGetProperty("type", out var eventTypeEl) || eventTypeEl.ValueKind != JsonValueKind.String)
            yield break;

        // Only content_block_delta carries text/thinking deltas we care about
        if (!eventTypeEl.ValueEquals("content_block_delta"))
            yield break;

        if (!eventEl.TryGetProperty("delta", out var deltaEl) || deltaEl.ValueKind != JsonValueKind.Object)
            yield break;

        if (!deltaEl.TryGetProperty("type", out var deltaTypeEl) || deltaTypeEl.ValueKind != JsonValueKind.String)
            yield break;

        var deltaType = deltaTypeEl.GetString();
        if (deltaType == "text_delta")
        {
            if (deltaEl.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                yield return new TextDelta(textEl.GetString()!);
        }
        else if (deltaType == "thinking_delta")
        {
            if (deltaEl.TryGetProperty("thinking", out var thinkingEl) && thinkingEl.ValueKind == JsonValueKind.String)
                yield return new Thinking(thinkingEl.GetString()!);
        }
        // Any other delta type → yield nothing
    }

    // ── assistant (complete message, may contain tool_use blocks + usage) ────

    private IEnumerable<AgentStreamEvent> ReadAssistant(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object)
            yield break;

        // Update model if present on the message
        if (msg.TryGetProperty("model", out var modelEl) && modelEl.ValueKind == JsonValueKind.String)
            _model = modelEl.GetString();

        var toolCalls = new List<ToolCall>();
        StringBuilder? text = null;
        if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                var blockType = WireJson.String(block, "type");
                if (blockType == "text")
                {
                    // text blocks are NOT re-emitted (already streamed via stream_event deltas), but the
                    // last assistant turn's text is retained so a terminal result that ends empty
                    // (truncation / older CLI / provider variant) can fall back to it (see ReadResult).
                    if (block.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                        (text ??= new StringBuilder()).Append(textEl.GetString());
                }
                else if (blockType == "tool_use")
                {
                    var name = block.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                        ? nameEl.GetString()!
                        : string.Empty;
                    var id = block.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                        ? idEl.GetString()
                        : null;
                    var argsJson = block.TryGetProperty("input", out var inputEl)
                        ? inputEl.GetRawText()
                        : "{}";
                    toolCalls.Add(new ToolCall(name, argsJson, id));
                }
            }
        }

        // Remember the last NON-EMPTY assistant text (a later tool-only turn must not clear it).
        if (text is { Length: > 0 })
            _lastAssistantText = text.ToString();

        foreach (var tc in toolCalls)
            yield return tc;

        // Emit UsageLive if usage is present
        if (msg.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            yield return new UsageLive(
                WireJson.Long(usage, "input_tokens"),
                WireJson.Long(usage, "output_tokens"),
                WireJson.Long(usage, "cache_read_input_tokens"));
        }
    }

    // ── user (tool results fed back) ─────────────────────────────────────────

    private static IEnumerable<AgentStreamEvent> ReadUser(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object)
            yield break;

        if (!msg.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var block in content.EnumerateArray())
        {
            if (WireJson.String(block, "type") != "tool_result")
                continue;

            var callId = block.TryGetProperty("tool_use_id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString()
                : null;

            var isError = block.TryGetProperty("is_error", out var isErrorEl) && isErrorEl.ValueKind == JsonValueKind.True;

            string contentStr = string.Empty;
            if (block.TryGetProperty("content", out var contentEl))
                contentStr = contentEl.ValueKind == JsonValueKind.String
                    ? contentEl.GetString() ?? string.Empty
                    : StreamJsonFields.ConcatTextBlocks(contentEl); // array → concat text blocks; else ""

            yield return new ToolResult(callId, contentStr, isError);
        }
    }

    // ── result (terminal) ────────────────────────────────────────────────────

    private IEnumerable<AgentStreamEvent> ReadResult(JsonElement root)
    {
        var isError = root.TryGetProperty("is_error", out var isErrorEl) && isErrorEl.ValueKind == JsonValueKind.True;

        var subtype = root.TryGetProperty("subtype", out var subtypeEl) && subtypeEl.ValueKind == JsonValueKind.String
            ? subtypeEl.GetString()
            : null;

        var sessionId = root.TryGetProperty("session_id", out var sidEl) && sidEl.ValueKind == JsonValueKind.String
            ? sidEl.GetString()
            : null;

        var finalText = root.TryGetProperty("result", out var resultEl) && resultEl.ValueKind == JsonValueKind.String
            ? resultEl.GetString()
            : null;

        // A run that ended with assistant text but an empty terminal result falls back to that text, so a
        // consumer treating empty FinalText as failure does not fail spuriously. Gated on !isError: a failed
        // turn's partial text is not an answer (IAgentSession's fold states the same rule; so does codex).
        if (!isError && string.IsNullOrWhiteSpace(finalText) && !string.IsNullOrEmpty(_lastAssistantText))
            finalText = _lastAssistantText;

        // UsageFinal first (if usage present), then SessionEnded — shared wire read; UsageFinal projects
        // the raw counts incl. cache-create (it deliberately carries no cost — the app prices itself)
        if (StreamJsonFields.ReadUsage(root) is { } w)
            yield return new UsageFinal(w.Input, w.Output, w.CacheRead, w.CacheCreate, _model);

        yield return new SessionEnded(
            // classified, never hand-rolled, so an expired login is AuthFailed and a 429 RateLimited; Failed
            // only when the CLI gives no words to read
            Verdict: isError
                ? (string.IsNullOrWhiteSpace(finalText ?? subtype)
                    ? ProviderVerdict.Failed
                    : ProviderVerdictClassifier.FromErrorText(finalText ?? subtype!))
                : ProviderVerdict.Ok,
            IsError: isError,
            Subtype: subtype,
            SessionId: sessionId,
            FinalText: finalText,
            Diagnostic: null);
    }
}
