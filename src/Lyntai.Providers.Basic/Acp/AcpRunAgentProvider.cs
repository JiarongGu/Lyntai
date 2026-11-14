using Lyntai.Agents;
using Lyntai.Providers.Basic;

namespace Lyntai.Providers.Acp;

/// <summary>A protocol door's <see cref="IAgentProvider"/> that is also an <see cref="IAgentRunProvider"/>, so a host finds a kept
/// run by type test on the provider it already resolved. Describing and opening a one-turn session are the wrapped provider's own:
/// this adds a member and changes none.</summary>
internal sealed class AcpRunAgentProvider(AgentProvider inner, AcpAgentSession session) : IAgentProvider, IAgentRunProvider
{
    public AgentProviderDescriptor Describe() => inner.Describe();

    public IAgentSession Open(string doorId) => inner.Open(doorId);

    public Task<IAgentRun> OpenRunAsync(
        string doorId, AgentSessionOptions options, AgentRunOptions? run = null, CancellationToken ct = default)
    {
        var descriptor = inner.Describe();
        if (KeptRuns.Admit(descriptor, doorId, options) is { } refused) return Task.FromResult(AcpAgentRun.AlreadyOver(refused));

        var door = descriptor.Doors.Single(d => string.Equals(d.Id, doorId, StringComparison.Ordinal));
        return session.OpenRunAsync(options, run, door.SupportFor(AgentAbilities.QueuedWords) == AgentSupport.Supported, ct);
    }
}
