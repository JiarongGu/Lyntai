namespace Lyntai.Generation.Routing;

/// <summary>What the router does with a candidate's failure. The same four actions as the LLM router's
/// <c>FallbackAction</c>, because the question is the same one: advance, advance and remember, bench, or
/// stop.</summary>
public enum GenerationFallbackAction
{
    /// <summary>Stop and return this result to the caller.</summary>
    Surface,

    /// <summary>Try the next capable candidate; do not hold it against this backend (it simply wasn't the one
    /// for this request).</summary>
    Advance,

    /// <summary>Count one failure toward the dead-host threshold, then advance — for faults that MIGHT be
    /// transient (a dropped connection, a render that timed out). Repeated ones bench the backend.</summary>
    PenalizeAndAdvance,

    /// <summary>Bench this backend immediately for the cooldown window, then advance — for a backend that has
    /// TOLD us retrying now cannot work (a 429, a rejected key). Re-asking inside the window spends a request
    /// to be refused again.</summary>
    CooldownAndAdvance,
}

/// <summary>Per-verdict fallback behaviour for <see cref="GenerationRouter"/> — a POLICY, not a law.
///
/// The defaults follow the SHAPE of the LLM router's (design §6), so one mental model carries across most of
/// both domains: a <see cref="GenerationVerdict.Refused"/> SURFACES (a content refusal is the backend's
/// judgement, and quietly re-submitting the same prompt to another vendor is not a library's decision to make),
/// a rate limit or a rejected key BENCHES the backend, a transient fault counts toward the threshold, and a
/// backend that was never set up advances without blame. The deliberate divergence is
/// <see cref="GenerationVerdict.Unsupported"/>: a capability gap ADVANCES here, where the LLM policy surfaces
/// it. <see cref="GenerationVerdictClassifier"/> carries the reason — chat candidates share a capability gap,
/// media backends differ widely in what they accept.
///
/// It is configurable because that Refused default is wrong for at least one real setup: a host that
/// deliberately lists a hosted backend AND a locally-run one, where the hosted one refuses content the local
/// one has no policy against. Then <c>On(Refused, Advance)</c> is exactly right — and it is the HOST's call,
/// not Lyntai's. Same reasoning, and the same shape, as the LLM router's policy
/// (<c>docs/DECISIONS.md</c> D10).</summary>
public sealed class GenerationRoutingPolicy
{
    private readonly Dictionary<GenerationVerdict, GenerationFallbackAction> _actions = new()
    {
        // the backend judged the CONTENT, not the transport — surface it
        [GenerationVerdict.Refused] = GenerationFallbackAction.Surface,
        // "not for me" — advance without blame
        [GenerationVerdict.NotConfigured] = GenerationFallbackAction.Advance,
        [GenerationVerdict.Unsupported] = GenerationFallbackAction.Advance,
        // the backend told us to stop: benching is the only response that doesn't waste a request
        [GenerationVerdict.RateLimited] = GenerationFallbackAction.CooldownAndAdvance,
        [GenerationVerdict.AuthFailed] = GenerationFallbackAction.CooldownAndAdvance,
        // might be transient — one is noise, several in a row is a dead backend
        [GenerationVerdict.Timeout] = GenerationFallbackAction.PenalizeAndAdvance,
        [GenerationVerdict.Failed] = GenerationFallbackAction.PenalizeAndAdvance,
    };

    /// <summary>Never skip the ONLY capable candidate for being benched (default true). Benching the sole
    /// option converts a real, actionable verdict ("rate limited, try in a minute") into a synthetic one
    /// ("no capable backend"), which is strictly less useful to the host — and if the cooldown was stale, the
    /// attempt succeeds.</summary>
    public bool ExemptSoleCandidate { get; set; } = true;

    /// <summary>What to do about <paramref name="verdict"/>. Unknown verdicts advance — a verdict this policy
    /// has never heard of should not silently end a run that another candidate could serve.</summary>
    public GenerationFallbackAction ActionFor(GenerationVerdict verdict) =>
        _actions.TryGetValue(verdict, out var action) ? action : GenerationFallbackAction.Advance;

    /// <summary>Set the action for a verdict. Fluent, so a host can chain a couple of overrides.</summary>
    public GenerationRoutingPolicy On(GenerationVerdict verdict, GenerationFallbackAction action)
    {
        _actions[verdict] = action;
        return this;
    }
}
