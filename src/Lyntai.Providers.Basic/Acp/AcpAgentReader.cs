using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Processes;
using Lyntai.Providers.Basic;
using Lyntai.Providers.ClaudeCli;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Providers.Acp;

/// <summary>One ACP turn, as a client: feed it each line the agent writes with <see cref="Read"/> and it yields
/// the events, writing the client's next frames to the outbox as it goes — <c>initialize</c>, then
/// <c>session/new</c>, <c>session/load</c> or <c>session/resume</c>, then — for a <see cref="AgentSessionOptions.Model"/> — the
/// <c>session/set_config_option</c> of the agent's model option, then <c>session/prompt</c>. Tolerant: a line
/// that is not JSON, or is JSON of an unexpected shape, yields nothing and never throws.
/// <para><b>The mapping.</b> <c>agent_message_chunk</c> is <see cref="TextDelta"/>, <c>agent_thought_chunk</c>
/// <see cref="Thinking"/>, <c>plan</c> <see cref="PlanUpdated"/>, <c>tool_call</c> a <see cref="ToolCall"/> and a
/// <c>tool_call_update</c> that has finished a <see cref="ToolResult"/>, both carrying the update's own object.
/// The prompt's <c>usage</c> is <see cref="UsageFinal"/> and <see cref="UsageReading"/>; its <c>stopReason</c> and
/// any error end the turn. Everything else — a user message chunk, the commands and mode and config updates, a
/// <c>usage_update</c>'s rate-limit meta read by an extension — is no event but a <c>usage_update</c> is a
/// <see cref="ContextReading"/>, and nothing is inferred.</para>
/// <para><b>A load replays the conversation as updates before it answers</b>; they are dropped, so a turn's events
/// are the prompt's alone.</para></summary>
internal sealed class AcpAgentReader
{
    private const int InitializeId = 1;
    private const int OpenId = 2;
    private const int PromptId = 3;
    private const int SetModelId = 4;
    private const int SetModeId = 5;
    private const int SetEffortId = 6;
    private const int MaxOfferedNamed = 25;

    // a kept run's prompts count from here, so no id of the handshake's own can be read as one
    private const int KeptPromptBase = 100;

    private enum Phase { Initializing, Opening, Configuring, Prompting, Kept, Done }

    private readonly AgentSessionOptions _options;
    private readonly string _cwd;
    private readonly string _clientVersion;
    private readonly ChannelWriter<string> _outbox;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly bool _kept;
    private readonly AcpTurnState _single = new();
    private readonly AcpTurnState _idle = new();
    private readonly List<AcpTurnState> _outstanding = [];
    private AcpTurnState _current;
    private AcpTurnState? _frameTurn;
    private int _nextPrompt = KeptPromptBase;
    private bool _systemPending;
    private Phase _phase = Phase.Initializing;
    private string? _sessionId;
    private bool _cancelSent;
    private readonly IReadOnlyList<IAcpExtensionReader> _readers;
    private AcpExtensionSet _extensions;
    private AcpModelOption? _modelOption;
    private AcpModelOption? _effortOption;
    private readonly Queue<(int Id, string Frame)> _steps = new();
    private int _pendingStep;
    private IReadOnlyList<string> _offeredModes = [];
    private readonly IAcpPermissionApprover? _approver;
    private readonly InactivityPause? _pause;
    private readonly TimeSpan _permissionWindow;
    private readonly ILogger _logger;
    private readonly List<HeldPermission> _held = [];
    private int _frames;
    private string _line = "";
    private int _promptFrame = -1;
    private volatile bool _promptAnswered;

    /// <param name="options">The turn.</param>
    /// <param name="cwd">The absolute working directory the session opens in.</param>
    /// <param name="clientVersion">What the client says its version is.</param>
    /// <param name="outbox">Where the client's frames go, one per line.</param>
    /// <param name="time">The clock a usage reading is stamped from and a sentence's time is read against.</param>
    /// <param name="extensions">The readers of the agent's own extensions.</param>
    /// <param name="approver">Who answers a write turn's permission requests; null = the tool policy does.</param>
    /// <param name="inactivity">The turn's inactivity window: half of it is what the approver has to answer, the
    /// rest is the agent's to act on a refusal before the runner's clock ends the process.</param>
    /// <param name="logger">Diagnostics; null = none.</param>
    /// <param name="pause">Given when the wait is the approver's: a request waits as long as the approver holds it, and the
    /// runner's inactivity window is paused for as long as any request is held. Null = half the window, then a refusal.</param>
    /// <param name="kept">True for a run kept across turns: the open ends idle instead of prompting, <see cref="TryPrompt"/> sends
    /// each prompt, and a prompt's answer ends that turn and nothing else. <see cref="ReadRun"/> reads, never <see cref="Read"/>.</param>
    public AcpAgentReader(
        AgentSessionOptions options, string cwd, string clientVersion, ChannelWriter<string> outbox, TimeProvider time,
        IReadOnlyList<IAcpExtensionReader> extensions, IAcpPermissionApprover? approver = null,
        TimeSpan inactivity = default, ILogger? logger = null, InactivityPause? pause = null,
        bool kept = false)
    {
        _kept = kept;
        _current = kept ? _idle : _single;
        _systemPending = kept && !string.IsNullOrEmpty(options.SystemPrompt);
        _approver = approver;
        _pause = pause;
        // no window to halve (a host that configured none, or a reader built without one): the request waits on the approver alone
        _permissionWindow = pause is null && inactivity > TimeSpan.Zero && inactivity < TimeSpan.FromMilliseconds(uint.MaxValue - 1) ? inactivity / 2 : Timeout.InfiniteTimeSpan;
        _logger = logger ?? NullLogger.Instance;
        _options = options;
        _cwd = cwd;
        _clientVersion = clientVersion;
        _outbox = outbox;
        _time = time;
        _readers = extensions;
        _extensions = new AcpExtensionSet(extensions, time);
    }

    /// <summary>The place of the prompt's frame among the frames the client has queued (0 is <c>initialize</c>); -1 until
    /// it is queued. A writer that has asked for the frame after it has written it.</summary>
    public int PromptFrame => Volatile.Read(ref _promptFrame);

    /// <summary>Whether the agent has answered <c>session/prompt</c>, with a result or an error: it read the prompt.</summary>
    public bool PromptAnswered => _promptAnswered;

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
            ReleaseHeld(answerCancelled: true);   // the protocol: a cancelled turn answers each pending request cancelled
            return true;
        }
    }

    /// <summary>The frames no case here took: a request that is not a permission request, a notification that is not a
    /// session update, and an update of a kind with no case.</summary>
    public IReadOnlyList<AgentUnreadFrame> Unread() => _single.Unread.Snapshot();

    /// <summary>One line the agent wrote, as 0..N events.</summary>
    public IReadOnlyList<AgentStreamEvent> Read(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return [];
        _line = line;
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

    /// <summary>One line the agent wrote to a kept run, as 0..N events each with the turn it belongs to. A null turn is the run's
    /// own: the open's events and its terminal. A frame no turn owns is dropped.</summary>
    public IReadOnlyList<(AcpTurnState? Turn, AgentStreamEvent Event)> ReadRun(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return [];
        _line = line;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var events = new List<AgentStreamEvent>();
            lock (_gate)
            {
                var open = _phase == Phase.Kept;
                _frameTurn = null;
                _current = _idle;
                ReadFrame(doc.RootElement, events);
                if (_frameTurn is null && open)
                {
                    return [];
                }

                var turn = _frameTurn;
                return [.. events.Select(e => (turn, e))];
            }
        }
        catch (Exception ex) when (WireJson.IsShapeFault(ex))
        {
            return [];
        }
    }

    /// <summary>Send a prompt on a kept run's idle process. Decided and sent under one lock.</summary>
    /// <param name="text">The prompt's words.</param>
    /// <param name="queuedWords">Whether the door takes a word on a busy run.</param>
    /// <param name="sinkFor">Builds what receives the turn's events, called under the lock with the turn.</param>
    /// <param name="turn">The turn the prompt opened; null when it was refused.</param>
    public AcpPromptRefusal TryPrompt(string text, bool queuedWords, Func<AcpTurnState, object?> sinkFor, out AcpTurnState? turn)
    {
        lock (_gate)
        {
            turn = null;
            if (!_kept || _phase != Phase.Kept) return AcpPromptRefusal.Over;
            if (_outstanding.Count > 0) return AcpPromptRefusal.Busy;

            // what the readers held from the last turn's updates (a rejected allowance report) is not this turn's failure
            if (_outstanding.Count == 0) _extensions = new AcpExtensionSet(_readers, _time);
            string[] blocks = _systemPending ? [_options.SystemPrompt!, text] : [text];
            _systemPending = false;
            turn = new AcpTurnState { Id = _nextPrompt++, FrameIndex = Volatile.Read(ref _frames) };
            turn.Sink = sinkFor(turn);
            _outstanding.Add(turn);
            Send(AcpFrames.Prompt(turn.Id, _sessionId!, blocks));
            return AcpPromptRefusal.None;
        }
    }

    /// <summary>Cancel the sent turn that runs. False, sending nothing, where none runs or a stop is already out.</summary>
    public bool TryCancelRun(out AcpTurnState? turn)
    {
        lock (_gate)
        {
            turn = _outstanding.Count == 1 ? _outstanding[0] : null;
            if (!_kept || _phase != Phase.Kept || turn is null || turn.CancelSent || _sessionId is null) return false;
            turn.CancelSent = true;
            Send(AcpFrames.Cancel(_sessionId));
            ReleaseHeld(answerCancelled: true);
            return true;
        }
    }

    /// <summary>Whether a sent turn is running, which is when the process's inactivity window counts.</summary>
    public bool SentRunning
    {
        get
        {
            lock (_gate) return _outstanding.Count > 0;
        }
    }

    /// <summary>The conversation's id once opened.</summary>
    public string? SessionId
    {
        get
        {
            lock (_gate) return _sessionId;
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

        if (method == "session/update" && (_phase == Phase.Prompting || _phase == Phase.Kept) &&
            WireJson.Object(root, "params") is { } parameters && ForThisSession(parameters) &&
            WireJson.Object(parameters, "update") is { } update)
        {
            if (_kept) RouteKept(update);
            ReadUpdate(update, events);
        }
        else if (method != "session/update")
            _current.Unread.Note("notification", method, _line);
    }

    // A kept run's update belongs to the oldest turn a prompt opened that is not answered; with none running, to no turn.
    private void RouteKept(JsonElement update)
    {
        var kind = WireJson.String(update, "sessionUpdate");
        var turn = _outstanding.Count > 0 ? _outstanding[0] : null;
        if (turn is null) return;
        _frameTurn = turn;
        _current = turn;
    }

    private bool ForThisSession(JsonElement parameters) =>
        string.Equals(WireJson.String(parameters, "sessionId"), _sessionId, StringComparison.Ordinal);

    // ── the client's side of the handshake ───────────────────────────────────

    private void ReadResponse(int id, JsonElement root, List<AgentStreamEvent> events)
    {
        var error = WireJson.Object(root, "error");
        var result = WireJson.Object(root, "result") ?? default;
        if (_kept && _phase == Phase.Kept)
        {
            if (id >= KeptPromptBase) ReadKeptAnswer(id, error, result, events);
            return;
        }

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

                // an open that offers nothing says nothing, so it adds no event
                var offered = AcpOffers.Read(result);
                if (offered.Options.Count > 0 || offered.Modes.Count > 0) events.Add(offered);

                if (_options.Model is { Length: > 0 } wanted)
                {
                    _modelOption = AcpModelOption.Read(result);
                    if (_modelOption is null)
                    {
                        End(events, ModelRefused($"the agent states no model option (a config option of category model), so model '{wanted}' cannot be set on it.", "acp.model.no-option"));
                        break;
                    }

                    if (_modelOption.Current != wanted)
                        _steps.Enqueue((SetModelId, AcpFrames.SetConfigOption(SetModelId, _sessionId, _modelOption.Id, wanted)));
                }

                if (!QueueModeAndEffort(result, events)) break;
                NextStep(events);
                break;

            case SetModelId when _phase == Phase.Configuring && _pendingStep == SetModelId:
                ReadModelSet(error, result, events);
                break;

            case SetModeId when _phase == Phase.Configuring && _pendingStep == SetModeId:
                ReadSetAnswer(error, "permission mode", _options.PermissionMode!, "acp.mode.set-refused", events);
                break;

            case SetEffortId when _phase == Phase.Configuring && _pendingStep == SetEffortId:
                if (error is null && AcpModelOption.Read(result, AcpModelOption.EffortCategory) is { Current: { } nowEffort }
                    && nowEffort != _options.Effort && _effortOption!.Offered.Contains(_options.Effort!))
                    End(events, Refused($"the agent answered effort '{nowEffort}' to a set of '{_options.Effort}', which it lists.", AgentProvider.OptionRefusedSubtype));
                else
                    ReadSetAnswer(error, "effort", _options.Effort!, "acp.effort.set-refused", events);
                break;

            case PromptId when _phase == Phase.Prompting:
                _promptAnswered = true;
                EndPrompt(error, result, events);
                break;
        }
    }

    // A kept run's prompt is answered on its own: its turn ends, the process and the others go on. An earlier prompt answered
    // end_turn with every count 0 while a later word is out is the fold the agent makes when it takes that word (recorded).
    private void ReadKeptAnswer(int id, JsonElement? error, JsonElement result, List<AgentStreamEvent> events)
    {
        var at = _outstanding.FindIndex(turn => turn.Id == id);
        if (at < 0) return;
        var answered = _outstanding[at];
        var later = at < _outstanding.Count - 1;
        _outstanding.RemoveAt(at);
        answered.Answered = true;
        _current = answered;
        _frameTurn = answered;
        EndPrompt(error, result, events, later);
    }

    // A mode is set through session/set_mode with an id the open's modes answer offers, an effort through the config option
    // of its category, and both are the agent's to accept: an agent that offers neither ends the turn before the prompt.
    private bool QueueModeAndEffort(JsonElement result, List<AgentStreamEvent> events)
    {
        if (_options.PermissionMode is { Length: > 0 } mode)
        {
            var modes = WireJson.Object(result, "modes");
            _offeredModes = modes is { } m && WireJson.Array(m, "availableModes") is { } list
                ? [.. list.EnumerateArray().Select(x => WireJson.String(x, "id")).OfType<string>()]
                : [];
            if (!_offeredModes.Contains(mode, StringComparer.Ordinal))
            {
                End(events, Refused(_offeredModes.Count == 0
                    ? $"the agent states no modes, so permission mode '{mode}' cannot be set on it."
                    : $"the agent does not offer permission mode '{mode}'. It offers: {string.Join(", ", _offeredModes.Take(MaxOfferedNamed))}.",
                    AgentProvider.OptionRefusedSubtype));
                return false;
            }

            if (modes is { } current && WireJson.String(current, "currentModeId") != mode)
                _steps.Enqueue((SetModeId, AcpFrames.SetMode(SetModeId, _sessionId!, mode)));
        }

        if (_options.Effort is { Length: > 0 } effort)
        {
            _effortOption = AcpModelOption.Read(result, AcpModelOption.EffortCategory);
            if (_effortOption is null)
            {
                End(events, Refused($"the agent states no effort option (a config option of category {AcpModelOption.EffortCategory}), so effort '{effort}' cannot be set on it.",
                    AgentProvider.OptionRefusedSubtype));
                return false;
            }

            if (_effortOption.Current != effort)
                _steps.Enqueue((SetEffortId, AcpFrames.SetConfigOption(SetEffortId, _sessionId!, _effortOption.Id, effort)));
        }
        return true;
    }

    // the next set the turn needs, or the prompt
    private void NextStep(List<AgentStreamEvent> events)
    {
        if (!_steps.TryDequeue(out var step))
        {
            StartPrompt(events);
            return;
        }

        _phase = Phase.Configuring;
        _pendingStep = step.Id;
        Send(step.Frame);
    }

    // the agent's answer to a set is the verdict: an error refuses it, unless the agent states a limit or a sign-in there
    private void ReadSetAnswer(JsonElement? error, string what, string wanted, string by, List<AgentStreamEvent> events)
    {
        if (error is { } setError)
        {
            var failure = AcpFailure.FromError(setError, _extensions, _time, out var words);
            if (failure.Kind != AgentFailureKind.Fatal) End(events, FailedByError(setError));
            else End(events, Refused($"the agent refused {what} '{wanted}': {words}", AgentProvider.OptionRefusedSubtype));
            return;
        }

        NextStep(events);
    }

    // The session is announced only once it is ready to be prompted, so a turn refused before this point shows the
    // caller nothing and its delivery reads NeverSent.
    private void StartPrompt(List<AgentStreamEvent> events)
    {
        events.Add(new SessionStarted(_sessionId!));
        if (_kept)
        {
            _phase = Phase.Kept;
            return;
        }

        _phase = Phase.Prompting;
        string[] blocks = string.IsNullOrEmpty(_options.SystemPrompt)
            ? [_options.Prompt]
            : [_options.SystemPrompt, _options.Prompt];
        Volatile.Write(ref _promptFrame, Volatile.Read(ref _frames));
        Send(AcpFrames.Prompt(PromptId, _sessionId!, blocks));
    }

    // The agent's answer to session/set_config_option is the verdict on the model: an error refuses it, and so does
    // an answer that puts another value of the agent's own list in its place. An id the list does not name is the
    // agent's to accept and to spell (claude-agent-acp answers an alias for a full id), so it is never judged by the list.
    private void ReadModelSet(JsonElement? error, JsonElement result, List<AgentStreamEvent> events)
    {
        var wanted = _options.Model!;
        if (error is { } setError)
        {
            // a limit or a sign-in the agent states here is that, and not a refusal of the model
            var failure = AcpFailure.FromError(setError, _extensions, _time, out var words);
            if (failure.Kind != AgentFailureKind.Fatal)
            {
                End(events, FailedByError(setError));
                return;
            }

            var details = WireJson.Object(setError, "data") is { } data ? WireJson.String(data, "details") : null;
            End(events, ModelRefused(
                $"the agent refused model '{wanted}': {(string.IsNullOrEmpty(details) ? words : $"{words}: {details}")}. {OfferedWords()}",
                "acp.model.set-refused"));
            return;
        }

        if (AcpModelOption.Read(result) is { Current: { } now } && now != wanted && _modelOption!.Offered.Contains(wanted))
        {
            End(events, ModelRefused($"the agent answered model '{now}' to a set of '{wanted}', which it lists. {OfferedWords()}", "acp.model.not-taken"));
            return;
        }

        NextStep(events);
    }

    private string OfferedWords()
    {
        var offered = _modelOption!.Offered;
        return offered.Count == 0
            ? "It lists no model."
            : $"It offers: {string.Join(", ", offered.Take(MaxOfferedNamed))}{(offered.Count > MaxOfferedNamed ? ", …" : "")}.";
    }

    private static SessionEnded ModelRefused(string why, string by) =>
        new(ProviderVerdict.Unsupported, true, AgentProvider.OptionRefusedSubtype, null, null, why)
        {
            Failure = StreamJsonFailure.ModelRefused(by),
        };

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
    // once and never for good, unless a write turn has an approver, which answers instead. An agent in a mode that
    // asks for nothing is gated by its mode, not by this.
    private void ReadRequest(string method, JsonElement id, JsonElement root)
    {
        if (method != "session/request_permission")
        {
            _current.Unread.Note("request", method, _line);
            Send(AcpFrames.MethodNotFound(id, method));
            return;
        }

        var parameters = WireJson.Object(root, "params");
        var offered = parameters is { } p ? OfferedOptions(p) : [];
        if (_approver is null || _options.ToolPolicy == AgentToolPolicy.ReadOnly || _phase != (_kept ? Phase.Kept : Phase.Prompting))
        {
            Send(PermissionAnswer(id, _options.ToolPolicy == AgentToolPolicy.ReadOnly ? AcpPermissionAnswer.Deny : AcpPermissionAnswer.Approve, offered));
            return;
        }

        var toolCall = parameters is { } q && WireJson.Object(q, "toolCall") is { } call ? call.GetRawText() : "{}";
        Hold(new HeldPermission(id.Clone(), new CancellationTokenSource(_permissionWindow, _time), _pause?.Hold()),
            new AcpPermissionRequest(_sessionId!, toolCall, offered));
    }

    private static IReadOnlyList<AcpPermissionOption> OfferedOptions(JsonElement parameters)
    {
        var offered = new List<AcpPermissionOption>();
        if (WireJson.Array(parameters, "options") is not { } options) return offered;
        foreach (var option in options.EnumerateArray())
        {
            if (WireJson.String(option, "optionId") is { } optionId)
                offered.Add(new AcpPermissionOption(optionId, WireJson.String(option, "name") ?? "", WireJson.String(option, "kind") ?? ""));
        }
        return offered;
    }

    // The agent's options hold what was asked: approve is the allow_once, deny the reject_once and then reject_always, a
    // named id only when offered. No such option, and the protocol's own way to say no is the cancelled outcome.
    private static string PermissionAnswer(JsonElement id, AcpPermissionAnswer? answer, IReadOnlyList<AcpPermissionOption> offered)
    {
        string[] kinds = answer is { Approves: true } ? ["allow_once"] : ["reject_once", "reject_always"];
        var chosen = answer?.OptionId is { } named && offered.Any(o => o.OptionId == named)
            ? named
            : kinds.Select(kind => offered.FirstOrDefault(o => o.Kind == kind)?.OptionId).FirstOrDefault(optionId => optionId is not null);
        return chosen is null ? AcpFrames.PermissionCancelled(id) : AcpFrames.PermissionSelected(id, chosen);
    }

    // ── a request held for the host ──────────────────────────────────────────

    // One held request is answered once: whichever of the approver, the window, the caller's cancel or the turn's end
    // takes it out of _held first. The approver runs off the reading loop and off the lock, since it may block, and
    // its token is cancelled outside the lock too, since a host's callback on it is not ours.
    private void Hold(HeldPermission held, AcpPermissionRequest request)
    {
        _held.Add(held);
        var token = held.Window.Token;
        _ = Task.Run(async () =>
        {
            AcpPermissionAnswer? answer = null;
            try
            {
                answer = await _approver!.ApproveAsync(request, token).WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // the window passed, or the turn went away: a refusal, or nothing to send
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "an ACP permission approver failed; the request is refused");
            }

            lock (_gate)
            {
                if (!_held.Remove(held)) return;
                Send(PermissionAnswer(held.Id, answer, request.Options));
            }
            held.Pause?.Dispose();
            held.Window.Dispose();
        });
    }

    private void ReleaseHeld(bool answerCancelled)
    {
        var released = _held.ToList();
        _held.Clear();
        if (answerCancelled)
        {
            foreach (var held in released) Send(AcpFrames.PermissionCancelled(held.Id));
        }
        if (released.Count == 0) return;
        _ = Task.Run(() =>
        {
            foreach (var held in released)
            {
                held.Pause?.Dispose();
                held.Window.Cancel();
                held.Window.Dispose();
            }
        });
    }

    private sealed record HeldPermission(JsonElement Id, CancellationTokenSource Window, IDisposable? Pause);

    // ── the prompt's updates ─────────────────────────────────────────────────

    private void ReadUpdate(JsonElement update, List<AgentStreamEvent> events)
    {
        switch (WireJson.String(update, "sessionUpdate"))
        {
            case "agent_message_chunk" when ChunkText(update) is { } text:
                // a stamped chunk is a child's: it is not the session's answer
                if (ParentOf(update) is not { } textParent) _current.Answer.Append(text);
                events.Add(new TextDelta(text) { ParentCallId = ParentOf(update) });
                break;

            case "agent_thought_chunk" when ChunkText(update) is { } thought:
                events.Add(new Thinking(thought) { ParentCallId = ParentOf(update) });
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

            case "usage_update":
                AddContext(update, events);
                break;

            // known updates that carry nothing a turn reports
            case "user_message_chunk" or "available_commands_update" or "current_mode_update" or "config_option_update"
                or "session_info_update":
                break;

            case { } other:
                _current.Unread.Note("session/update", other, _line);
                break;
        }

        _extensions.ReadUpdate(update, events);
    }

    private void AddContext(JsonElement update, List<AgentStreamEvent> events)
    {
        long? used = WireJson.Int64(update, "used"), size = WireJson.Int64(update, "size");
        if (used is not null || size is not null) events.Add(new ContextReading(used, size, _time.GetUtcNow()));
    }

    // claude-agent-acp stamps what a child it did not declare as a session sends on the root session with the call that spawned it
    private static string? ParentOf(JsonElement update) =>
        WireJson.Object(update, "_meta") is { } meta && WireJson.Object(meta, "claudeCode") is { } claude
            ? WireJson.String(claude, "parentToolUseId")
            : null;

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
        if (_current.StartedTools.Add(id)) events.Add(new ToolCall(ToolName(update), update.GetRawText(), id) { ParentCallId = ParentOf(update) });
        if (IsFinished(update, out var failed)) events.Add(new ToolResult(id, update.GetRawText(), failed) { ParentCallId = ParentOf(update) });
    }

    private void ReadToolUpdate(JsonElement update, List<AgentStreamEvent> events)
    {
        if (WireJson.String(update, "toolCallId") is not { } id) return;
        if (!IsFinished(update, out var failed)) return;
        if (_current.StartedTools.Add(id)) events.Add(new ToolCall(ToolName(update), update.GetRawText(), id) { ParentCallId = ParentOf(update) });   // never seen start: still visible
        events.Add(new ToolResult(id, update.GetRawText(), failed) { ParentCallId = ParentOf(update) });
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

    private void EndPrompt(JsonElement? error, JsonElement result, List<AgentStreamEvent> events, bool laterOutstanding = false)
    {
        if (error is { } promptError)
        {
            End(events, FailedByError(promptError));
            return;
        }

        if (WireJson.Object(result, "usage") is { } usage) AddUsage(usage, events);

        // codex-acp ends a failed turn end_turn and says the failure only as a field of this answer (when the client
        // declared it): a reader states it, whatever the stop reason
        var stopReason = WireJson.String(result, "stopReason");
        if (_extensions.ReadTurnEnd(result) is { Count: > 0 } stated)
        {
            var failure = AcpFailure.FromStatedEnd(stated, _extensions, _time, out var statedWords);
            End(events, new SessionEnded(AcpFailure.VerdictOf(failure, statedWords), true, "session-failure", _sessionId, null, statedWords)
                { Failure = failure });
            return;
        }

        switch (stopReason)
        {
            case "end_turn":
                End(events, new SessionEnded(ProviderVerdict.Ok, false, null,
                    _sessionId, _current.Answer.Length > 0 ? _current.Answer.ToString() : null, null));
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
        var failure = AcpFailure.FromError(error, _extensions, _time, out var words);
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
        if (_kept && _phase == Phase.Kept)
        {
            // a kept run's turn ends alone: the process stays up, and a request nobody is left to answer is released
            if (_outstanding.Count == 0) ReleaseHeld(answerCancelled: false);
            return;
        }

        _phase = Phase.Done;
        ReleaseHeld(answerCancelled: false);
        _outbox.TryComplete();
    }

    private void Send(string frame)
    {
        if (_outbox.TryWrite(frame)) Interlocked.Increment(ref _frames);
    }
}
