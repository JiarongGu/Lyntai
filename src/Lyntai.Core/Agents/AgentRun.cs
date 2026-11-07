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
    /// <param name="ct">Cancels the open: the process is killed and the call throws.</param>
    /// <returns>The run. <b>An open that fails does not throw</b>: the run is already ended, <see cref="IAgentRun.Ended"/> holds the
    /// typed failure, and the host opens another run, which is a new process. The library never repeats an open itself.</returns>
    /// <exception cref="KeyNotFoundException">The provider has no such door.</exception>
    Task<IAgentRun> OpenRunAsync(string doorId, AgentSessionOptions options, CancellationToken ct = default);
}

/// <summary>A run of one agent, kept across turns on one process: its session is opened once, and each <see cref="SendAsync"/> is a
/// turn of it. One account for its life; the host gives its pick back when <see cref="Ended"/> completes.
/// <para><b>A turn</b> is the stream <see cref="IAgentSession.StreamAsync"/> gives, with exactly one <see cref="SessionEnded"/>, so
/// delivery, failure and usage read as they do per turn. Events that belong to no turn (an allowance report between turns) are
/// dropped.</para>
/// <para><b>A refusal is a turn, never a throw</b>: a refused <see cref="SendAsync"/> returns a turn whose only event is a
/// <see cref="SessionEnded"/> with <see cref="ProviderVerdict.Unsupported"/> and <see cref="AgentDelivery.NeverSent"/>, its subtype one
/// of <see cref="AgentRunSubtypes"/>.</para></summary>
public interface IAgentRun : IAsyncDisposable
{
    /// <summary>The conversation's id once the session is open (the resume token); null when the open failed.</summary>
    string? SessionId { get; }

    /// <summary>Completes once, when the run is over: the failed open or the process fault as the typed terminal it is, or an
    /// <see cref="ProviderVerdict.Ok"/> one with subtype <see cref="AgentRunSubtypes.Disposed"/> after
    /// <see cref="IAsyncDisposable.DisposeAsync"/>. A turn still open then ends first, failed, with the same cause.</summary>
    Task<SessionEnded> Ended { get; }

    /// <summary>Send a prompt. On an idle run it starts a turn. Refused while a turn runs, and on an ended run.</summary>
    /// <param name="prompt">The words.</param>
    /// <param name="ct">Cancels the send before it is made; a sent prompt is stopped by <see cref="StopAsync"/>.</param>
    Task<AgentRunTurn> SendAsync(string prompt, CancellationToken ct = default);

    /// <summary>Stop the running turn as the one-turn door cancels one (<c>session/cancel</c>), which ends as a cancelled turn does.
    /// Whether the next turn after a stop is usable is <see cref="AgentAbilities.KeepingStop"/>.</summary>
    /// <returns>True when a cancel was sent; false when no sent turn runs or a stop is already out.</returns>
    Task<bool> StopAsync(CancellationToken ct = default);
}

/// <summary>One turn of an <see cref="IAgentRun"/>: its events and its end.</summary>
public sealed class AgentRunTurn
{
    /// <param name="events">The turn's events in order, ending with its one <see cref="SessionEnded"/>; read once.</param>
    /// <param name="ended">Completes with that terminal, whether or not <paramref name="events"/> is read.</param>
    public AgentRunTurn(IAsyncEnumerable<AgentStreamEvent> events, Task<SessionEnded> ended)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(ended);
        Events = events;
        Ended = ended;
    }

    /// <summary>The turn's events, ending with its one <see cref="SessionEnded"/>. Read once.</summary>
    public IAsyncEnumerable<AgentStreamEvent> Events { get; }

    /// <summary>Completes with the turn's <see cref="SessionEnded"/>.</summary>
    public Task<SessionEnded> Ended { get; }
}

/// <summary>The <see cref="SessionEnded.Subtype"/> words a run says, each the library's own and never an agent's.</summary>
public static class AgentRunSubtypes
{
    /// <summary>Refused: a turn is running and a run takes no word on it.</summary>
    public const string Busy = "run-busy";

    /// <summary>Refused, or a turn cut off: the run is over.</summary>
    public const string Over = "run-over";

    /// <summary>The run's own end after <see cref="IAsyncDisposable.DisposeAsync"/>.</summary>
    public const string Disposed = "run-disposed";
}
