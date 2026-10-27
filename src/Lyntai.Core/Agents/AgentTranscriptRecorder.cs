using System.Runtime.CompilerServices;
using System.Text;

namespace Lyntai.Agents;

/// <summary>Folds the agent event stream into an <see cref="AgentTranscript"/>, so a host does not have to.
/// Call <see cref="BeginTurn"/> with the options a turn is sent with, then <see cref="Add"/> each event of its
/// stream — or wrap the session in a <see cref="RecordingAgentSession"/>, which does both.
/// <para>One recorder is one conversation, one turn at a time: beginning a turn closes the one before it, with
/// no terminal if the stream never gave one. The recorder keeps what a brief needs and nothing else: the host's
/// words verbatim, the agent's last message, a count per tool, the last plan and the session id. Thinking and
/// diagnostics are not kept. Where the transcript is stored is the host's.</para>
/// <para>Safe to call from several threads; the events of a turn must still arrive in the order they were sent.</para></summary>
public sealed class AgentTranscriptRecorder
{
    private readonly object _gate = new();
    private readonly List<TranscriptTurn> _turns = [];
    private OpenTurn? _open;

    /// <summary>Start recording a turn that is about to run. A turn still open is closed as it stands.</summary>
    /// <param name="source">Who runs the turn.</param>
    /// <param name="options">What the turn is sent with; its prompt and system prompt are recorded verbatim.</param>
    public void BeginTurn(AgentConversationSource source, AgentSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.ProviderId, nameof(source));
        ArgumentException.ThrowIfNullOrWhiteSpace(source.DoorId, nameof(source));
        lock (_gate)
        {
            CloseOpen();
            _open = new OpenTurn(source, options.Prompt, options.SystemPrompt);
        }
    }

    /// <summary>Fold one event of the open turn into it. A terminal <see cref="SessionEnded"/> closes the turn.</summary>
    /// <exception cref="InvalidOperationException">No turn is open: <see cref="BeginTurn"/> comes first.</exception>
    public void Add(AgentStreamEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        lock (_gate)
        {
            var open = _open ?? throw new InvalidOperationException(
                "No turn is open: call BeginTurn with the turn's options before adding its events.");
            open.Fold(e);
            if (e is SessionEnded) CloseOpen();
        }
    }

    /// <summary>The conversation so far. A turn still open appears as it stands, with no terminal.</summary>
    public AgentTranscript Transcript
    {
        get
        {
            lock (_gate)
                return new AgentTranscript(_open is null ? [.. _turns] : [.. _turns, _open.Build()]);
        }
    }

    private void CloseOpen()
    {
        if (_open is null) return;
        _turns.Add(_open.Build());
        _open = null;
    }

    private sealed class OpenTurn(AgentConversationSource source, string prompt, string? systemPrompt)
    {
        private readonly List<ToolUse> _tools = [];
        private readonly Dictionary<string, ToolUse> _byCallId = new(StringComparer.Ordinal);
        private readonly StringBuilder _streamed = new();
        private ToolUse? _lastCall;
        private IReadOnlyList<PlanEntry>? _plan;
        private string? _sessionId;
        private string? _finalText;
        private SessionEnded? _ended;

        public void Fold(AgentStreamEvent e)
        {
            switch (e)
            {
                case SessionStarted s:
                    // A second start in one turn is a retried attempt: its words replace the failed one's.
                    _streamed.Clear();
                    _sessionId = s.SessionId;
                    break;
                case TextDelta t:
                    _streamed.Append(t.Text);
                    break;
                case ToolCall c:
                    _streamed.Clear();
                    var use = Tool(c.Name);
                    use.Calls++;
                    _lastCall = use;
                    if (c.CallId is not null) _byCallId[c.CallId] = use;
                    break;
                case ToolResult r:
                    _streamed.Clear();
                    var owner = r.CallId is not null && _byCallId.TryGetValue(r.CallId, out var found) ? found : _lastCall;
                    if (r.IsError && owner is not null) owner.Failed++;
                    break;
                case PlanUpdated p:
                    _plan = p.Entries;
                    break;
                case SessionEnded end:
                    _ended = end;
                    _finalText = end.FinalText;
                    if (!string.IsNullOrEmpty(end.SessionId)) _sessionId = end.SessionId;
                    break;
            }
        }

        private ToolUse Tool(string name)
        {
            var existing = _tools.Find(t => string.Equals(t.Name, name, StringComparison.Ordinal));
            if (existing is not null) return existing;
            var created = new ToolUse(name);
            _tools.Add(created);
            return created;
        }

        public TranscriptTurn Build()
        {
            var message = !string.IsNullOrWhiteSpace(_finalText) ? _finalText
                : _streamed.Length > 0 && !string.IsNullOrWhiteSpace(_streamed.ToString()) ? _streamed.ToString()
                : null;
            return new TranscriptTurn
            {
                Source = source,
                Prompt = prompt,
                SystemPrompt = systemPrompt,
                LastMessage = message,
                Tools = [.. _tools.Select(t => new ToolUseSummary(t.Name, t.Calls, t.Failed))],
                Plan = _plan,
                SessionId = _sessionId,
                Verdict = _ended?.Verdict,
                FailureKind = _ended?.Failure?.Kind,
            };
        }
    }

    private sealed class ToolUse(string name)
    {
        public string Name { get; } = name;
        public int Calls { get; set; }
        public int Failed { get; set; }
    }
}

/// <summary>Records every turn that passes through it into an <see cref="AgentTranscriptRecorder"/>, and changes
/// nothing about the stream: each event is forwarded as it arrives. Build one by hand for a conversation — a
/// transcript belongs to one conversation, so the library registers none for a whole container.
/// <para>Stack it over a <see cref="RetryingAgentSession"/> to record the turn the caller sees (a retried turn
/// is then one turn, with its attempts' tools counted together), or under one to record each attempt.</para></summary>
public sealed class RecordingAgentSession : IAgentSession
{
    private readonly IAgentSession _inner;
    private readonly AgentTranscriptRecorder _recorder;
    private readonly AgentConversationSource _source;

    /// <param name="inner">The session to record.</param>
    /// <param name="recorder">Where the turns are folded.</param>
    /// <param name="source">Who the session is, as the host names it.</param>
    public RecordingAgentSession(IAgentSession inner, AgentTranscriptRecorder recorder, AgentConversationSource source)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(source);
        _inner = inner;
        _recorder = recorder;
        _source = source;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(
        AgentSessionOptions options, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        _recorder.BeginTurn(_source, options);
        await foreach (var e in _inner.StreamAsync(options, ct).WithCancellation(ct).ConfigureAwait(false))
        {
            _recorder.Add(e);
            yield return e;
        }
    }
}
