using Lyntai.Inference;

namespace Lyntai.Agents;

/// <summary>Who ran a turn: the provider, the door and the account, as the HOST names them. A hand-over compares
/// these to decide whether a conversation can be resumed (<see cref="AgentHandover"/>); Lyntai does not check
/// that they are true.</summary>
/// <param name="ProviderId">The <see cref="AgentProviderDescriptor.Id"/> the turn ran on.</param>
/// <param name="DoorId">The <see cref="AgentDoor.Id"/> it ran through. A resume token belongs to one door.</param>
/// <param name="Account">The host's name for the account, or null for the one the agent was already signed in as.
/// Opaque: two turns are on the same account only when the strings are equal.</param>
public sealed record AgentConversationSource(string ProviderId, string DoorId, string? Account = null);

/// <summary>What one turn did with one tool, counted from the stream: how many calls and how many of them came
/// back as an error. The name is the agent's own, so a brief shows it as a claim's detail, never as a fact.</summary>
/// <param name="Name">The tool's name as the door reported it.</param>
/// <param name="Calls">How many times it was called this turn.</param>
/// <param name="Failed">How many of those calls returned <see cref="ToolResult.IsError"/>.</param>
public sealed record ToolUseSummary(string Name, int Calls, int Failed);

/// <summary>One turn of a recorded conversation: the host's words, and what the stream showed of the agent's
/// answer. Everything the agent produced is a CLAIM; <see cref="Prompt"/> and <see cref="SystemPrompt"/> are the
/// host's own and are kept exactly as sent.</summary>
public sealed record TranscriptTurn
{
    /// <summary>The provider, door and account the turn ran on.</summary>
    public required AgentConversationSource Source { get; init; }

    /// <summary>The host's words for the turn, verbatim: <see cref="AgentSessionOptions.Prompt"/>.</summary>
    public required string Prompt { get; init; }

    /// <summary>The host's standing instruction for the turn, verbatim; null when it sent none.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>The agent's last message: the terminal event's <see cref="SessionEnded.FinalText"/> when it has
    /// one, else the text streamed after the turn's last tool step. Null when the agent said nothing. Never the
    /// agent's thinking, and never a diagnostic.</summary>
    public string? LastMessage { get; init; }

    /// <summary>The tools the turn used, in the order each was first called.</summary>
    public IReadOnlyList<ToolUseSummary> Tools { get; init; } = [];

    /// <summary>The agent's plan as the turn left it (the last <see cref="PlanUpdated"/>); null when the door
    /// sent none, which is not the same as the agent having none.</summary>
    public IReadOnlyList<PlanEntry>? Plan { get; init; }

    /// <summary>The conversation id the turn ended with, else the one it started with; the resume token for the
    /// next turn on the same door. Null when the door never gave one.</summary>
    public string? SessionId { get; init; }

    /// <summary>The terminal event's verdict; null when the stream ended with no terminal (it was abandoned or
    /// cancelled), which is a different fact from a failure.</summary>
    public ProviderVerdict? Verdict { get; init; }

    /// <summary>The kind of the terminal's <see cref="SessionEnded.Failure"/>; null for a turn that did not fail
    /// or whose failure the door did not read.</summary>
    public AgentFailureKind? FailureKind { get; init; }
}

/// <summary>A recorded conversation: its turns, oldest first. Immutable; what the host keeps, and where, is its
/// own (<c>docs/agent-provider-layer.md</c> §8). Built by <see cref="AgentTranscriptRecorder"/>, or by hand from
/// a host's own record.</summary>
/// <param name="Turns">The turns, oldest first.</param>
public sealed record AgentTranscript(IReadOnlyList<TranscriptTurn> Turns)
{
    /// <summary>A transcript with no turns.</summary>
    public static AgentTranscript Empty { get; } = new([]);

    /// <summary>The most recent turn that gave a session id: where a conversation could be picked up. Null when
    /// no turn did.</summary>
    public TranscriptTurn? LatestSession =>
        Turns.LastOrDefault(t => !string.IsNullOrEmpty(t.SessionId));
}
