using System.Text;
using Lyntai.Providers.Basic;

namespace Lyntai.Providers.Acp;

/// <summary>What a reader keeps for one turn: the words said so far, the calls it started and has open, and the frames it read
/// nothing from. A one-turn reader holds one for its life; a kept run's holds one per prompt and per woken turn.</summary>
internal sealed class AcpTurnState
{
    public int Id { get; init; }

    public StringBuilder Answer { get; } = new();

    public HashSet<string> StartedTools { get; } = new(StringComparer.Ordinal);

    public UnreadFrames Unread { get; } = new();

    /// <summary>The place of the prompt's frame among the frames queued (0 is <c>initialize</c>); -1 for a turn no prompt opened.</summary>
    public int FrameIndex { get; init; } = -1;

    public bool Answered { get; set; }

    public bool CancelSent { get; set; }

    /// <summary>What the run built to receive the turn's events; the reader never reads it.</summary>
    public object? Sink { get; set; }
}

/// <summary>Why a run's reader did not take a prompt.</summary>
internal enum AcpPromptRefusal
{
    None,
    Over,
    Busy,
}
