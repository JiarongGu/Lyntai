using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Providers.Basic;
using Lyntai.Providers.ClaudeCli;

namespace Lyntai.Providers.Acp;

/// <summary>One ACP turn, as a client: feed it each line the agent writes with <see cref="Read"/> and it yields
/// the events, writing the client's next frames to the outbox as it goes — <c>initialize</c>, then
/// <c>session/new</c>, <c>session/load</c> or <c>session/resume</c>, then <c>session/prompt</c>. Tolerant: a line
/// that is not JSON, or is JSON of an unexpected shape, yields nothing and never throws.
/// <para><b>The mapping.</b> <c>agent_message_chunk</c> is <see cref="TextDelta"/>, <c>agent_thought_chunk</c>
/// <see cref="Thinking"/>, <c>plan</c> <see cref="PlanUpdated"/>, <c>tool_call</c> a <see cref="ToolCall"/> and a
/// <c>tool_call_update</c> that has finished a <see cref="ToolResult"/>, both carrying the update's own object.
/// The prompt's <c>usage</c> is <see cref="UsageFinal"/> and <see cref="UsageReading"/>; its <c>stopReason</c> and
/// any error end the turn. Everything else — a user message chunk, the commands and mode and config updates, a
/// <c>usage_update</c>'s context figures — is no event, and nothing is inferred.</para>
/// <para><b>A load replays the conversation as updates before it answers</b>; they are dropped, so a turn's events
/// are the prompt's alone.</para></summary>
internal sealed class AcpAgentReader
{
    private const int InitializeId = 1;
    private const int OpenId = 2;
    private const int PromptId = 3;

    private enum Phase { Initializing, Opening, Prompting, Done }

    private readonly AgentSessionOptions _options;
    private readonly string _cwd;
    private readonly string _clientVersion;
    private readonly ChannelWriter<string> _outbox;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly StringBuilder _answer = new();
    private readonly HashSet<string> _startedTools = new(StringComparer.Ordinal);
    private Phase _phase = Phase.Initializing;
    private string? _sessionId;
    private bool _cancelSent;
    private RejectedLimit? _rejected;
    private AcpTypedFailure? _typed;

    /// <param name="options">The turn.</param>
    /// <param name="cwd">The absolute working directory the session opens in.</param>
    /// <param name="clientVersion">What the client says its version is.</param>
    /// <param name="outbox">Where the client's frames go, one per line.</param>
    /// <param name="time">The clock a usage reading is stamped from and a sentence's time is read against.</param>
    public AcpAgentReader(AgentSessionOptions options, string cwd, string clientVersion, ChannelWriter<string> outbox, TimeProvider time)
    {
        _options = options;
        _cwd = cwd;
        _clientVersion = clientVersion;
        _outbox = outbox;
        _time = time;
    }

    /// <summary>Queue <c>initialize</c>, the first frame.</summary>
    public void Start() => Send(AcpFrames.Initialize(InitializeId, _clientVersion));

    /// <summary>Ask the agent to stop the running turn with <c>session/cancel</c>. False when no prompt is in
    /// flight, which leaves nothing to cancel but the process.</summary>
    public bool TryCancel()
    {
        lock (_gate)
        {
            if (_phase != Phase.Prompting || _cancelSent || _sessionId is null) return false;
            _cancelSent = true;
            Send(AcpFrames.Cancel(_sessionId));
            return true;
        }
    }

    /// <summary>One line the agent wrote, as 0..N events.</summary>
    public IReadOnlyList<AgentStreamEvent> Read(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return [];
        try
        {
            using var doc = JsonDocument.Parse(line);
            var events = new List<AgentStreamEvent>();
            lock (_gate) ReadFrame(doc.RootElement, events);
            return events;
        }
        catch (Exception ex) when (WireJson.IsShapeFault(ex))
        {
            return [];
        }
    }

    private void ReadFrame(JsonElement root, List<AgentStreamEvent> events)
    {
        if (_phase == Phase.Done || root.ValueKind != JsonValueKind.Object) return;

        var method = WireJson.String(root, "method");
        if (method is null)
        {
            if (root.TryGetProperty("id", out var responseId) && responseId.ValueKind == JsonValueKind.Number &&
                responseId.TryGetInt32(out var id))
                ReadResponse(id, root, events);
            return;
        }

        if (root.TryGetProperty("id", out var requestId) && requestId.ValueKind is JsonValueKind.Number or JsonValueKind.String)
        {
            ReadRequest(method, requestId, root);
            return;
        }

        if (method == "session/update" && _phase == Phase.Prompting &&
            WireJson.Object(root, "params") is { } parameters && ForThisSession(parameters) &&
            WireJson.Object(parameters, "update") is { } update)
            ReadUpdate(update, events);
    }

    private bool ForThisSession(JsonElement parameters) =>
        string.Equals(WireJson.String(parameters, "sessionId"), _sessionId, StringComparison.Ordinal);

    // ── the client's side of the handshake ───────────────────────────────────

    private void ReadResponse(int id, JsonElement root, List<AgentStreamEvent> events)
    {
        var error = WireJson.Object(root, "error");
        var result = WireJson.Object(root, "result") ?? default;
        switch (id)
        {
            case InitializeId when _phase == Phase.Initializing:
                if (error is { } initializeError) End(events, FailedByError(initializeError));
                else Open(AcpCapabilities.Read(result), events);
                break;

            case OpenId when _phase == Phase.Opening:
                if (error is { } openError)
                {
                    End(events, FailedByError(openError));
                    break;
                }

                _sessionId = _options.ResumeToken ?? WireJson.String(result, "sessionId");
                if (string.IsNullOrWhiteSpace(_sessionId))
                {
                    End(events, Failed("the agent opened a session and named none", "acp.session.id", AgentFailureKind.Fatal));
                    break;
                }

                events.Add(new SessionStarted(_sessionId));
                _phase = Phase.Prompting;
                string[] blocks = string.IsNullOrEmpty(_options.SystemPrompt)
                    ? [_options.Prompt]
                    : [_options.SystemPrompt, _options.Prompt];
                Send(AcpFrames.Prompt(PromptId, _sessionId, blocks));
                break;

            case PromptId when _phase == Phase.Prompting:
                EndPrompt(error, result, events);
                break;
        }
    }

    private void Open(AcpCapabilities agent, List<AgentStreamEvent> events)
    {
        if (agent.ProtocolVersion is { } version && version != AcpFrames.ProtocolVersion)
        {
            End(events, Refused($"the agent answers ACP protocol version {version}; this door speaks {AcpFrames.ProtocolVersion}.",
                AgentProvider.OptionRefusedSubtype));
            return;
        }

        var method = "session/new";
        if (_options.ResumeToken is not null)
        {
            if (agent.ResumeMethod is not { } resumeMethod)
            {
                End(events, Refused("the agent states neither sessionCapabilities.resume nor loadSession, so it cannot continue a conversation.",
                    AgentProvider.OptionRefusedSubtype));
                return;
            }
            method = resumeMethod;
        }

        if (AcpFrames.McpServers(_options.McpServers, agent, out var refusal) is not { } servers)
        {
            End(events, Refused(refusal!, AgentProvider.OptionRefusedSubtype));
            return;
        }

        _phase = Phase.Opening;
        Send(AcpFrames.Open(OpenId, method, _options.ResumeToken, _cwd, servers));
    }

    // A tool policy is a gate on what the agent ASKS to do: ReadOnly rejects every request, Write allows each one
    // once and never for good. An agent in a mode that asks for nothing is gated by its mode, not by this.
    private void ReadRequest(string method, JsonElement id, JsonElement root)
    {
        if (method != "session/request_permission")
        {
            Send(AcpFrames.MethodNotFound(id, method));
            return;
        }

        string[] wanted = _options.ToolPolicy == AgentToolPolicy.ReadOnly ? ["reject_once", "reject_always"] : ["allow_once"];
        string? chosen = null;
        if (WireJson.Object(root, "params") is { } parameters && WireJson.Array(parameters, "options") is { } options)
        {
            foreach (var kind in wanted)
            {
                chosen = options.EnumerateArray()
                    .Where(o => WireJson.String(o, "kind") == kind)
                    .Select(o => WireJson.String(o, "optionId"))
                    .FirstOrDefault(optionId => optionId is not null);
                if (chosen is not null) break;
            }
        }
        Send(chosen is null ? AcpFrames.PermissionCancelled(id) : AcpFrames.PermissionSelected(id, chosen));
    }

    // ── the prompt's updates ─────────────────────────────────────────────────

    private void ReadUpdate(JsonElement update, List<AgentStreamEvent> events)
    {
        switch (WireJson.String(update, "sessionUpdate"))
        {
            case "agent_message_chunk" when ChunkText(update) is { } text:
                _answer.Append(text);
                events.Add(new TextDelta(text));
                break;

            case "agent_thought_chunk" when ChunkText(update) is { } thought:
                events.Add(new Thinking(thought));
                break;

            case "plan":
                events.Add(new PlanUpdated(ReadPlan(update)));
                break;

            case "tool_call":
                ReadToolCall(update, events);
                break;

            case "tool_call_update":
                ReadToolUpdate(update, events);
                break;

            // the agent's own failure field, read for the turn's end; a warning is a notice, not an ending
            case "session_info_update" when AcpTypedFailure.Read(update) is { } typed:
                _typed = typed;
                break;

            // claude-agent-acp forwards the CLI's rate_limit_info here unchanged
            case "usage_update" when WireJson.Object(update, "_meta") is { } meta && WireJson.Object(meta, "_claude/rateLimit") is { } info:
                _rejected = StreamJsonFailure.ReadRejectionInfo(info) ?? _rejected;
                break;
        }
    }

    private static string? ChunkText(JsonElement update) =>
        WireJson.Object(update, "content") is { } content && WireJson.String(content, "type") == "text"
            ? WireJson.String(content, "text")
            : null;

    private static IReadOnlyList<PlanEntry> ReadPlan(JsonElement update)
    {
        var entries = new List<PlanEntry>();
        if (WireJson.Array(update, "entries") is not { } list) return entries;
        foreach (var entry in list.EnumerateArray())
        {
            if (WireJson.String(entry, "content") is { } content)
                entries.Add(new PlanEntry(content, WirePlan.Status(WireJson.String(entry, "status"))));
        }
        return entries;
    }

    // The tool step's NAME is the protocol's own kind (read, edit, execute…) and its payload the update's whole
    // object, so which tool ran is in the payload's title and rawInput, not in the name.
    private void ReadToolCall(JsonElement update, List<AgentStreamEvent> events)
    {
        if (WireJson.String(update, "toolCallId") is not { } id) return;
        if (_startedTools.Add(id)) events.Add(new ToolCall(ToolName(update), update.GetRawText(), id));
        if (IsFinished(update, out var failed)) events.Add(new ToolResult(id, update.GetRawText(), failed));
    }

    private void ReadToolUpdate(JsonElement update, List<AgentStreamEvent> events)
    {
        if (WireJson.String(update, "toolCallId") is not { } id) return;
        if (!IsFinished(update, out var failed)) return;
        if (_startedTools.Add(id)) events.Add(new ToolCall(ToolName(update), update.GetRawText(), id));   // never seen start: still visible
        events.Add(new ToolResult(id, update.GetRawText(), failed));
    }

    private static string ToolName(JsonElement update) =>
        WireJson.String(update, "kind") ?? WireJson.String(update, "title") ?? "tool";

    private static bool IsFinished(JsonElement update, out bool failed)
    {
        var status = WireJson.String(update, "status");
        failed = status == "failed";
        return status is "completed" or "failed";
    }

    // ── the end of the turn ──────────────────────────────────────────────────

    private void EndPrompt(JsonElement? error, JsonElement result, List<AgentStreamEvent> events)
    {
        if (error is { } promptError)
        {
            End(events, FailedByError(promptError));
            return;
        }

        if (WireJson.Object(result, "usage") is { } usage) AddUsage(usage, events);

        // codex-acp ends a failed turn end_turn and says the failure only as a field of this answer (when the client
        // declared it); an earlier update's may have been recovered from, so only this one ends the turn
        var typed = AcpTypedFailure.Read(result);
        var stopReason = WireJson.String(result, "stopReason");
        if (typed is { EndsTurn: true })
        {
            var failure = AcpFailure.FromTypedEnd(typed, _rejected, _time);
            End(events, new SessionEnded(AcpFailure.VerdictOf(failure, typed.Title), true, "session-failure", _sessionId, null, typed.Title)
                { Failure = failure });
            return;
        }

        switch (stopReason)
        {
            case "end_turn":
                End(events, new SessionEnded(ProviderVerdict.Ok, false, null, _sessionId, _answer.Length > 0 ? _answer.ToString() : null, null));
                break;
            case "refusal":
                End(events, new SessionEnded(ProviderVerdict.Refused, true, "refusal", _sessionId, null, "the agent refused the prompt (stopReason refusal)")
                    { Failure = new AgentFailure(AgentFailureKind.Fatal, "acp.stopReason=refusal") });
                break;
            default:
                // cancelled (the caller's, or the agent's own), max_tokens, max_turn_requests, or a word not in the schema
                End(events, Failed($"the turn ended with stopReason {stopReason ?? "(none)"}", $"acp.stopReason={stopReason}",
                    AgentFailureKind.Fatal, subtype: stopReason));
                break;
        }
    }

    // codex-acp's counts exclude what it read from a cache, claude's do not: the figures are the agent's own,
    // and only a count the agent sent is a number
    private void AddUsage(JsonElement usage, List<AgentStreamEvent> events)
    {
        long? input = WireJson.Int64(usage, "inputTokens"), output = WireJson.Int64(usage, "outputTokens");
        long? cacheRead = WireJson.Int64(usage, "cachedReadTokens"), cacheWrite = WireJson.Int64(usage, "cachedWriteTokens");
        if (input is not null && output is not null)
            events.Add(new UsageFinal(input.Value, output.Value, cacheRead ?? 0, cacheWrite ?? 0, Model: null));
        events.Add(new UsageReading(input, output, cacheRead, cacheWrite, UsageReading.Tokens, UsageScope.Turn, _time.GetUtcNow()));
    }

    private SessionEnded FailedByError(JsonElement error)
    {
        var failure = AcpFailure.FromError(error, _rejected, _typed, _time, out var words);
        return new SessionEnded(AcpFailure.VerdictOf(failure, words), true, "jsonrpc-error", _sessionId, null, words) { Failure = failure };
    }

    private SessionEnded Failed(string why, string by, AgentFailureKind kind, string? subtype = null) =>
        new(ProviderVerdict.Failed, true, subtype, _sessionId, null, why) { Failure = new AgentFailure(kind, by) };

    private SessionEnded Refused(string why, string subtype) =>
        new(ProviderVerdict.Unsupported, true, subtype, null, null, why)
        {
            Failure = new AgentFailure(AgentFailureKind.Fatal, "refused-after-initialize"),
        };

    // The terminal ends the conversation with the agent: stdin closes so it can go.
    private void End(List<AgentStreamEvent> events, SessionEnded terminal)
    {
        events.Add(terminal);
        _phase = Phase.Done;
        _outbox.TryComplete();
    }

    private void Send(string frame) => _outbox.TryWrite(frame);
}
