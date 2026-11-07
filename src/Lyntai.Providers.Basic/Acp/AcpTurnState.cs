using System.Text;
using Lyntai.Providers.Basic;

namespace Lyntai.Providers.Acp;

/// <summary>What a reader keeps for one turn: the words said so far, the calls it started and has open, and the frames it read
/// nothing from. A one-turn reader holds one for its life; a kept run's holds one per prompt and per woken turn.</summary>
internal sealed class AcpTurnState
{
    public int Id { get; init; }

    public bool Woken { get; init; }

    public StringBuilder Answer { get; } = new();

    public HashSet<string> StartedTools { get; } = new(StringComparer.Ordinal);

    public HashSet<string> OpenCalls { get; } = new(StringComparer.Ordinal);

    public UnreadFrames Unread { get; } = new();

    /// <summary>The place of the prompt's frame among the frames queued (0 is <c>initialize</c>); -1 for a turn no prompt opened.</summary>
    public int FrameIndex { get; init; } = -1;

    public bool Answered { get; set; }

    public bool CancelSent { get; set; }

    /// <summary>When the agent last said, thought, called or planned in this turn: what a woken turn's quiet counts from.</summary>
    public DateTimeOffset LastSpoke { get; set; }

    /// <summary>What the run built to receive the turn's events; the reader never reads it.</summary>
    public object? Sink { get; set; }

    /// <summary>Task events seen while no turn ran, which begin this turn's stream.</summary>
    public List<Lyntai.Agents.AgentStreamEvent> Carried { get; } = [];
}

/// <summary>Why a run's reader did not take a prompt.</summary>
internal enum AcpPromptRefusal
{
    None,
    Over,
    Busy,
    RequestHeld,
    WokenRunning,
}
