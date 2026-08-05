namespace Lyntai.Llm.Routing;

/// <summary>Drops repeat (ProviderId, Model) candidates — first wins, order preserved — so a
/// misconfigured list that re-prepends the primary never retries it. Internal: a router
/// implementation detail, not consumer surface (tests reach it via InternalsVisibleTo).
///
/// <para>The generation router shares this through the generic <c>Dedup&lt;T&gt;</c> rather than growing its own
/// copy, for the same reason both domains share <see cref="DeadHostTracker"/>: "the same backend named twice is
/// one candidate" is list bookkeeping over a pair of strings, not an LLM concept, and a second copy would be a
/// second set of bugs. The generic overload exists because generation dedups the RESOLVED pair — the provider
/// instance an id actually selected, and the model after the candidate's override has been applied to the
/// request — which is not an <see cref="LlmCandidate"/> at all.</para>
///
/// <para><b>The provider-id half compares case-INSENSITIVELY</b>, matching how every other id in the tree is
/// matched (<c>GenerationRouter</c>, <c>ProviderPoolGuard</c>, <c>IToolRegistry</c>,
/// <c>IJobHandlerRegistry</c>, <c>BoundedProviderPool</c>) and how <see cref="LlmRouter"/> resolves one.
/// Otherwise <c>[openai, OpenAI]</c> would survive dedup as two candidates that both select the one provider
/// — re-attempting a backend that just failed, and inflating the count that
/// <see cref="RoutingPolicy.ExemptSoleCandidate"/> reads. The MODEL half stays ORDINAL: a model id is a
/// vendor's opaque string this library does not own, and two casings of one are not reliably the same
/// endpoint.</para></summary>
internal static class CandidateDedup
{
    public static IReadOnlyList<LlmCandidate> Dedup(IEnumerable<LlmCandidate> candidates) =>
        Dedup(candidates, c => (c.ProviderId, c.Model));

    /// <summary>Dedup any sequence on the (provider id, model) pair <paramref name="key"/> projects out —
    /// first wins, order preserved. The caller chooses WHICH pair: the candidate as written, or the pair it
    /// resolved to.</summary>
    public static IReadOnlyList<T> Dedup<T>(
        IEnumerable<T> items, Func<T, (string ProviderId, string? Model)> key)
    {
        var seen = new HashSet<(string ProviderId, string? Model)>(PairComparer.Instance);
        var result = new List<T>();
        foreach (var item in items)
        {
            if (seen.Add(key(item))) result.Add(item);
        }
        return result;
    }

    /// <summary>Case-insensitive on the id, ordinal on the model — see the type doc for why the two halves
    /// differ. Nulls are tolerated rather than thrown on: a hand-built candidate list is consumer input, and
    /// the tuple's default comparer used to accept them.</summary>
    private sealed class PairComparer : IEqualityComparer<(string ProviderId, string? Model)>
    {
        public static readonly PairComparer Instance = new();

        public bool Equals((string ProviderId, string? Model) x, (string ProviderId, string? Model) y) =>
            string.Equals(x.ProviderId, y.ProviderId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Model, y.Model, StringComparison.Ordinal);

        public int GetHashCode((string ProviderId, string? Model) pair) => HashCode.Combine(
            pair.ProviderId is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(pair.ProviderId),
            pair.Model is null ? 0 : StringComparer.Ordinal.GetHashCode(pair.Model));
    }
}
