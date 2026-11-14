namespace Lyntai.Providers.Acp;

/// <summary>Whether a protocol door reads a child agent as a session of its own, and which agent's record the claim rests on. It is
/// the host's choice, because the two readings are not interchangeable: declared, the agent removes the call that spawned the child
/// from the session's stream, so a transcript, a permission surface or a hand-over that reads that call loses it.</summary>
public enum AcpChildSessions
{
    /// <summary>The door declares nothing. A child's words and calls arrive on the session stamped with the call that spawned it
    /// (<see cref="Lyntai.Agents.ToolCall.ParentCallId"/>), the call stays in the session's stream, and the door states
    /// <see cref="Lyntai.Agents.AgentAbilities.ChildSessions"/> unsupported.</summary>
    None = 0,

    /// <summary><c>claude-agent-acp</c>: the door declares AIR's <c>nativeSubagentSessions</c> at <c>initialize</c>, reads
    /// <c>subagent_spawned</c> and <c>subagent_state_update</c> as <see cref="Lyntai.Agents.ChildSessionStarted"/> and
    /// <see cref="Lyntai.Agents.ChildSessionEnded"/>, and routes a child's updates by their session id as
    /// <see cref="Lyntai.Agents.ChildSessionUpdate"/>s, recorded by a host against 0.79.0 with one asynchronous child. <b>The spawning
    /// call then leaves the session's stream.</b> A synchronous, nested or failed child was not recorded.</summary>
    ClaudeAgentAcp,
}
