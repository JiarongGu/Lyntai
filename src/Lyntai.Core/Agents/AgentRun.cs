using Lyntai.Inference;

namespace Lyntai.Agents;

/// <summary>An optional capability of an <see cref="IAgentProvider"/>, found by type test: it opens an <see cref="IAgentRun"/>, one
/// agent process kept across turns. <see cref="IAgentSession"/> gains no member for it, and a provider that is not an
/// <see cref="IAgentRunProvider"/> keeps the one-turn model alone. The door states what its run can do as
/// <see cref="AgentAbilities.KeptRun"/> and the five abilities beside it.</summary>
public interface IAgentRunProvider
{
    /// <summary>Open a run on a door: spawn the agent and open its session, once. The run is idle when this returns.</summary>
    /// <param name="doorId">An <see cref="AgentDoor.Id"/> from <see cref="IAgentProvider.Describe"/> whose door states
    /// <see cref="AgentAbilities.KeptRun"/> <see cref="AgentSupport.Supported"/>.</param>
    /// <param name="options">What the session opens with. <see cref="AgentSessionOptions.Prompt"/> is read as nothing: a run's words
    /// are <see cref="IAgentRun.SendAsync"/>'s, and <see cref="AgentSessionOptions.SystemPrompt"/> leads the first of them only.
    /// <see cref="AgentSessionOptions.Account"/> is the run's for its life.</param>
    /// <param name="run">What the host sets for the run itself; null = none.</param>
    /// <param name="ct">Cancels the open: the process is killed and the call throws.</param>
    /// <returns>The run. <b>An open that fails does not throw</b>: the run is already ended, <see cref="IAgentRun.Ended"/> holds the
    /// typed failure, and the host opens another run, which is a new process. The library never repeats an open itself.</returns>
    /// <exception cref="KeyNotFoundException">The provider has no such door.</exception>
    Task<IAgentRun> OpenRunAsync(
        string doorId, AgentSessionOptions options, AgentRunOptions? run = null, CancellationToken ct = default);
}

/// <summary>What a host sets for an <see cref="IAgentRun"/> beyond the session's own options.</summary>
public sealed record AgentRunOptions
{
    /// <summary>How long a woken turn may say nothing before the run ends it with <see cref="AgentRunSubtypes.Quiet"/>. <b>Null, the
    /// default, never ends one</b>: the library invents no deadline for the agent's own turn, so a woken turn then ends only with the
    /// run. Silence is counted while the turn has no call open and no permission request held, from the agent's last word.
    /// Positive, and at most 49 days, which a timer can count.</summary>
    public TimeSpan? WokenTurnQuiet { get; init; }
}

/// <summary>A run of one agent, kept across turns on one process: its session is opened once, and each <see cref="SendAsync"/> is a
/// turn of it. One account for its life; the host gives its pick back when <see cref="Ended"/> completes.
/// <para><b>A turn</b> is the stream <see cref="IAgentSession.StreamAsync"/> gives, with exactly one <see cref="SessionEnded"/>, so
/// delivery, failure and usage read as they do per turn. Events that belong to no turn (an allowance report between turns) are
/// dropped, and a background task's start or end seen while none runs is held for the next turn to begin.</para>
/// <para><b>A refusal is a turn, never a throw</b>: a refused <see cref="SendAsync"/> returns a turn whose only event is a
/// <see cref="SessionEnded"/> with <see cref="ProviderVerdict.Unsupported"/> and <see cref="AgentDelivery.NeverSent"/>, its subtype one
/// of <see cref="AgentRunSubtypes"/>.</para></summary>
public interface IAgentRun : IAsyncDisposable
{
    /// <summary>The conversation's id once the session is open (the resume token); null when the open failed.</summary>
    string? SessionId { get; }

    /// <summary>Completes once, when the run is over: the failed open or the process fault as the typed terminal it is, or an
    /// <see cref="ProviderVerdict.Ok"/> one with subtype <see cref="AgentRunSubtypes.Disposed"/> after
    /// <see cref="IAsyncDisposable.DisposeAsync"/>, also when the dispose had to kill an agent that would not leave (its
    /// <see cref="SessionEnded.Diagnostic"/> says so). A turn still open then ends first, failed, with the same cause.</summary>
    Task<SessionEnded> Ended { get; }

    /// <summary>Send a prompt. On an idle run it starts a turn. On a busy run it is a word: taken only where the door states
    /// <see cref="AgentAbilities.QueuedWords"/> <see cref="AgentSupport.Supported"/>, when the earlier turn then ends
    /// <see cref="AgentRunSubtypes.Folded"/> (success, every usage count 0) and this one owns the rest of the work. Refused while a
    /// permission request is held, while a woken turn runs, on a busy run whose door takes no word, while a stop is out (the
    /// cancelled turn would answer it), and on an ended run.</summary>
    /// <param name="prompt">The words.</param>
    /// <param name="ct">Cancels the send before it is made; a sent prompt is stopped by <see cref="StopAsync"/>.</param>
    Task<AgentRunTurn> SendAsync(string prompt, CancellationToken ct = default);

    /// <summary>Stop the running turn as the one-turn door cancels one (<c>session/cancel</c>), which ends as a cancelled turn does.
    /// <b>Sends nothing while a word is on its way</b>, since a cancel then loses the word's answer: stop again once the earlier
    /// turn has ended. Whether the next turn after a stop is usable is <see cref="AgentAbilities.KeepingStop"/>.</summary>
    /// <returns>True when a cancel was sent; false when no sent turn runs, a word is on its way, or a stop is already out.</returns>
    Task<bool> StopAsync(CancellationToken ct = default);

    /// <summary>The turns the agent starts by itself after a task's end, in the order they begin, completing when the run ends. One
    /// reader; a turn not read is held. A woken turn ends only by <see cref="AgentRunOptions.WokenTurnQuiet"/>.</summary>
    IAsyncEnumerable<AgentRunTurn> WokenTurns { get; }
}

/// <summary>An optional capability of an <see cref="IAgentRun"/>, found by type test (<see cref="IAgentRun"/> gains no member): words that
/// cut into the running turn at once, where <see cref="IAgentRun.SendAsync"/>'s word waits for the agent's next step. A run offers it
/// where its door states <see cref="AgentAbilities.Steering"/> <see cref="AgentSupport.Supported"/>, and refuses it where the agent's open
/// did not say it takes it.</summary>
public interface IAgentRunSteering
{
    /// <summary>Send words into the running turn and say where they landed. <b>A refusal or a failure is a result, never a throw.</b>
    /// Refused while a permission request is held, while a woken turn runs, while a stop is out, and on an ended run, since no record
    /// shows a steer beside any of them. A stop is not sent while a steer waits for its answer.</summary>
    /// <param name="words">The words, sent as the content of a prompt.</param>
    /// <param name="ct">Cancels the wait; words already sent are not taken back.</param>
    Task<AgentSteerResult> SteerAsync(string words, CancellationToken ct = default);
}

/// <summary>Where a steer's words landed, as the agent answered.</summary>
public enum AgentSteerOutcome
{
    /// <summary>Taken into the running turn: the step in flight is cut, the turn goes on, and no turn ends. A call the agent had
    /// announced may never be finished, and the turn's own end still arrives on its <see cref="AgentRunTurn"/>.</summary>
    Injected,

    /// <summary>No turn was running and the agent began one with the words. No prompt owns it, so it is a woken turn on
    /// <see cref="IAgentRun.WokenTurns"/>, ended as a woken turn is.</summary>
    StartedNewTurn,

    /// <summary>No turn was running and the agent left the words to the host: send them with <see cref="IAgentRun.SendAsync"/>.</summary>
    PromptRequired,

    /// <summary>The run refused before sending; <see cref="AgentSteerResult.Subtype"/> says why, as an <see cref="AgentRunSubtypes"/> word.</summary>
    Refused,

    /// <summary>Whether the words landed is not known: the agent answered an error or an outcome this library has no word for, or the
    /// run ended before it answered.</summary>
    Failed,
}

/// <summary>What <see cref="IAgentRunSteering.SteerAsync"/> answered.</summary>
/// <param name="Outcome">Where the words landed.</param>
public sealed record AgentSteerResult(AgentSteerOutcome Outcome)
{
    /// <summary>For <see cref="AgentSteerOutcome.Refused"/>, the <see cref="AgentRunSubtypes"/> word saying why; null otherwise.</summary>
    public string? Subtype { get; init; }

    /// <summary>The reason in words, or the agent's own for a failure; null when there is nothing to add.</summary>
    public string? Detail { get; init; }
}

/// <summary>Where a turn of a run came from.</summary>
public enum AgentRunTurnOrigin
{
    /// <summary>The host's <see cref="IAgentRun.SendAsync"/>.</summary>
    Sent,

    /// <summary>The agent's own, on <see cref="IAgentRun.WokenTurns"/>: no prompt opened it.</summary>
    Woken,
}

/// <summary>One turn of an <see cref="IAgentRun"/>: its events and its end.</summary>
public sealed class AgentRunTurn
{
    /// <param name="origin">Where the turn came from.</param>
    /// <param name="events">The turn's events in order, ending with its one <see cref="SessionEnded"/>; read once.</param>
    /// <param name="ended">Completes with that terminal, whether or not <paramref name="events"/> is read.</param>
    public AgentRunTurn(AgentRunTurnOrigin origin, IAsyncEnumerable<AgentStreamEvent> events, Task<SessionEnded> ended)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(ended);
        Origin = origin;
        Events = events;
        Ended = ended;
    }

    /// <summary>Where the turn came from.</summary>
    public AgentRunTurnOrigin Origin { get; }

    /// <summary>The turn's events, ending with its one <see cref="SessionEnded"/>. Read once.</summary>
    public IAsyncEnumerable<AgentStreamEvent> Events { get; }

    /// <summary>Completes with the turn's <see cref="SessionEnded"/>.</summary>
    public Task<SessionEnded> Ended { get; }
}

/// <summary>The <see cref="SessionEnded.Subtype"/> words a run says, each the library's own and never an agent's.</summary>
public static class AgentRunSubtypes
{
    /// <summary>A turn the agent answered <c>end_turn</c> with every usage count 0 because a later word took over its work.</summary>
    public const string Folded = "folded";

    /// <summary>A woken turn ended by <see cref="AgentRunOptions.WokenTurnQuiet"/> of silence.</summary>
    public const string Quiet = "quiet";

    /// <summary>Refused: the run is busy and its door states no <see cref="AgentAbilities.QueuedWords"/>, or a stop is out.</summary>
    public const string Busy = "run-busy";

    /// <summary>Refused: a permission request is held for the host, and no record shows a word with one.</summary>
    public const string RequestHeld = "run-request-held";

    /// <summary>Refused: a woken turn runs, and a prompt sent into one is never answered.</summary>
    public const string WokenRunning = "run-woken-turn";

    /// <summary>Refused, or a turn cut off: the run is over.</summary>
    public const string Over = "run-over";

    /// <summary>A steer refused: the agent's open did not say it takes one.</summary>
    public const string SteeringUnavailable = "run-steering-unavailable";

    /// <summary>The run's own end after <see cref="IAsyncDisposable.DisposeAsync"/>.</summary>
    public const string Disposed = "run-disposed";
}
