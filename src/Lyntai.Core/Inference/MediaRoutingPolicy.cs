
namespace Lyntai.Inference;


/// <summary>Per-verdict fallback behaviour for <see cref="MediaRouter"/> — a POLICY, not a law.
///
/// The defaults follow the SHAPE of <see cref="TextRouter"/>'s (design §6), so one mental model carries across most of
/// both domains: a <see cref="ProviderVerdict.Refused"/> SURFACES (a content refusal is the backend's
/// judgement, and quietly re-submitting the same prompt to another vendor is not a library's decision to make),
/// a rate limit or a rejected key BENCHES the backend, a transient fault counts toward the threshold, and a
/// backend that was never set up advances without blame. The deliberate divergence is
/// <see cref="ProviderVerdict.Unsupported"/>: a capability gap ADVANCES here, where <see cref="RoutingPolicy"/> surfaces
/// it. <see cref="ProviderVerdictClassifier"/> carries the reason — chat candidates share a capability gap,
/// media backends differ widely in what they accept.
///
/// It is configurable because that Refused default is wrong for at least one real setup: a host that
/// deliberately lists a hosted backend AND a locally-run one, where the hosted one refuses content the local
/// one has no policy against. Then <c>On(Refused, Advance)</c> is exactly right — and it is the HOST's call,
/// not Lyntai's. Same reasoning, and the same shape, as <see cref="TextRouter"/>'s policy
/// (<c>docs/DECISIONS.md</c> D3).</summary>
public sealed class MediaRoutingPolicy
{
    private readonly Dictionary<ProviderVerdict, FallbackAction> _actions = new()
    {
        // the backend judged the CONTENT, not the transport — surface it
        [ProviderVerdict.Refused] = FallbackAction.Surface,
        // "not for me" — advance without blame
        [ProviderVerdict.NotConfigured] = FallbackAction.Advance,
        [ProviderVerdict.Unsupported] = FallbackAction.Advance,
        // too big for THIS backend is a capability gap, not ill health — explicit rather than left to the
        // unmapped default, since a silent default is how it came to PENALIZE a healthy backend before
        [ProviderVerdict.ContextWindowExceeded] = FallbackAction.Advance,
        // the backend told us to stop: benching is the only response that doesn't waste a request
        [ProviderVerdict.RateLimited] = FallbackAction.CooldownAndAdvance,
        [ProviderVerdict.AuthFailed] = FallbackAction.CooldownAndAdvance,
        // might be transient — one is noise, several in a row is a dead backend
        [ProviderVerdict.Timeout] = FallbackAction.PenalizeAndAdvance,
        [ProviderVerdict.Failed] = FallbackAction.PenalizeAndAdvance,
    };

    /// <summary>Never skip the ONLY capable candidate for being benched (default true). Benching the sole
    /// option converts a real, actionable verdict ("rate limited, try in a minute") into a synthetic one
    /// ("no capable backend"), which is strictly less useful to the host — and if the cooldown was stale, the
    /// attempt succeeds.</summary>
    public bool ExemptSoleCandidate { get; set; } = true;

    /// <summary>What to do about <paramref name="verdict"/>. Unknown verdicts advance — a verdict this policy
    /// has never heard of should not silently end a run that another candidate could serve.</summary>
    public FallbackAction ActionFor(ProviderVerdict verdict) =>
        _actions.TryGetValue(verdict, out var action) ? action : FallbackAction.Advance;

    /// <summary>Set the action for a verdict. Fluent, so a host can chain a couple of overrides.</summary>
    public MediaRoutingPolicy On(ProviderVerdict verdict, FallbackAction action)
    {
        _actions[verdict] = action;
        return this;
    }
}
