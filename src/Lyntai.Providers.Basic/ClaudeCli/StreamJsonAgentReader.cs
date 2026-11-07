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
internal sealed class StreamJsonAgentReader : ICliAgentLineReader
{
    private readonly TimeProvider _time;
    private string? _model;
    private string? _sessionId;
    private string? _lastAssistantText;
    private string? _assistantError;
    private RejectedLimit? _rejectedLimit;
    private readonly UnreadFrames _unread = new();
    private string _line = "";

    // a streamed text delta since the last own assistant message (or message_start): that message's words were already yielded
    private bool _streamedText;
    // the id the last message_start stated: a content_block_delta names no message of its own
    private string? _streamMessageId;
    private bool _readAssistant;
    private bool _readResult;
    private readonly HashSet<string> _pendingCalls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _taskCalls = new(StringComparer.Ordinal);
    private readonly HashSet<(string Task, string Path)> _taskOutputs = [];

    /// <summary>The <see cref="SessionEnded.Subtype"/> of a turn the process ended cleanly with no <c>result</c> frame.</summary>
    internal const string NoResultSubtype = "no-result-frame";

    /// <param name="time">The clock a <see cref="UsageReading.MeasuredAt"/> is stamped from; null = the system
    /// clock.</param>
    public StreamJsonAgentReader(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    /// <summary>The CLI states its own ending in the <c>result</c> line, so the end of the stream adds none — except a process
    /// that exits 0 after an assistant message of its own with no <c>result</c> frame, which ends the turn as
    /// <see cref="NoResultSubtype"/>: not an error, the last assistant text as its words (null when it had none). It stays
    /// the loop's failed turn when a tool call of its own was never answered (the agent was cut off mid-step), and an
    /// assistant message that was an API error ends it with that error's failure. A non-zero exit is the process fault's.</summary>
    public SessionEnded? ReadEnd(int exitCode)
    {
        if (exitCode != 0 || _readResult || !_readAssistant || _pendingCalls.Count > 0) return null;
        var failure = _assistantError is null
            ? null
            : StreamJsonFailure.Read(null, _assistantError, _rejectedLimit, _lastAssistantText, _time);
        return new SessionEnded(
            Verdict: failure is null ? ProviderVerdict.Ok : ProviderVerdictClassifier.FromAgentFailure(failure, _lastAssistantText ?? NoResultSubtype),
            IsError: failure is not null,
            Subtype: NoResultSubtype,
            SessionId: _sessionId,
            FinalText: _lastAssistantText,
            Diagnostic: null)
        { Failure = failure };
    }

    /// <summary>The frames no case here took, by type: a type this reader has no case for, and a streamed event it
    /// has no case for.</summary>
    public IReadOnlyList<AgentUnreadFrame> Unread() => _unread.Snapshot();

    /// <summary>Translates one stream-json line into 0..N events. Never throws.</summary>
    public IReadOnlyList<AgentStreamEvent> Read(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return [];
        _line = line;
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

    // A subagent's lines arrive on the session's stdout with `parent_tool_use_id` set to the call that spawned it.
    // Such a line yields its calls and results with that id and nothing the session owns: no plan, usage tick,
    // text, model or error field, and no text delta (a TextDelta has no parent to name).
    private IEnumerable<AgentStreamEvent> ReadLine(JsonElement root) => WireJson.String(root, "type") switch
    {
        "system" => WireJson.String(root, "subtype") is { } subtype && subtype.StartsWith("task_", StringComparison.Ordinal)
            ? ReadTask(root, subtype)
            : ReadSystem(root),
        "stream_event" => WireJson.String(root, "parent_tool_use_id") is null ? ReadStreamEvent(root) : [],
        "assistant" => ReadAssistant(root, WireJson.String(root, "parent_tool_use_id")),
        "user" => ReadUser(root, WireJson.String(root, "parent_tool_use_id")),
        "result" => ReadResult(root),
        "rate_limit_event" => NoteRateLimit(root),
        // Any other type → nothing, and kept as unread
        var type => NoteUnread(type, null),
    };

    private IEnumerable<AgentStreamEvent> NoteUnread(string? type, string? subtype)
    {
        if (type is not null) _unread.Note(type, subtype, _line);
        return [];
    }

    // the envelope of every streamed message, which carries nothing a reader would read
    private static readonly HashSet<string> KnownStreamEvents = new(StringComparer.Ordinal)
        { "message_start", "content_block_start", "content_block_stop", "message_delta", "message_stop", "content_block_delta" };

    // every `rate_limit_info` is an AgentAllowanceReport; a REJECTED one is also kept so a failing result can name the
    // limit and when it renews. An `allowed` frame, which is every ordinary one, is never kept as a failure.
    private IEnumerable<AgentStreamEvent> NoteRateLimit(JsonElement root)
    {
        _rejectedLimit = StreamJsonFailure.ReadRejection(root) ?? _rejectedLimit;
        return WireJson.Object(root, "rate_limit_info") is { } info ? [StreamJsonFailure.ReadReport(info)] : [];
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
            // the agent's own start is its init (or a first frame that names no subtype); a later frame carrying a new id only reports one
            var announced = WireJson.String(root, "subtype") == "init" || (_sessionId is null && WireJson.String(root, "subtype") is null);
            _sessionId = sessionId;
            yield return new SessionStarted(sessionId) { Announced = announced };
        }

        if (WireJson.String(root, "subtype") == "init" && ReadLoaded(root) is { } loaded)
            yield return loaded;
    }

    // what the init frame lists; null when it carries none of the four lists, so a bare init yields nothing beyond its start
    private static AgentLoaded? ReadLoaded(JsonElement root)
    {
        if (!HasList(root, "tools") && !HasList(root, "mcp_servers") && !HasList(root, "skills") && !HasList(root, "plugins"))
            return null;

        var servers = new List<AgentLoadedServer>();
        if (WireJson.Array(root, "mcp_servers") is { } list)
        {
            foreach (var server in list.EnumerateArray())
            {
                if (WireJson.String(server, "name") is { Length: > 0 } name)
                    servers.Add(new AgentLoadedServer(name, WireJson.String(server, "status") is { Length: > 0 } status ? status : null));
            }
        }
        return new AgentLoaded(Names(root, "tools"), servers, Names(root, "skills"), Names(root, "plugins"));
    }

    private static bool HasList(JsonElement root, string field) => WireJson.Array(root, field) is not null;

    // an entry is a bare string or an object's `name`, by the harness version
    private static List<string> Names(JsonElement root, string field)
    {
        var names = new List<string>();
        if (WireJson.Array(root, field) is not { } list) return names;
        foreach (var item in list.EnumerateArray())
        {
            var name = item.ValueKind == JsonValueKind.String ? item.GetString() : WireJson.String(item, "name");
            if (!string.IsNullOrEmpty(name)) names.Add(name);
        }
        return names;
    }

    // ── task_* (background work) ─────────────────────────────────────────────

    // A `task_*` line is routed here before ReadSystem, so it can never announce a session or move anything the
    // session owns. `task_notification` is the end: every capture carries one per task, while `task_updated` is a
    // patch that precedes it with a status of its own, so reading both would end a task twice. `task_progress` and
    // `background_tasks_changed` say nothing these two do not. A task's output file is named by whichever of the two
    // carries `output_file` first (the capture's notification does), and an end is keyed by the call its start named
    // when its own line names none.
    private IEnumerable<AgentStreamEvent> ReadTask(JsonElement root, string subtype)
    {
        if (WireJson.String(root, "task_id") is not { Length: > 0 } id)
            return [];

        var callId = WireJson.String(root, "tool_use_id");
        switch (subtype)
        {
            case "task_started":
                if (callId is not null) _taskCalls[id] = callId;
                return [new AgentTaskStarted(id, WireJson.String(root, "task_type"), WireJson.String(root, "description"), callId)
                    {
                        ParentTaskId = WireJson.String(root, "parent_task_id"),
                        Depth = WireJson.Int32(root, "spawn_depth"),
                        SubagentType = WireJson.String(root, "subagent_type"),
                    },
                    .. OutputFile(root, id)];
            case "task_notification":
                return [.. OutputFile(root, id),
                    new AgentTaskEnded(id, WireJson.String(root, "status")) { CallId = callId ?? _taskCalls.GetValueOrDefault(id) }];
            default:
                return [];
        }
    }

    private IEnumerable<AgentStreamEvent> OutputFile(JsonElement root, string id) =>
        WireJson.String(root, "output_file") is { Length: > 0 } path && _taskOutputs.Add((id, path))
            ? [new AgentTaskOutputFile(id, path)]
            : [];

    // ── stream_event (partial content deltas) ────────────────────────────────

    private IEnumerable<AgentStreamEvent> ReadStreamEvent(JsonElement root)
    {
        if (!root.TryGetProperty("event", out var eventEl) || eventEl.ValueKind != JsonValueKind.Object)
            yield break;

        if (!eventEl.TryGetProperty("type", out var eventTypeEl) || eventTypeEl.ValueKind != JsonValueKind.String)
            yield break;

        // Only content_block_delta carries text/thinking deltas we care about
        if (!eventTypeEl.ValueEquals("content_block_delta"))
        {
            if (eventTypeEl.ValueEquals("message_start"))
            {
                _streamedText = false;
                _streamMessageId = WireJson.Object(eventEl, "message") is { } started ? WireJson.String(started, "id") : null;
            }
            if (!KnownStreamEvents.Contains(eventTypeEl.GetString()!)) _unread.Note("stream_event", eventTypeEl.GetString(), _line);
            yield break;
        }

        if (!eventEl.TryGetProperty("delta", out var deltaEl) || deltaEl.ValueKind != JsonValueKind.Object)
            yield break;

        if (!deltaEl.TryGetProperty("type", out var deltaTypeEl) || deltaTypeEl.ValueKind != JsonValueKind.String)
            yield break;

        var deltaType = deltaTypeEl.GetString();
        if (deltaType == "text_delta")
        {
            if (deltaEl.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
            {
                _streamedText = true;
                yield return new TextDelta(textEl.GetString()!) { MessageId = _streamMessageId };
            }
        }
        else if (deltaType == "thinking_delta")
        {
            if (deltaEl.TryGetProperty("thinking", out var thinkingEl) && thinkingEl.ValueKind == JsonValueKind.String)
                yield return new Thinking(thinkingEl.GetString()!);
        }
        // Any other delta type → yield nothing
    }

    // ── assistant (complete message, may contain tool_use blocks + usage) ────

    private IEnumerable<AgentStreamEvent> ReadAssistant(JsonElement root, string? parentCallId)
    {
        if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object)
            yield break;

        var own = parentCallId is null;
        var messageId = WireJson.String(msg, "id");

        // Update model if present on the message
        if (own && msg.TryGetProperty("model", out var modelEl) && modelEl.ValueKind == JsonValueKind.String)
            _model = modelEl.GetString();

        // the CLI marks a message that is an API error with a category beside `message` — the door's own field;
        // the LAST message's is the one that can have ended the turn
        if (own)
            _assistantError = WireJson.String(root, "error");

        var blocks = new List<AgentStreamEvent>();
        PlanUpdated? plan = null;
        StringBuilder? text = null;
        if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                var blockType = WireJson.String(block, "type");
                if (blockType == "text")
                {
                    // own text is NOT re-emitted when stream_event deltas already carried it (see below), and the
                    // last assistant turn's text is retained so a terminal result that ends empty
                    // (truncation / older CLI / provider variant) can fall back to it (see ReadResult).
                    if (own && block.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                        (text ??= new StringBuilder()).Append(textEl.GetString());
                    // a subagent's words arrive once, whole, and have no stream of their own to be read from
                    else if (!own && WireJson.String(block, "text") is { Length: > 0 } childText)
                        blocks.Add(new TextDelta(childText) { ParentCallId = parentCallId, MessageId = messageId });
                }
                else if (blockType == "thinking" && !own && WireJson.String(block, "thinking") is { Length: > 0 } childThought)
                {
                    blocks.Add(new Thinking(childThought) { ParentCallId = parentCallId });
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
                    blocks.Add(new ToolCall(name, argsJson, id) { ParentCallId = parentCallId });
                    if (own && id is not null) _pendingCalls.Add(id);
                    if (own && name == TodoWriteTool && ReadTodoPlan(block) is { } todoPlan)
                        plan = todoPlan;   // the last list in a message is the plan as it now stands
                }
            }
        }

        // Remember the last NON-EMPTY assistant text (a later tool-only turn must not clear it).
        if (text is { Length: > 0 })
            _lastAssistantText = text.ToString();

        // an answer that arrived whole, with no partial delta before it, is read from the message itself
        if (own)
        {
            _readAssistant = true;
            if (text is { Length: > 0 } && !_streamedText) yield return new TextDelta(text.ToString()) { MessageId = messageId };
            _streamedText = false;
        }

        foreach (var block in blocks)
            yield return block;

        if (plan is not null)
            yield return plan;

        // Emit UsageLive if usage is present
        if (own && msg.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
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

    private IEnumerable<AgentStreamEvent> ReadUser(JsonElement root, string? parentCallId)
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

            if (parentCallId is null && callId is not null) _pendingCalls.Remove(callId);

            var isError = block.TryGetProperty("is_error", out var isErrorEl) && isErrorEl.ValueKind == JsonValueKind.True;

            string contentStr = string.Empty;
            if (block.TryGetProperty("content", out var contentEl))
                contentStr = contentEl.ValueKind == JsonValueKind.String
                    ? contentEl.GetString() ?? string.Empty
                    : StreamJsonFields.ConcatTextBlocks(contentEl); // array → concat text blocks; else ""

            yield return new ToolResult(callId, contentStr, isError) { ParentCallId = parentCallId };
        }
    }

    // ── result (terminal) ────────────────────────────────────────────────────

    // the result's own list of the tool calls its harness denied
    private static IEnumerable<AgentToolDenied> ReadDenials(JsonElement root)
    {
        if (WireJson.Array(root, "permission_denials") is not { } list) yield break;
        foreach (var entry in list.EnumerateArray())
        {
            if (WireJson.String(entry, "tool_name") is { Length: > 0 } tool)
                yield return new AgentToolDenied(tool, WireJson.String(entry, "tool_use_id"), WireJson.String(entry, "reason"));
        }
    }

    // the result's usage by model; cost is in the report and is deliberately not carried
    private AgentModelUsage? ReadModelUsage(JsonElement root)
    {
        if (WireJson.Object(root, "modelUsage") is not { } byModel) return null;
        var entries = new List<AgentModelUsageEntry>();
        foreach (var model in byModel.EnumerateObject())
        {
            if (model.Value.ValueKind != JsonValueKind.Object) continue;
            var row = model.Value;
            entries.Add(new AgentModelUsageEntry(
                model.Name, WireJson.Int64(row, "inputTokens"), WireJson.Int64(row, "outputTokens"),
                WireJson.Int64(row, "cacheReadInputTokens"), WireJson.Int64(row, "cacheCreationInputTokens"),
                WireJson.Int64(row, "contextWindow"), string.Equals(model.Name, _model, StringComparison.Ordinal)));
        }
        return entries.Count == 0 ? null : new AgentModelUsage(entries, _time.GetUtcNow());
    }

    private IEnumerable<AgentStreamEvent> ReadResult(JsonElement root)
    {
        _readResult = true;
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

        foreach (var denied in ReadDenials(root)) yield return denied;
        if (ReadModelUsage(root) is { } byModel) yield return byModel;

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
