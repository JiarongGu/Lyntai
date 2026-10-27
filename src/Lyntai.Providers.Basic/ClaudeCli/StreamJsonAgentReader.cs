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
    private readonly TimeProvider _time;
    private string? _model;
    private string? _sessionId;
    private string? _lastAssistantText;
    private string? _assistantError;
    private RejectedLimit? _rejectedLimit;

    /// <param name="time">The clock a <see cref="UsageReading.MeasuredAt"/> is stamped from; null = the system
    /// clock.</param>
    public StreamJsonAgentReader(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

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
        "rate_limit_event" => NoteRateLimit(root),
        // Any other type → nothing.
        _ => [],
    };

    // `rate_limit_info` is not surfaced as an event (no AgentStreamEvent carries it); a REJECTED one is kept so a
    // failing result can name the limit and when it renews. An `allowed` frame, which is every ordinary one, is
    // never kept.
    private IEnumerable<AgentStreamEvent> NoteRateLimit(JsonElement root)
    {
        _rejectedLimit = StreamJsonFailure.ReadRejection(root) ?? _rejectedLimit;
        return [];
    }

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

        // the CLI marks a message that is an API error with a category beside `message` — the door's own field;
        // the LAST message's is the one that can have ended the turn
        _assistantError = WireJson.String(root, "error");

        var toolCalls = new List<ToolCall>();
        PlanUpdated? plan = null;
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
                    if (name == TodoWriteTool && ReadTodoPlan(block) is { } todoPlan)
                        plan = todoPlan;   // the last list in a message is the plan as it now stands
                }
            }
        }

        // Remember the last NON-EMPTY assistant text (a later tool-only turn must not clear it).
        if (text is { Length: > 0 })
            _lastAssistantText = text.ToString();

        foreach (var tc in toolCalls)
            yield return tc;

        if (plan is not null)
            yield return plan;

        // Emit UsageLive if usage is present
        if (msg.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            yield return new UsageLive(
                WireJson.Long(usage, "input_tokens"),
                WireJson.Long(usage, "output_tokens"),
                WireJson.Long(usage, "cache_read_input_tokens"));
        }
    }

    /// <summary>The harness's own to-do tool: its list is the agent's plan. The call is still a
    /// <see cref="ToolCall"/> — the plan event is read beside it, not instead of it.</summary>
    private const string TodoWriteTool = "TodoWrite";

    /// <summary>The <c>todos</c> of a <c>TodoWrite</c> call as a plan; null when the call carries no list. An
    /// entry with no <c>content</c> string is skipped.</summary>
    private static PlanUpdated? ReadTodoPlan(JsonElement toolUse)
    {
        if (WireJson.Object(toolUse, "input") is not { } input || WireJson.Array(input, "todos") is not { } todos)
            return null;

        var entries = new List<PlanEntry>();
        foreach (var todo in todos.EnumerateArray())
        {
            if (WireJson.String(todo, "content") is { } content)
                entries.Add(new PlanEntry(content, WirePlan.Status(WireJson.String(todo, "status"))));
        }
        return new PlanUpdated(entries);
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

        // Taken from the result's usage and never from the assistant lines: the result's figure is the turn's
        // total over every API call, while a streamed message's output count is partial.
        if (StreamJsonFields.ReadUsageCounts(root) is { } counts)
            yield return new UsageReading(counts.Input, counts.Output, counts.CacheRead, counts.CacheCreate,
                UsageReading.Tokens, UsageScope.Turn, _time.GetUtcNow());

        // classified, never hand-rolled, so an expired login is AuthFailed and a 429 RateLimited; the door's own
        // fields decide before its words, and Failed only when neither reads
        var failure = isError
            ? StreamJsonFailure.Read(WireJson.Int32(root, "api_error_status"), _assistantError, _rejectedLimit, finalText, _time)
            : null;
        yield return new SessionEnded(
            Verdict: failure is null ? ProviderVerdict.Ok : ProviderVerdictClassifier.FromAgentFailure(failure, finalText ?? subtype),
            IsError: isError,
            Subtype: subtype,
            SessionId: sessionId,
            FinalText: finalText,
            Diagnostic: null)
        { Failure = failure };
    }
}
