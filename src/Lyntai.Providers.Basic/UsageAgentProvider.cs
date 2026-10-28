using Lyntai.Agents;

namespace Lyntai.Providers.Basic;

/// <summary>A CLI door's <see cref="IAgentProvider"/> that is also an <see cref="IAgentUsageSource"/>, so a host finds
/// the ability by type test on the provider it already resolved. Describing and opening are the wrapped provider's
/// own: this adds a member and changes none.</summary>
internal sealed class UsageAgentProvider(AgentProvider inner, CliUsageSource usage) : IAgentProvider, IAgentUsageSource
{
    // concrete parameter types on purpose: this is an ability added beside a provider, not a decorator of the
    // interfaces, and the decorated-interface scan (D67) reads a parameter of the interface's own type as one
    /// <summary>What a start of the vendor's own server may do, which no flag here rules out: the account's home
    /// is written to, the vendor may refresh a credential, load what it is configured with, open an endpoint beyond
    /// stdio, and leave a helper running past the process the library ends.</summary>
    internal const AgentSideEffects ServerStart =
        AgentSideEffects.RefreshesCredentials | AgentSideEffects.ChangesConfiguration | AgentSideEffects.LoadsExtensions
        | AgentSideEffects.OpensListener | AgentSideEffects.LeavesProcess;

    public AgentProviderDescriptor Describe() => inner.Describe();

    public IAgentSession Open(string doorId) => inner.Open(doorId);

    public Task<AgentAllowanceReading> ReadAllowanceAsync(AgentAccount account, CancellationToken ct = default) =>
        usage.ReadAllowanceAsync(account, ct);
}
