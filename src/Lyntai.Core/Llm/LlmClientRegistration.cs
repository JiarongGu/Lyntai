using Lyntai.Llm;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai;

/// <summary>Configures ONE named LLM client inside <c>AddLlmClient(name, c => …)</c> — the counterpart of
/// the memory engine's own <c>AddMemoryEngine(name, e => …)</c> callback.</summary>
public sealed class LlmClientBuilder
{
    internal LlmClientBuilder(string name) => Name = name;

    /// <summary>The client's name.</summary>
    public string Name { get; }

    internal List<string> ProviderIds { get; } = [];

    /// <summary>
    /// Route this client over the named backends ONLY, in the order given — which is also the fallback
    /// order, exactly as it is for the default client.
    ///
    /// <para><b>Ids, not instances</b>, matching how <see cref="ILlmRouter"/> already selects candidates and
    /// how a consumer already names backends everywhere else (<c>LlmRequest.Candidates</c>, the routing
    /// policy). It also means a client can be configured before the backends it names are registered, so
    /// composition-root ORDER does not become load-bearing.</para>
    ///
    /// <para><b>An id that matches no registered provider is an ERROR at resolution</b>, never a silent
    /// narrowing to the ones that do exist. A memory subsystem pointed at "ollama" on a host where Ollama
    /// was never registered must fail loudly; degrading to the app's expensive default is precisely the
    /// outcome naming a client was meant to prevent.</para>
    ///
    /// <para>Naming none leaves the client over EVERY registered provider — the default client's own
    /// behaviour, which is the right meaning for a name that exists only to carry different governance
    /// later.</para>
    /// </summary>
    /// <param name="providerIds">Backend ids, in fallback order.</param>
    public LlmClientBuilder UseProviders(params string[] providerIds)
    {
        ArgumentNullException.ThrowIfNull(providerIds);
        ProviderIds.AddRange(providerIds);
        return this;
    }
}

/// <summary>Registers named <see cref="ILlmClient"/>s, resolvable through
/// <see cref="ILlmClientFactory"/>.</summary>
public static class LlmClientRegistration
{
    /// <summary>
    /// Register an LLM client under <paramref name="name"/>, routed over its own set of backends.
    ///
    /// <para>The counterpart of <c>AddMemoryEngine</c>: a subsystem names the client it wants rather than
    /// taking whatever the app made default, so memory extraction can run on a small local backend while
    /// chat runs on the best available one — without either hand-building a router (which throws away the
    /// shared dead-host cooldown and admission table) or rewiring the app.</para>
    ///
    /// <para><b>Every named client carries the same front-door decorators and refusal screening as the
    /// default one.</b> A name selects BACKENDS, never permissions — see
    /// <see cref="ILlmClientFactory"/>.</para>
    /// </summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="name">The client's name, unique within the container.</param>
    /// <param name="configure">Configures the client; null routes it over every registered provider.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or whitespace, or is already
    /// registered — a duplicate would make one of the two unreachable, exactly as it would for a memory
    /// engine.</exception>
    public static LyntaiBuilder AddLlmClient(this LyntaiBuilder builder, string name,
        Action<LlmClientBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("An LLM client needs a name.", nameof(name));

        var client = new LlmClientBuilder(name);
        configure?.Invoke(client);

        if (!builder.NamedLlmClients.TryAdd(name, client.ProviderIds))
            throw new ArgumentException(
                $"An LLM client named '{name}' is already registered; one of the two would be unreachable.",
                nameof(name));

        return builder;
    }
}
