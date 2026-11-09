using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Inference.Cli;
using Lyntai.Processes;
using Lyntai.Providers.Basic;
using Microsoft.Extensions.Logging;

namespace Lyntai.Providers.Acp;

/// <summary>An <see cref="IAgentRun"/> over the Agent Client Protocol: one agent process, one <c>initialize</c> and one
/// <c>session/new</c>, then a <c>session/prompt</c> per turn. A pump reads the agent's lines and hands each event to the turn it
/// belongs to (<see cref="AcpAgentReader.ReadRun"/>); a turn is a channel the host reads and a task that completes with its
/// terminal. The process's inactivity window counts only while a sent turn runs: between turns, and during a woken turn, the run
/// holds it paused, since an idle agent is the host waiting and a woken turn's end is the host's bound
/// (<see cref="AgentRunOptions.WokenTurnQuiet"/>), never the runner's.</summary>
internal sealed class AcpAgentRun : IAgentRun
{
    /// <summary>What a run needs of the door that opens it.</summary>
    internal sealed record Host(
        IProcessRunner Runner, AcpAgentSession.AcpLaunch Launch, IReadOnlyList<string> Arguments, TimeSpan CancelGrace,
        TimeProvider Time, IReadOnlyList<IAcpExtensionReader> Extensions, IAcpPermissionApprover? Permissions,
        AcpPermissionWait PermissionWait, ILogger Logger, string ClientVersion);

    private sealed class TurnSink(AcpTurnState state, CliAgentLoop.TurnWatch watch)
    {
        public AcpTurnState State { get; } = state;
        public CliAgentLoop.TurnWatch Watch { get; } = watch;
        public Channel<AgentStreamEvent> Events { get; } = Channel.CreateUnbounded<AgentStreamEvent>(new UnboundedChannelOptions { SingleReader = true });
        public TaskCompletionSource<SessionEnded> Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AgentRunTurn Turn { get; set; } = null!;
        public int Terminated;
    }

    // a timer's due time is a uint of milliseconds (about 49.7 days)
    private static readonly TimeSpan MaxWokenTurnQuiet = TimeSpan.FromDays(49);

    private readonly Host _host;
    private readonly AgentRunOptions _run;
    private readonly bool _queuedWords;
    private readonly AcpAgentReader _reader;
    private readonly Channel<string> _outbox = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _kill = new();
    private readonly CancellationToken _killToken;
    private readonly InactivityPause _pause = new();
    private readonly object _windowGate = new();
    private readonly object _dispatch = new();
    private readonly object _liveGate = new();
    private readonly object _timerGate = new();
    private readonly List<TurnSink> _live = [];
    private readonly Channel<AgentRunTurn> _woken = Channel.CreateUnbounded<AgentRunTurn>(new UnboundedChannelOptions { SingleReader = true });
    private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<SessionEnded> _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private List<AgentStreamEvent>? _openEvents = [];
    private SessionEnded? _cause;
    private IDisposable? _idleHold;
    private ITimer? _quietTimer;
    private Task _pump = Task.CompletedTask;
    private string? _killReason;
    private int _written;
    private int _disposing;
    private bool _openOk;

    private AcpAgentRun(Host host, AgentSessionOptions options, AgentRunOptions run, bool queuedWords)
    {
        _killToken = _kill.Token;
        _host = host;
        _run = run;
        _queuedWords = queuedWords;
        _reader = new AcpAgentReader(options, host.Launch.Cwd, host.ClientVersion, _outbox.Writer, host.Time, host.Extensions,
            host.Permissions, host.Launch.Inactivity, host.Logger,
            host.PermissionWait == AcpPermissionWait.AsLongAsApproverHolds ? _pause : null,
            kept: true, wokenSink: CreateSink, wokenMoved: ScheduleQuiet);
    }

    /// <summary>A run that never started: the refusal that kept it from spawning, as the run's own end.</summary>
    public static IAgentRun AlreadyOver(SessionEnded refusal) => new OverRun(refusal);

    /// <summary>Spawn the agent, open its session and return the run idle, or already over when the open failed.</summary>
    public static async Task<IAgentRun> OpenAsync(
        Host host, AgentSessionOptions options, AgentRunOptions run, bool queuedWords, CancellationToken ct)
    {
        if (run.WokenTurnQuiet is { } quiet && quiet <= TimeSpan.Zero)
            throw new ArgumentException("A woken turn's quiet is a span of silence, so it must be positive.", nameof(run));
        if (run.WokenTurnQuiet > MaxWokenTurnQuiet)
            throw new ArgumentException("A woken turn's quiet is counted by a timer, which holds at most 49 days.", nameof(run));
        var instance = new AcpAgentRun(host, options, run, queuedWords);
        instance.Start();
        try
        {
            await instance._opened.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            instance.Kill("the open was cancelled");
            await instance._pump.ConfigureAwait(false);
            throw;
        }

        return instance;
    }

    private void Start()
    {
        _reader.Start();
        IAsyncEnumerable<string> lines;
        try
        {
            var stdin = Frames(_outbox.Reader.ReadAllAsync(CancellationToken.None));
            var tokens = _host.Launch.Tokens;
            lines = _host.Runner.ExchangeLinesAsync(tokens[0], [.. tokens.Skip(1), .. _host.Arguments], stdin, _pause, _host.Launch.Inactivity,
                workingDirectory: _host.Launch.Cwd, environment: _host.Launch.Environment, ct: _killToken);
        }
        catch (NotSupportedException ex)
        {
            Over(CliAgentLoop.Refused("runner-unsupported", ex.Message));
            return;
        }

        _pump = Task.Run(() => PumpAsync(lines));
    }

    /// <inheritdoc />
    public string? SessionId => _openOk ? _reader.SessionId : null;

    /// <inheritdoc />
    public Task<SessionEnded> Ended => _ended.Task;

    /// <inheritdoc />
    public IAsyncEnumerable<AgentRunTurn> WokenTurns => _woken.Reader.ReadAllAsync();

    /// <inheritdoc />
    public Task<AgentRunTurn> SendAsync(string prompt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ct.ThrowIfCancellationRequested();
        if (_ended.Task.IsCompleted) return Task.FromResult(Refused(AgentRunSubtypes.Over, "the run is over."));

        var refusal = _reader.TryPrompt(prompt, _queuedWords, CreateSink, out var turn);
        switch (refusal)
        {
            case AcpPromptRefusal.None:
                Window();
                return Task.FromResult(((TurnSink)turn!.Sink!).Turn);
            case AcpPromptRefusal.Busy:
                return Task.FromResult(Refused(AgentRunSubtypes.Busy, "a turn is running and takes no word: this door states no queued-words, or a stop is out."));
            case AcpPromptRefusal.RequestHeld:
                return Task.FromResult(Refused(AgentRunSubtypes.RequestHeld, "a permission request is held for the host, and no record shows a word taken beside one."));
            case AcpPromptRefusal.WokenRunning:
                return Task.FromResult(Refused(AgentRunSubtypes.WokenRunning, "the agent's own turn is running, and a prompt sent into one is never answered."));
            default:
                return Task.FromResult(Refused(AgentRunSubtypes.Over, "the run is not open."));
        }
    }

    /// <inheritdoc />
    public Task<bool> StopAsync(CancellationToken ct = default)
    {
        if (!_reader.TryCancelRun(out var turn)) return Task.FromResult(false);

        // an agent that never answers its cancel ends the run, as the one-turn door ends its process after the grace
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_host.CancelGrace, _host.Time, _killToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!turn!.Answered) Kill("the agent did not answer session/cancel within its grace");
        });
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposing, 1) != 0) return;
        _outbox.Writer.TryComplete();   // stdin closes, which is the agent's cue to go
        try
        {
            await _pump.WaitAsync(_host.CancelGrace, _host.Time).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Kill("the agent did not leave within its grace after the run was disposed");
            await _pump.ConfigureAwait(false);
        }

        Over(DisposedEnd());
    }

    // a dispose that had to kill the agent still ends Disposed, as IAgentRun.Ended promises: the kill is the detail
    private SessionEnded DisposedEnd() => new(ProviderVerdict.Ok, false, AgentRunSubtypes.Disposed, SessionId, null, _killReason);

    // ── the pump ─────────────────────────────────────────────────────────────

    private async IAsyncEnumerable<string> Frames(IAsyncEnumerable<string> frames, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var index = 0;
        await foreach (var frame in frames.WithCancellation(ct).ConfigureAwait(false))
        {
            yield return frame;
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
                var over = false;
                lock (_dispatch)
                {
                    var items = _reader.ReadRun(line);
                    over = Dispatch(items);
                }

                Window();
                ScheduleQuiet();
                if (over) return;
            }

            ended = Volatile.Read(ref _disposing) == 1
                ? DisposedEnd()
                : new SessionEnded(ProviderVerdict.Failed, true, "process-exited", SessionId, null, "the agent's process ended while the run was open.")
                    { Failure = new AgentFailure(AgentFailureKind.Fatal, "process.exited") };
        }
        catch (Exception ex)
        {
            ended = Fault(ex);
        }

        Over(ended);
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

    // Under the dispatch lock, in the wire's order: each event reaches its turn's channel, and a terminal ends that turn alone.
    // True when the run itself ended, which is the open's failure.
    private bool Dispatch(IReadOnlyList<(AcpTurnState? Turn, AgentStreamEvent Event)> items)
    {
        foreach (var (turn, evt) in items)
        {
            if (turn is null)
            {
                if (OpenEvent(evt)) return true;
                continue;
            }

            var sink = (TurnSink)turn.Sink!;
            if (Volatile.Read(ref sink.Terminated) == 1) continue;
            if (evt is SessionEnded ended) Terminate(sink, CliAgentLoop.Stamp(ended, sink.Watch));
            else
            {
                sink.Watch.See(evt);
                sink.Events.Writer.TryWrite(evt);
            }
        }

        return false;
    }

    private bool OpenEvent(AgentStreamEvent evt)
    {
        switch (evt)
        {
            case SessionEnded ended:
                // an open that fails ends the run before any prompt: nothing was sent
                Over(CliAgentLoop.Stamp(ended, new CliAgentLoop.TurnWatch(true, static () => false, static () => false, static () => [])));
                return true;
            case SessionStarted:
                lock (_liveGate) _openEvents?.Add(evt);
                _openOk = true;
                Window();
                _opened.TrySetResult();
                return false;
            default:
                lock (_liveGate) _openEvents?.Add(evt);
                return false;
        }
    }

    private void Terminate(TurnSink sink, SessionEnded ended)
    {
        if (Interlocked.Exchange(ref sink.Terminated, 1) == 1) return;
        sink.Events.Writer.TryWrite(ended);
        sink.Events.Writer.TryComplete();
        sink.Ended.TrySetResult(ended);
        lock (_liveGate) _live.Remove(sink);
    }

    // Under the reader's lock. Builds a turn's receiver, registers it so the run's end can cut it, and starts its stream with what
    // the open said (the first turn) and the task events held for it.
    private object? CreateSink(AcpTurnState state)
    {
        var watch = new CliAgentLoop.TurnWatch(true,
            () => state.FrameIndex < 0 ? null : Volatile.Read(ref _written) > state.FrameIndex,
            () => state.Answered, () => state.Unread.Snapshot());
        var sink = new TurnSink(state, watch);
        sink.Turn = new AgentRunTurn(state.Woken ? AgentRunTurnOrigin.Woken : AgentRunTurnOrigin.Sent,
            sink.Events.Reader.ReadAllAsync(), sink.Ended.Task);

        SessionEnded? cause;
        List<AgentStreamEvent>? opening;
        lock (_liveGate)
        {
            cause = _cause;
            if (cause is null) _live.Add(sink);
            opening = state.Woken ? null : _openEvents;
            if (!state.Woken) _openEvents = null;
        }

        foreach (var evt in opening ?? []) sink.Events.Writer.TryWrite(evt);
        foreach (var evt in state.Carried)
        {
            // a woken turn has no prompt to be read; a sent turn's carried events came before its prompt
            if (state.Woken) sink.Watch.See(evt);
            else sink.Watch.Carry(evt);
            sink.Events.Writer.TryWrite(evt);
        }

        if (cause is not null) Terminate(sink, Cut(sink, cause));
        else if (state.Woken) _woken.Writer.TryWrite(sink.Turn);
        return sink;
    }

    // ── the window, the quiet and the end ────────────────────────────────────

    // The inactivity window counts while a sent turn runs and is held paused otherwise.
    private void Window()
    {
        lock (_windowGate)
        {
            if (!_openOk || _ended.Task.IsCompleted) return;
            var running = _reader.SentRunning;
            if (running && _idleHold is not null)
            {
                _idleHold.Dispose();
                _idleHold = null;
            }
            else if (!running && _idleHold is null)
                _idleHold = _pause.Hold();
        }
    }

    private void ScheduleQuiet()
    {
        if (_run.WokenTurnQuiet is not { } quiet) return;
        lock (_timerGate)
        {
            if (_ended.Task.IsCompleted) return;
            // read inside the gate: a reading taken before it can be set after a newer one and leave the timer unarmed
            var left = _reader.WokenQuietLeft(quiet);
            _quietTimer ??= _host.Time.CreateTimer(OnQuiet, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _quietTimer.Change(left ?? Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnQuiet(object? state)
    {
        lock (_dispatch)
        {
            if (_ended.Task.IsCompleted) return;
            Dispatch(_reader.EndWokenIfQuiet(_run.WokenTurnQuiet!.Value));
        }

        ScheduleQuiet();   // said something since, or a call is open: it counts again from the next word
    }

    // A turn the run's end finds open ends with the same cause, failed, whatever the run's own verdict: its work never finished.
    private static SessionEnded Cut(TurnSink sink, SessionEnded cause) =>
        CliAgentLoop.Stamp(cause.IsError
            ? cause
            : cause with
            {
                Verdict = ProviderVerdict.Failed, IsError = true, Subtype = AgentRunSubtypes.Over,
                Diagnostic = "the run was disposed while the turn was open.",
                Failure = new AgentFailure(AgentFailureKind.Fatal, "run-disposed"),
            }, sink.Watch);

    private void Over(SessionEnded ended)
    {
        List<TurnSink> open;
        lock (_dispatch)
        {
            lock (_liveGate)
            {
                if (_cause is not null) return;
                _cause = ended;
                open = [.. _live];
            }

            foreach (var sink in open) Terminate(sink, Cut(sink, ended));
        }

        lock (_windowGate)
        {
            _idleHold?.Dispose();
            _idleHold = null;
        }

        lock (_timerGate)
        {
            _quietTimer?.Dispose();
            _quietTimer = null;
        }

        _reader.Close();   // a request held for an approver has nobody to answer now
        _outbox.Writer.TryComplete();
        _woken.Writer.TryComplete();
        _ended.TrySetResult(ended);
        _opened.TrySetResult();
    }

    private AgentRunTurn Refused(string subtype, string why)
    {
        var ended = new SessionEnded(ProviderVerdict.Unsupported, true, subtype, SessionId, null, why)
        {
            Failure = new AgentFailure(AgentFailureKind.Fatal, "refused-by-run") { Delivery = AgentDelivery.NeverSent },
        };
        return RefusedTurn(ended);
    }

    private static AgentRunTurn RefusedTurn(SessionEnded ended)
    {
        var channel = Channel.CreateBounded<AgentStreamEvent>(1);
        channel.Writer.TryWrite(ended);
        channel.Writer.TryComplete();
        return new AgentRunTurn(AgentRunTurnOrigin.Sent, channel.Reader.ReadAllAsync(), Task.FromResult(ended));
    }

    // a run that never started
    private sealed class OverRun(SessionEnded refusal) : IAgentRun
    {
        public string? SessionId => null;

        public Task<SessionEnded> Ended { get; } = Task.FromResult(refusal);

        public IAsyncEnumerable<AgentRunTurn> WokenTurns { get; } = Complete();

        private static IAsyncEnumerable<AgentRunTurn> Complete()
        {
            var none = Channel.CreateBounded<AgentRunTurn>(1);
            none.Writer.TryComplete();
            return none.Reader.ReadAllAsync();
        }

        public Task<AgentRunTurn> SendAsync(string prompt, CancellationToken ct = default)
        {
            var ended = refusal with { Subtype = AgentRunSubtypes.Over, Diagnostic = "the run is over: " + refusal.Diagnostic };
            return Task.FromResult(RefusedTurn(ended));
        }

        public Task<bool> StopAsync(CancellationToken ct = default) => Task.FromResult(false);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
