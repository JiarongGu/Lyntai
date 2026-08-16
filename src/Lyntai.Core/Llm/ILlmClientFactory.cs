namespace Lyntai.Llm;

/// <summary>
/// Resolves LLM clients by name, the way <see cref="Lyntai.Memory.IMemoryEngineFactory"/> resolves memory
/// engines and <c>IHttpClientFactory</c> resolves HTTP clients.
///
/// <para><b>What it is for:</b> a subsystem that calls a model on the app's behalf — memory extraction,
/// reranking, salience judging — should not silently run on whatever backend happens to be default, and an
/// app should be able to point it elsewhere by NAME rather than by hand-building a router, which would
/// throw away the shared dead-host cooldown and admission bookkeeping.</para>
///
/// <para><b>Governance is NOT escapable by naming a client.</b> Every named client carries the same
/// front-door decorators (response cache, usage budget, rate limit) and the same refusal screening as the
/// default one. A named client that quietly bypassed the usage budget would be a governance hole that reads
/// as a feature: <b>a name selects BACKENDS, never permissions.</b> Per-name governance is a separate,
/// additive decision.</para>
///
/// <para>Like <see cref="Lyntai.Memory.IMemoryEngineFactory"/> and unlike <c>IHttpClientFactory</c>, the same
/// instance comes back every time — a client is a stateless composition over shared routing state, so there
/// is nothing to create per call. Hence <see cref="Get(string)"/> rather than <c>Create</c>.</para>
/// </summary>
public interface ILlmClientFactory
{
    /// <summary>The client registered under <paramref name="name"/>.</summary>
    /// <param name="name">The client's name, as passed to <c>AddLlmClient</c>.</param>
    /// <exception cref="KeyNotFoundException">No client has that name; the message lists the registered
    /// ones, so a typo is diagnosable without reading the composition root.</exception>
    ILlmClient Get(string name);

    /// <summary>The default client — the container's own <see cref="ILlmClient"/>, the one every existing
    /// call site already resolves. Always available, so a consumer who registers no named client at all can
    /// still depend on this factory.</summary>
    ILlmClient Get();

    /// <summary>Look one up without throwing — for a subsystem that has an OPTIONAL configured client name
    /// and falls back to the default when it is absent.</summary>
    /// <param name="name">The client's name.</param>
    /// <param name="client">The client, when found.</param>
    bool TryGet(string name, out ILlmClient client);

    /// <summary>Every registered name. Does not include the default, which has none.</summary>
    IReadOnlyList<string> Names { get; }
}

/// <summary>The default <see cref="ILlmClientFactory"/> over the registered named compositions.</summary>
/// <param name="named">Name → client, already composed.</param>
/// <param name="fallback">The container's own client, returned by <see cref="Get()"/>.</param>
public sealed class LlmClientFactory(
    IReadOnlyDictionary<string, ILlmClient> named,
    ILlmClient fallback) : ILlmClientFactory
{
    /// <inheritdoc />
    public IReadOnlyList<string> Names { get; } = [.. named.Keys.OrderBy(n => n, StringComparer.Ordinal)];

    /// <inheritdoc />
    public ILlmClient Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return named.TryGetValue(name, out var client)
            ? client
            : throw new KeyNotFoundException(
                Names.Count == 0
                    ? $"No LLM client is named '{name}'. None is registered — call AddLlmClient(name, …)."
                    : $"No LLM client is named '{name}'. Registered: {string.Join(", ", Names)}.");
    }

    /// <inheritdoc />
    public ILlmClient Get() => fallback;

    /// <inheritdoc />
    public bool TryGet(string name, out ILlmClient client)
    {
        ArgumentNullException.ThrowIfNull(name);
        return named.TryGetValue(name, out client!);
    }
}
