using Lyntai.Agents;
using Lyntai.Inference;
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
        ArgumentNullException.ThrowIfNull(doorId);
        ArgumentNullException.ThrowIfNull(options);
        var descriptor = inner.Describe();
        var door = descriptor.Doors.FirstOrDefault(d => string.Equals(d.Id, doorId, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException(
                $"Agent provider '{descriptor.Id}' has no door '{doorId}'. Doors: {string.Join(", ", descriptor.Doors.Select(d => d.Id))}.");

        // the two refusals AgentProvider makes of a turn, made of a run's open too: nothing is spawned for either
        if (options.Account is { } account
            && !string.Equals(account.Provider, descriptor.Id, StringComparison.Ordinal)
            && !string.Equals(account.Provider, descriptor.Agent, StringComparison.Ordinal))
            return Task.FromResult(AcpAgentRun.AlreadyOver(Refused(AgentProvider.AccountRefusedSubtype,
                $"account '{account.Name}' belongs to '{account.Provider}', not to provider '{descriptor.Id}'"
                + (descriptor.Agent is null ? "" : $" or its agent '{descriptor.Agent}'"))));

        var refused = door.Unhonourable(options);
        if (refused.Count > 0)
            return Task.FromResult(AcpAgentRun.AlreadyOver(Refused(AgentProvider.OptionRefusedSubtype,
                $"door '{door.Id}' cannot honour: {string.Join(", ", refused)}")));

        return session.OpenRunAsync(options, run, door.SupportFor(AgentAbilities.QueuedWords) == AgentSupport.Supported, ct);
    }

    private static SessionEnded Refused(string subtype, string why) =>
        new(ProviderVerdict.Unsupported, true, subtype, null, null, why)
        {
            Failure = new AgentFailure(AgentFailureKind.Fatal, "refused-before-spawn") { Delivery = AgentDelivery.NeverSent },
        };
}
