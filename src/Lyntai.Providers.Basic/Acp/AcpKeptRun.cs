namespace Lyntai.Providers.Acp;

/// <summary>Whether a protocol door opens a run kept across turns (<see cref="Lyntai.Agents.IAgentRunProvider"/>), and which
/// agent's measured record the claim rests on. The door is generic, so a registration names the agent it speaks to: a claim
/// no record shows is <see cref="Lyntai.Agents.AgentSupport.Unknown"/>, never assumed from the protocol.</summary>
public enum AcpKeptRun
{
    /// <summary>The door opens no run, and states <see cref="Lyntai.Agents.AgentAbilities.KeptRun"/> unknown.</summary>
    None = 0,

    /// <summary><c>claude-agent-acp</c>: a second prompt on one process, a word queued and folded at the next step, background
    /// work read until it ends and the turn its end wakes, each recorded by a host against 0.79.0 and 0.84.0.</summary>
    ClaudeAgentAcp,
}
