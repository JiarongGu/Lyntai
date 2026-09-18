namespace Lyntai.Inference;

/// <summary>What a router does with a candidate's non-Ok outcome — ONE vocabulary for every domain, as
/// <see cref="ProviderVerdict"/> is.
///
/// <para><b>The mapping is the POLICY; these are the moves it can choose from.</b> Each domain keeps its own
/// table — <c>RoutingPolicy</c> surfaces <see cref="ProviderVerdict.Unsupported"/> where
/// <c>MediaRoutingPolicy</c> advances on it, and their defaults for an UNMAPPED verdict differ too. That
/// is table content, not vocabulary: **D139** merged the table's key and left its value duplicated, which is
/// the same shape one layer down (<c>docs/DECISIONS.md</c> D140).</para></summary>
public enum FallbackAction
{
    /// <summary>Try the next candidate; do not penalize this backend — it simply was not the one for this
    /// request (the prompt was too big for this model, or it does not serve this shape).</summary>
    Advance,

    /// <summary>Count one failure toward the dead-host threshold, then advance — for faults that MIGHT be
    /// transient (a dropped connection, a render that timed out). Repeated ones bench the backend.</summary>
    PenalizeAndAdvance,

    /// <summary>Bench this backend immediately for the cooldown window, then advance — for a backend that has
    /// TOLD us retrying now cannot work (a 429, a rejected key). Re-asking inside the window spends a request
    /// to be refused again.</summary>
    CooldownAndAdvance,

    /// <summary>Stop; return this outcome without trying any further candidate — a content refusal is the
    /// backend's judgement about the prompt, and quietly re-submitting it to another vendor is not a
    /// library's decision to make.</summary>
    Surface,
}
