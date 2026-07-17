namespace Lyntai.Llm.Routing;

/// <summary>What the router does with a candidate's non-Ok outcome (design §6). The mapping from
/// <see cref="LlmVerdict"/> to one of these is the <see cref="RoutingPolicy"/> — the hard-coded
/// defaults become just the default policy, overridable per consumer.</summary>
public enum FallbackAction
{
    /// <summary>Try the next candidate; do not penalize this host (e.g. the request was too big for
    /// this model — a host fault it isn't).</summary>
    Advance,

    /// <summary>Count one failure toward the dead-host threshold, then advance (transient
    /// availability problems — Failed/Timeout).</summary>
    PenalizeAndAdvance,

    /// <summary>Put this host on immediate cooldown (the same window/credentials won't recover by
    /// retrying), then advance (RateLimited/AuthFailed).</summary>
    CooldownAndAdvance,

    /// <summary>Stop; return this outcome without trying any further candidate (content policy
    /// follows the prompt, not the host — Refused).</summary>
    Surface,
}
