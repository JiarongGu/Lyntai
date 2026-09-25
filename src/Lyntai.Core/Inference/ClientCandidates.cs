
namespace Lyntai.Inference;

/// <summary>Works out the fallback list a NAMED <see cref="ITextClient"/> routes over, and holds the rules a
/// text candidate list is judged by — at composition and by <see cref="TextRouter"/> per call: a model pin that
/// can never take effect, and a backend that serves no text.
///
/// <para><b>Why a name needs its own list at all.</b> A named client narrows the router's PROVIDER set, and
/// the candidates a call tries are a separate thing. Take them from
/// <see cref="LyntaiOptions.DefaultCandidates"/> and a client pooled over one backend, on a host whose
/// defaults name a different one, resolves cleanly and then fails every call — every candidate it tries is
/// absent from its own pool. That is the wiring <see cref="TextClientBuilder.UseProviders"/> documents, so the
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

    /// <summary>Whether a backend can serve a TEXT call: it declares <see cref="ProviderKinds.Text"/> among what
    /// it produces. The one rule the router's live-route filter, its per-call skip and the composition check
    /// all read.</summary>
    internal static bool ServesText(IModelProvider provider) =>
        provider.Capabilities.Produces.Contains(ProviderKinds.Text, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a backend serves a text call on <paramref name="door"/>: it produces text AND declares
    /// that operation. What the router asks per door, so a Complete-only backend is never asked to stream.</summary>
    internal static bool Serves(IModelProvider provider, ProviderOperation door) =>
        ServesText(provider) && provider.Capabilities.Operations.Contains(door);

    /// <summary>What a backend declares it produces, for a message: "nothing" when it declares no kind at all.</summary>
    internal static string Produces(IModelProvider provider) =>
        provider.Capabilities.Produces.Count == 0 ? "nothing" : string.Join(" and ", provider.Capabilities.Produces);

    /// <summary>The candidates of a CONFIGURED text list that name a registered backend serving no text, each
    /// as its spec, what it produces, and whether it declares no kind at all — a call to one can never be
    /// served, so the caller hears about it at composition.
    /// <para>A candidate naming NO registered backend is not reported: an adapter package may be absent in one
    /// environment, and the router skips it per call.</para></summary>
    /// <param name="candidates">The list, as the client routes over it.</param>
    /// <param name="providers">The backends it routes over; the first under an id wins, as in the router.</param>
    internal static IReadOnlyList<(string Candidate, string Produces, bool DeclaresNothing)> ServingNoText(
        IReadOnlyList<ProviderCandidate> candidates, IEnumerable<IModelProvider> providers)
    {
        var byId = new Dictionary<string, IModelProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in providers) byId.TryAdd(p.Id, p);
        return [.. CandidateDedup.Dedup(candidates)
            .Select(c => (Candidate: c, Provider: byId.GetValueOrDefault(c.ProviderId)))
            .Where(x => x.Provider is not null && !ServesText(x.Provider))
            .Select(x => (ProviderCandidateSpec.Format(x.Candidate), Produces(x.Provider!),
                x.Provider!.Capabilities.Produces.Count == 0))];
    }
}
