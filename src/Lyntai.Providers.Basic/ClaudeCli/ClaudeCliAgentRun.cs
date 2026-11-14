using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Inference.Cli;
using Lyntai.Processes;
using Lyntai.Providers.Basic;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>An <see cref="IAgentRun"/> over the <c>claude</c> CLI with its stdin held open (<c>-p --input-format stream-json</c>): one
/// process, a user line per turn, each turn ended by its <c>result</c>. The open is the spawn, so the run's session id is null until
/// the CLI's first <c>system/init</c>, which it says with the first turn. A pump reads the CLI's lines and hands them to the turn that
/// runs. With none running: what the CLI says before the first line leads the first turn, a task's line waits for the next turn, the
/// CLI's own speech is unread on the next turn and refuses a line until a <c>result</c> closes it, and anything else is dropped. The
/// process's inactivity window counts only while a sent turn runs.</summary>
internal sealed class ClaudeCliAgentRun : IAgentRun
{
    /// <summary>How long the CLI has to answer an interrupt, or to leave once its stdin closes, before it is killed.</summary>
    internal static readonly TimeSpan DefaultStopGrace = TimeSpan.FromSeconds(5);

    /// <summary>What a run needs of the door that opens it. <paramref name="TempFiles"/> are deleted when the run ends.</summary>
    internal sealed record Host(
        IProcessRunner Runner, string Exe, IReadOnlyList<string> Arguments, string? WorkingDirectory,
        IReadOnlyDictionary<string, string?>? Environment, TimeSpan Inactivity, TimeSpan StopGrace, TimeProvider Time,
        IReadOnlyList<string> TempFiles);

    private sealed class TurnSink
    {
        public TurnSink(int line, Func<int> written, Func<IReadOnlyList<AgentUnreadFrame>> unread)
        {
            Watch = new CliAgentLoop.TurnWatch(false, () => written() > line, () => Answered, unread);
            Turn = new AgentRunTurn(AgentRunTurnOrigin.Sent, Events.Reader.ReadAllAsync(), Ended.Task);
        }

        public CliAgentLoop.TurnWatch Watch { get; }

        public Channel<AgentStreamEvent> Events { get; } = Channel.CreateUnbounded<AgentStreamEvent>(new UnboundedChannelOptions { SingleReader = true });

        public TaskCompletionSource<SessionEnded> Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AgentRunTurn Turn { get; }

        // the CLI's result answers the line: it read it, whatever the turn showed before
        public bool Answered { get; set; }
    }

    // a stop is settled once the CLI has answered the interrupt AND the turn it cut has ended, in either order
    private sealed class Stop(string id)
    {
        public string Id { get; } = id;

        public bool Answered { get; set; }

        public bool TurnEnded { get; set; }
    }

    private readonly Host _host;
    private readonly StreamJsonAgentReader _reader;
    private readonly Channel<string> _outbox = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _kill = new();
    private readonly CancellationToken _killToken;
    private readonly InactivityPause _pause = new();
    private readonly object _gate = new();
    private readonly object _windowGate = new();
    // never written: no record shows the CLI starting a turn of its own on held stdin, so none is offered
    private readonly Channel<AgentRunTurn> _woken = Channel.CreateBounded<AgentRunTurn>(1);
    private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<SessionEnded> _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private List<AgentStreamEvent> _held = [];
    private TurnSink? _current;
    private Stop? _stop;
    private SessionEnded? _cause;
    private IDisposable? _idleHold;
    private Task _pump = Task.CompletedTask;
    private string? _killReason;
    private int _queued;
    private int _written;
    private int _stops;
    private int _disposing;
    private bool _sent;
    private bool _speaking;

    private ClaudeCliAgentRun(Host host)
    {
        _killToken = _kill.Token;
        _host = host;
        _reader = new StreamJsonAgentReader(host.Time);
    }

    /// <summary>Spawn the CLI and return the run idle once the runner has started it, or already over when it could not.</summary>
    public static async Task<IAgentRun> OpenAsync(Host host, CancellationToken ct)
    {
        var run = new ClaudeCliAgentRun(host);
        run.Start();
        try
        {
            await run._opened.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            run.Kill("the open was cancelled");
            await run._pump.ConfigureAwait(false);
            throw;
        }

        return run;
    }

    private void Start()
    {
        Window();   // idle from the spawn: until a line is sent, the CLI's silence is the host's wait
        IAsyncEnumerable<string> lines;
        try
        {
            lines = _host.Runner.ExchangeLinesAsync(_host.Exe, _host.Arguments, Frames(_outbox.Reader.ReadAllAsync(CancellationToken.None)),
                _pause, _host.Inactivity, workingDirectory: _host.WorkingDirectory, environment: _host.Environment, ct: _killToken);
        }
        catch (NotSupportedException ex)
        {
            Over(CliAgentLoop.Refused("runner-unsupported", ex.Message));
            return;
        }

        _pump = Task.Run(() => PumpAsync(lines));
    }

    /// <inheritdoc />
    public string? SessionId => _reader.SessionId;

    /// <inheritdoc />
    public Task<SessionEnded> Ended => _ended.Task;

    /// <inheritdoc />
    public IAsyncEnumerable<AgentRunTurn> WokenTurns => _woken.Reader.ReadAllAsync();

    /// <inheritdoc />
    public Task<AgentRunTurn> SendAsync(string prompt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ct.ThrowIfCancellationRequested();
        AgentRunTurn turn;
        lock (_gate)
        {
            if (_cause is not null) return Refused(AgentRunSubtypes.Over, "the run is over.");
            if (_current is not null)
                return Refused(AgentRunSubtypes.Busy, "a turn is running, and this door takes no word into it: on its wire a word lands in the running turn or becomes the next one, and nothing tells which.");
            if (_stop is not null)
                return Refused(AgentRunSubtypes.Busy, "a stop is out: the stopped turn's end and the CLI's answer to the interrupt are both awaited.");
            if (_speaking)
                return Refused(AgentRunSubtypes.WokenRunning, "the CLI is speaking with no turn of the host's running, and no record shows a line sent into its own turn.");

            _reader.BeginTurn();
            var sink = new TurnSink(_queued, () => Volatile.Read(ref _written), _reader.Unread);
            foreach (var evt in _held)
            {
                sink.Watch.Carry(evt);   // seen before the line was written: shown, and no sign it was read
                sink.Events.Writer.TryWrite(evt);
            }

            _held = [];
            _sent = true;
            Volatile.Write(ref _current, sink);
            Write(UserLine(prompt));
            turn = sink.Turn;
        }

        Window();
        return Task.FromResult(turn);
    }

    /// <inheritdoc />
    public Task<bool> StopAsync(CancellationToken ct = default)
    {
        Stop stop;
        lock (_gate)
        {
            if (_cause is not null || _current is null || _stop is not null) return Task.FromResult(false);
            stop = _stop = new Stop($"lyntai-interrupt-{++_stops}");
            Write(InterruptLine(stop.Id));
        }

        // a CLI that never answers its interrupt ends the run, as the protocol door ends one that never answers its cancel
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_host.StopGrace, _host.Time, _killToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            bool answered;
            lock (_gate) answered = stop.Answered;
            if (!answered) Kill("the claude CLI did not answer its interrupt within its grace");
        });
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposing, 1) != 0) return;
        _outbox.Writer.TryComplete();   // stdin closes, which ends the CLI's session
        try
        {
            await _pump.WaitAsync(_host.StopGrace, _host.Time).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Kill("the claude CLI did not leave within its grace after the run was disposed");
            await _pump.ConfigureAwait(false);
        }

        Over(DisposedEnd());
    }

    // a dispose that had to kill the CLI still ends Disposed, as IAgentRun.Ended promises: the kill is the detail
    private SessionEnded DisposedEnd() => new(ProviderVerdict.Ok, false, AgentRunSubtypes.Disposed, SessionId, null, _killReason);

    // ── the wire ─────────────────────────────────────────────────────────────

    // RECORDED (a host's stream-json note, 2.1.281): no priority and no id, so the CLI takes it as the next turn's prompt
    private static string UserLine(string prompt) => new JsonObject
    {
        ["type"] = "user",
        ["message"] = new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = prompt }),
        },
    }.ToJsonString();

    // RECORDED (the same note): answered by a control_response naming the id, the cut turn ending on its own result
    private static string InterruptLine(string id) => new JsonObject
    {
        ["type"] = "control_request",
        ["request_id"] = id,
        ["request"] = new JsonObject { ["subtype"] = "interrupt" },
    }.ToJsonString();

    private void Write(string line)
    {
        if (_outbox.Writer.TryWrite(line)) _queued++;
    }

    // ── the pump ─────────────────────────────────────────────────────────────

    private async IAsyncEnumerable<string> Frames(IAsyncEnumerable<string> lines, [EnumeratorCancellation] CancellationToken ct = default)
    {
        _opened.TrySetResult();   // a runner asks for stdin only once it has started the process: the open is the spawn
        var index = 0;
        await foreach (var line in lines.WithCancellation(ct).ConfigureAwait(false))
        {
            yield return line;
            Volatile.Write(ref _written, ++index);
        }
    }

    private async Task PumpAsync(IAsyncEnumerable<string> lines)
    {
        SessionEnded ended;
        try
        {
            await foreach (var line in lines.ConfigureAwait(false))
            {
                lock (_gate) Route(line);
                Window();
            }

            ended = Volatile.Read(ref _disposing) == 1
                ? DisposedEnd()
                : new SessionEnded(ProviderVerdict.Failed, true, "process-exited", SessionId, null, "the claude CLI's process ended while the run was open.")
                    { Failure = new AgentFailure(AgentFailureKind.Fatal, "process.exited") };
        }
        catch (Exception ex)
        {
            ended = Fault(ex);
        }

        Over(ended);
    }

    // Under the gate, in the wire's order.
    private void Route(string line)
    {
        if (_cause is not null) return;
        var events = _reader.ReadRun(line, out var frame);
        if (frame.Kind == StreamJsonAgentReader.RunFrameKind.ControlResponse)
        {
            if (_stop is { } stop && string.Equals(frame.RequestId, stop.Id, StringComparison.Ordinal))
            {
                stop.Answered = true;
                Settle();
            }
            return;
        }

        if (_current is { } turn)
        {
            foreach (var evt in events)
            {
                if (evt is SessionEnded ended)
                {
                    turn.Answered = true;
                    EndTurn(turn, CliAgentLoop.Stamp(ended, turn.Watch));
                    return;
                }

                turn.Watch.See(evt);
                turn.Events.Writer.TryWrite(evt);
            }
            return;
        }

        switch (frame.Kind)
        {
            case StreamJsonAgentReader.RunFrameKind.Task:
                _held.AddRange(events);
                break;
            case StreamJsonAgentReader.RunFrameKind.OwnActivity:
                _reader.NoteUnread(frame, line);
                _speaking = true;
                break;
            case StreamJsonAgentReader.RunFrameKind.Result:
                _reader.NoteUnread(frame, line);
                _speaking = false;
                break;
            default:
                if (!_sent) _held.AddRange(events);   // the CLI's start, before the first line, is the first turn's
                break;
        }
    }

    private void EndTurn(TurnSink turn, SessionEnded ended)
    {
        Terminate(turn, ended);
        Volatile.Write(ref _current, null);
        _reader.EndTurn();
        if (_stop is { } stop)
        {
            stop.TurnEnded = true;
            Settle();
        }
    }

    private void Settle()
    {
        if (_stop is { Answered: true, TurnEnded: true }) _stop = null;
    }

    private static void Terminate(TurnSink turn, SessionEnded ended)
    {
        turn.Events.Writer.TryWrite(ended);
        turn.Events.Writer.TryComplete();
        turn.Ended.TrySetResult(ended);
    }

    private SessionEnded Fault(Exception ex)
    {
        if (Volatile.Read(ref _disposing) == 1) return DisposedEnd();
        if (_killReason is { } reason)
            return new SessionEnded(ProviderVerdict.Failed, true, AgentRunSubtypes.Over, SessionId, null, reason)
                { Failure = new AgentFailure(AgentFailureKind.Fatal, "run-killed") };
        if (CliFault.Classify(ex) is not { } fault)
            return new SessionEnded(ProviderVerdict.Failed, true, AgentRunSubtypes.Over, SessionId, null, "the run was cancelled.")
                { Failure = new AgentFailure(AgentFailureKind.Fatal, "run-cancelled") };
        return new SessionEnded(fault.Verdict, true, ex is ProcessTimeoutException ? "timeout" : null, SessionId, null, fault.Detail)
            { Failure = CliAgentLoop.FaultFailure(ex, fault.Verdict) };
    }

    private void Kill(string reason)
    {
        _killReason ??= reason;
        try
        {
            _kill.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // the run is already over
        }
    }

    // ── the window and the end ───────────────────────────────────────────────

    // The inactivity window counts while a sent turn runs and is held paused otherwise.
    private void Window()
    {
        lock (_windowGate)
        {
            if (Volatile.Read(ref _cause) is not null) return;
            var running = Volatile.Read(ref _current) is not null;
            if (running && _idleHold is not null)
            {
                _idleHold.Dispose();
                _idleHold = null;
            }
            else if (!running && _idleHold is null)
                _idleHold = _pause.Hold();
        }
    }

    private void Over(SessionEnded ended)
    {
        lock (_gate)
        {
            if (_cause is not null) return;
            Volatile.Write(ref _cause, ended);
            if (_current is { } open)
            {
                Terminate(open, KeptRuns.Cut(ended, open.Watch));
                Volatile.Write(ref _current, null);
            }
            _stop = null;
        }

        lock (_windowGate)
        {
            _idleHold?.Dispose();
            _idleHold = null;
        }

        _outbox.Writer.TryComplete();
        _woken.Writer.TryComplete();
        foreach (var path in _host.TempFiles) OwnerOnlyTempFile.TryDelete(path);   // the MCP document lives as long as the run
        _ended.TrySetResult(ended);
        _opened.TrySetResult();
    }

    private Task<AgentRunTurn> Refused(string subtype, string why) => Task.FromResult(KeptRuns.Refused(subtype, why, SessionId));
}
