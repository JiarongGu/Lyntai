using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lyntai.Agents;

/// <summary>Wraps every <see cref="IAgentSession"/> registration — unkeyed and keyed — in a
/// <see cref="RetryingAgentSession"/>, once, when <c>AddLyntai</c> finishes composing, so the order of
/// <c>AddAgentRetry</c> against the <c>Add*AgentSession</c> calls does not matter. The registration's lifetime
/// and key are kept.</summary>
internal static class AgentRetryRegistration
{
    public static void Wrap(IServiceCollection services, AgentRetryOptions options)
    {
        for (var i = 0; i < services.Count; i++)
            if (services[i] is { ServiceType: var type } d && type == typeof(IAgentSession))
                services[i] = Decorate(d, options);
    }

    private static ServiceDescriptor Decorate(ServiceDescriptor d, AgentRetryOptions options) =>
        d.IsKeyedService
            ? ServiceDescriptor.DescribeKeyed(typeof(IAgentSession), d.ServiceKey,
                (sp, key) => Retrying(sp, ResolveKeyed(d, sp, key), options), d.Lifetime)
            : ServiceDescriptor.Describe(typeof(IAgentSession),
                sp => Retrying(sp, Resolve(d, sp), options), d.Lifetime);

    private static RetryingAgentSession Retrying(IServiceProvider sp, IAgentSession inner, AgentRetryOptions options) =>
        new(inner, options, sp.GetService<TimeProvider>(), sp.GetService<ILogger<RetryingAgentSession>>());

    private static IAgentSession Resolve(ServiceDescriptor d, IServiceProvider sp) =>
        d.ImplementationInstance as IAgentSession
        ?? d.ImplementationFactory?.Invoke(sp) as IAgentSession
        ?? (d.ImplementationType is { } type ? (IAgentSession)ActivatorUtilities.CreateInstance(sp, type) : null)
        ?? throw new InvalidOperationException("An IAgentSession registration produced no session.");

    private static IAgentSession ResolveKeyed(ServiceDescriptor d, IServiceProvider sp, object? key) =>
        d.KeyedImplementationInstance as IAgentSession
        ?? d.KeyedImplementationFactory?.Invoke(sp, key) as IAgentSession
        ?? (d.KeyedImplementationType is { } type ? (IAgentSession)ActivatorUtilities.CreateInstance(sp, type) : null)
        ?? throw new InvalidOperationException($"The IAgentSession registered under '{key}' produced no session.");
}
