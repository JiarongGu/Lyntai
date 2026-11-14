using Lyntai.Agents;
using Lyntai.Providers.Basic;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>The <c>claude</c> CLI door's provider: the CLI door's abilities (<see cref="CliAgentProvider"/>) and a run kept across
/// turns on one process with stdin held open, each found by type test on the provider a host already resolved. It derives rather
/// than wraps, since a wrapper would hide the usage and sign-in sources from that type test.</summary>
internal sealed class ClaudeCliAgentProvider(AgentProvider inner, CliUsageSource usage, CliAuthSource auth, ClaudeAgentSession session)
    : CliAgentProvider(inner, usage, auth), IAgentRunProvider
{
    public Task<IAgentRun> OpenRunAsync(
        string doorId, AgentSessionOptions options, AgentRunOptions? run = null, CancellationToken ct = default) =>
        KeptRuns.Admit(Describe(), doorId, options) is { } refused
            ? Task.FromResult<IAgentRun>(new KeptRuns.OverRun(refused))
            : session.OpenRunAsync(options, run, ct);
}
