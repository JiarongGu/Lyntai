namespace Lyntai.Llm.Routing;

/// <summary>Works out the fallback list a NAMED <see cref="ILlmClient"/> routes over.
///
/// <para><b>Why a name needs its own list at all.</b> A named client narrows the router's PROVIDER set;
/// the candidates a call tries are a separate thing, and through 3.0.2 they always came from
/// <see cref="LyntaiOptions.DefaultCandidates"/>. So a client pooled over one backend, on a host whose
/// defaults named a different one, resolved cleanly and then failed every call — every candidate it tried
/// was absent from its own pool. That is the wiring <see cref="LlmClientBuilder.UseProviders"/> documents,
/// and it could not work as written.</para>
///
/// <para>Internal: which list a composed client ends up with is a property of the wiring, not a service a
/// consumer resolves. Tests reach it through <c>InternalsVisibleTo</c>.</para></summary>
internal static class ClientCandidates
{
    /// <summary>The list, in fallback order.</summary>
    /// <param name="providerIds">The client's pool, in the order <c>UseProviders</c> declared. Empty means
    /// every registered provider, which is the DEFAULT client's own pool — so there is nothing to narrow and
    /// nothing to derive, and <paramref name="defaults"/> stands.</param>
    /// <param name="stated">What <c>UseCandidates</c> said, if anything. Wins outright: a caller who states a
    /// list has answered the question this method exists to answer.</param>
    /// <param name="defaults">The global fallback list.</param>
    internal static IReadOnlyList<LlmCandidate> Resolve(
        IReadOnlyList<string> providerIds,
        IReadOnlyList<LlmCandidate> stated,
        IReadOnlyList<LlmCandidate> defaults)
    {
        if (stated.Count > 0) return [.. stated];
        if (providerIds.Count == 0) return [.. defaults];

        var derived = new List<LlmCandidate>(providerIds.Count);
        foreach (var id in providerIds)
        {
            // Every default entry for this id, not just the first: a global list may legitimately name one
            // backend twice under different models (try the big one, fall back to the small one), and taking
            // one of the pair would silently drop half a configured fallback chain.
            var pinned = defaults.Where(c =>
                string.Equals(c.ProviderId, id, StringComparison.OrdinalIgnoreCase)).ToList();
            derived.AddRange(pinned.Count > 0 ? pinned : [new LlmCandidate(id)]);
        }

        return derived;
    }

    /// <summary>The stated candidates naming a backend outside <paramref name="providerIds"/> — each one a
    /// call that can only ever fail, so the caller hears about it at composition instead of per request.
    /// <para>Takes the RESOLVED pool, not the declared ids: a client that names no provider is pooled over
    /// every registered one, and a stated candidate is then outside it exactly when nothing registered
    /// answers to that id. An empty pool therefore rejects every stated candidate, which is correct — there
    /// is no backend for one to select.</para></summary>
    internal static IReadOnlyList<string> OutsideThePool(
        IReadOnlyList<string> providerIds, IReadOnlyList<LlmCandidate> stated)
    {
        if (stated.Count == 0) return [];
        var pool = new HashSet<string>(providerIds, StringComparer.OrdinalIgnoreCase);
        return [.. stated.Select(c => c.ProviderId).Where(id => !pool.Contains(id)).Distinct(StringComparer.OrdinalIgnoreCase)];
    }
}
