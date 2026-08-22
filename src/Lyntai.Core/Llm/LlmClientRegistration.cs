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

    internal List<LlmCandidate> Candidates { get; } = [];

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
    /// <para><b>These ids become the client's candidate list</b>, so narrowing a client's backends narrows
    /// what it TRIES — <c>UseDefaultCandidates</c> governs the default client, not this one. An id the
    /// global list already pins to a model keeps that model; the rest get the backend's own default. State
    /// the list outright with <see cref="UseCandidates"/> when that is not what you want.</para>
    ///
    /// <para>Naming none leaves the client over EVERY registered provider — the default client's own
    /// behaviour, which is the right meaning for a name that exists only to carry different governance
    /// later — and it then routes over the default candidate list, there being nothing to narrow.</para>
    /// </summary>
    /// <param name="providerIds">Backend ids, in fallback order.</param>
    public LlmClientBuilder UseProviders(params string[] providerIds)
    {
        ArgumentNullException.ThrowIfNull(providerIds);
        ProviderIds.AddRange(providerIds);
        return this;
    }

    /// <summary>
    /// State this client's fallback list outright, instead of letting it be derived from
    /// <see cref="UseProviders"/>.
    ///
    /// <para>Needed for the one split derivation cannot express: TWO models of the SAME backend. A derived
    /// list carries one entry per pooled id, so "the big model for chat, the small one for extraction" is
    /// unsayable whenever both live behind a single id — which is the split naming a client exists for.</para>
    ///
    /// <para><b>Every candidate must name a backend this client is pooled over</b>, or composition throws:
    /// a candidate the router can never select is a call that fails on every attempt, and that failure is
    /// worth having at startup rather than per request.</para>
    ///
    /// <para>SETS (clears + replaces), matching <c>UseDefaultCandidates</c> — the last call wins; it does not
    /// append.</para>
    /// </summary>
    /// <param name="candidates">The fallback list, in order.</param>
    public LlmClientBuilder UseCandidates(params LlmCandidate[] candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        Candidates.Clear();
        Candidates.AddRange(candidates);
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

        if (!builder.NamedLlmClients.TryAdd(name, client))
            throw new ArgumentException(
                $"An LLM client named '{name}' is already registered; one of the two would be unreachable.",
                nameof(name));

        return builder;
    }
}
