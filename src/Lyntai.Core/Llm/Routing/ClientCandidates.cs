using Lyntai.Inference;

namespace Lyntai.Llm.Routing;

/// <summary>Works out the fallback list a NAMED <see cref="ILlmClient"/> routes over.
///
/// <para><b>Why a name needs its own list at all.</b> A named client narrows the router's PROVIDER set, and
/// the candidates a call tries are a separate thing. Take them from
/// <see cref="LyntaiOptions.DefaultCandidates"/> and a client pooled over one backend, on a host whose
/// defaults name a different one, resolves cleanly and then fails every call — every candidate it tries is
/// absent from its own pool. That is the wiring <see cref="LlmClientBuilder.UseProviders"/> documents, so the
/// two must narrow together.</para>
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
    internal static IReadOnlyList<ProviderCandidate> Resolve(
        IReadOnlyList<string> providerIds,
        IReadOnlyList<ProviderCandidate> stated,
        IReadOnlyList<ProviderCandidate> defaults)
    {
        if (stated.Count > 0) return [.. stated];
        if (providerIds.Count == 0) return [.. defaults];

        var derived = new List<ProviderCandidate>(providerIds.Count);
        foreach (var id in providerIds)
        {
            // Every default entry for this id, not just the first: a global list may legitimately name one
            // backend twice under different models (try the big one, fall back to the small one), and taking
            // one of the pair would silently drop half a configured fallback chain.
            var pinned = defaults.Where(c =>
                string.Equals(c.ProviderId, id, StringComparison.OrdinalIgnoreCase)).ToList();
            derived.AddRange(pinned.Count > 0 ? pinned : [new ProviderCandidate(id)]);
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
        IReadOnlyList<string> providerIds, IReadOnlyList<ProviderCandidate> stated)
    {
        if (stated.Count == 0) return [];
        var pool = new HashSet<string>(providerIds, StringComparer.OrdinalIgnoreCase);
        return [.. stated.Select(c => c.ProviderId).Where(id => !pool.Contains(id)).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Whether a seam asking for <paramref name="model"/> can NEVER get it from
    /// <paramref name="candidates"/> — the router resolves <c>candidate.Model ?? request.Model</c>, so a
    /// seam's model is reachable only through a candidate that pins none.
    ///
    /// <para><b>Deliberately narrow: every candidate pins a model AND none is this one.</b> A single
    /// unpinned candidate makes the request reachable, so a partly-pinned list is NOT a contradiction and
    /// must not be reported as one — it works, just not on every hop. The point of the check is to catch a
    /// configuration that is PROVABLY inert, never one that is merely fragile, because the caller who set
    /// both values meant something and the caller who set one is entitled to keep working.</para>
    ///
    /// <para>An empty list cannot contradict anything: there is no candidate to out-rank the request.</para></summary>
    internal static bool ModelPinIsInert(string model, IReadOnlyList<ProviderCandidate> candidates) =>
        candidates.Count > 0
        && candidates.All(c => !string.IsNullOrWhiteSpace(c.Model))
        && !candidates.Any(c => string.Equals(c.Model, model, StringComparison.OrdinalIgnoreCase));
}
