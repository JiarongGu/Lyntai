using Lyntai.Agents;
using Lyntai.Inference;

namespace Lyntai.Providers.Basic;

/// <summary>A CLI door's <see cref="IAgentProvider"/> that is also an <see cref="IAgentUsageSource"/> and an
/// <see cref="IAgentAuthSource"/>, so a host finds each ability by type test on the provider it already resolved. Describing and
/// opening are the wrapped provider's own: this adds members and changes none.</summary>
internal sealed class CliAgentProvider(AgentProvider inner, CliUsageSource usage, CliAuthSource auth)
    : IAgentProvider, IAgentUsageSource, IAgentAuthSource
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

    public Task<ProviderAuthStatus> StatusAsync(AgentAccount? account = null, CancellationToken ct = default) =>
        auth.StatusAsync(account, ct);

    public Task<ProviderAuthResult> LoginAsync(
        AgentAccount? account = null, ProviderLoginRequest? request = null, Action<string>? onOutput = null, CancellationToken ct = default) =>
        auth.LoginAsync(account, request, onOutput, ct);
}
