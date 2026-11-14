using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lyntai.Agents;

/// <summary>Wraps every <see cref="IAgentSession"/> registration — unkeyed and keyed — in a
/// <see cref="RetryingAgentSession"/>, once, when <c>AddLyntai</c> finishes composing, so the order of
/// <c>AddAgentRetry</c> against the <c>Add*AgentSession</c> calls does not matter. The registration's lifetime
/// and key are kept, and so is who disposes the session: a factory- or type-built one is built through a holder
/// the container tracks, so it is disposed with the provider or scope that built it, and an instance stays the
/// host's.</summary>
internal static class AgentRetryRegistration
{
    public static void Wrap(IServiceCollection services, AgentRetryOptions options)
    {
        var count = services.Count;
        var anyKeyHeld = false;
        for (var i = 0; i < count; i++)
            if (services[i] is { ServiceType: var type } d && type == typeof(IAgentSession))
                services[i] = Decorate(services, d, options, ref anyKeyHeld);
    }

    private static ServiceDescriptor Decorate(
        IServiceCollection services, ServiceDescriptor d, AgentRetryOptions options, ref bool anyKeyHeld)
    {
        if (!d.IsKeyedService)
        {
            if (d.ImplementationInstance is IAgentSession instance)
                return ServiceDescriptor.Describe(typeof(IAgentSession), sp => Retrying(sp, instance, options), d.Lifetime);
            var key = new InnerKey(d, null);
            services.Add(ServiceDescriptor.DescribeKeyed(typeof(AgentRetryInner), key,
                (sp, _) => AgentRetryInner.Over(Resolve(d, sp)), d.Lifetime));
            return ServiceDescriptor.Describe(typeof(IAgentSession), sp => Retrying(sp, Inner(sp, key), options), d.Lifetime);
        }

        if (d.KeyedImplementationInstance is IAgentSession keyedInstance)
            return ServiceDescriptor.DescribeKeyed(typeof(IAgentSession), d.ServiceKey,
                (sp, _) => Retrying(sp, keyedInstance, options), d.Lifetime);

        if (!Equals(d.ServiceKey, KeyedService.AnyKey))
        {
            var key = new InnerKey(d, d.ServiceKey);
            services.Add(ServiceDescriptor.DescribeKeyed(typeof(AgentRetryInner), key,
                (sp, _) => AgentRetryInner.Over(ResolveKeyed(d, sp, d.ServiceKey)), d.Lifetime));
            return ServiceDescriptor.DescribeKeyed(typeof(IAgentSession), d.ServiceKey,
                (sp, _) => Retrying(sp, Inner(sp, key), options), d.Lifetime);
        }

        // Under any key the holder is asked once per wrapper built, from the provider building it, so transient
        // tracks it where the registration's own lifetime would; one registration serves every such descriptor.
        if (!anyKeyHeld)
        {
            services.Add(ServiceDescriptor.DescribeKeyed(typeof(AgentRetryInner), KeyedService.AnyKey,
                (sp, key) => key is InnerKey inner
                    ? AgentRetryInner.Over(ResolveKeyed(inner.Registration, sp, inner.Key))
                    : throw new InvalidOperationException($"No IAgentSession registration is held under '{key}'."),
                ServiceLifetime.Transient));
            anyKeyHeld = true;
        }

        return ServiceDescriptor.DescribeKeyed(typeof(IAgentSession), d.ServiceKey,
            (sp, key) => Retrying(sp, Inner(sp, new InnerKey(d, key)), options), d.Lifetime);
    }

    private static RetryingAgentSession Retrying(IServiceProvider sp, IAgentSession inner, AgentRetryOptions options) =>
        new(inner, options, sp.GetService<TimeProvider>(), sp.GetService<ILogger<RetryingAgentSession>>());

    private static IAgentSession Inner(IServiceProvider sp, InnerKey key) =>
        sp.GetRequiredKeyedService<AgentRetryInner>(key).Session;

    private static IAgentSession Resolve(ServiceDescriptor d, IServiceProvider sp) =>
        d.ImplementationFactory?.Invoke(sp) as IAgentSession
        ?? (d.ImplementationType is { } type ? (IAgentSession)ActivatorUtilities.CreateInstance(sp, type) : null)
        ?? throw new InvalidOperationException("An IAgentSession registration produced no session.");

    private static IAgentSession ResolveKeyed(ServiceDescriptor d, IServiceProvider sp, object? key) =>
        d.KeyedImplementationFactory?.Invoke(sp, key) as IAgentSession
        ?? (d.KeyedImplementationType is { } type ? (IAgentSession)ActivatorUtilities.CreateInstance(sp, type) : null)
        ?? throw new InvalidOperationException($"The IAgentSession registered under '{key}' produced no session.");

    // Private, so only this class can ask for a holder; the descriptor compares by reference, one key per registration.
    private sealed record InnerKey(ServiceDescriptor Registration, object? Key);

    /// <summary>The session a registration built, resolved through the container so the container tracks it. Never an
    /// <see cref="IAgentSession"/>, so no enumeration of sessions surfaces the unwrapped one.</summary>
    private class AgentRetryInner(IAgentSession session)
    {
        public IAgentSession Session { get; } = session;

        // only a disposable session gets a disposable holder: the container tracks a transient it disposes for as
        // long as its scope lives, and a session that holds nothing must not be kept alive for that
        public static AgentRetryInner Over(IAgentSession session) =>
            session is IDisposable or IAsyncDisposable ? new Owned(session) : new AgentRetryInner(session);

        // Both interfaces, since a sync ServiceProvider.Dispose() throws on a service that has only the async one;
        // an async-only session meets the refusal the container would have given it unwrapped.
        private sealed class Owned(IAgentSession session) : AgentRetryInner(session), IAsyncDisposable, IDisposable
        {
            public ValueTask DisposeAsync()
            {
                if (Session is IAsyncDisposable disposable) return disposable.DisposeAsync();
                ((IDisposable)Session).Dispose();
                return default;
            }

            public void Dispose()
            {
                if (Session is IDisposable disposable) disposable.Dispose();
                else throw new InvalidOperationException(
                    $"'{Session.GetType()}' type only implements IAsyncDisposable. Use DisposeAsync to dispose the container.");
            }
        }
    }
}
