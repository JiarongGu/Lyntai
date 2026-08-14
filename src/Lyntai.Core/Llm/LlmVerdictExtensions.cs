namespace Lyntai.Llm;

/// <summary>
/// Call-site predicates over <see cref="LlmVerdict"/>, so the common branches read as questions rather
/// than as a chain of enum comparisons. They hang off the ENUM, not off <see cref="LlmReply"/>, because
/// five released types carry a verdict (<see cref="LlmReply"/>, <see cref="LlmChunk"/>,
/// <see cref="Agents.SessionEnded"/>, <see cref="Agents.AgentSessionResult"/>,
/// <see cref="Agents.ToolLoopResult"/>) and one definition should serve all of them.
/// <para><b>Why categories and not one method per verdict.</b> <see cref="LlmVerdict"/> grows —
/// <see cref="LlmVerdict.NotConfigured"/> was appended after the 1.0 freeze (<c>docs/DECISIONS.md</c> D31)
/// — so an <c>IsRateLimited</c>/<c>IsRefused</c>/… set would make every future member a public-surface
/// addition, and would leave the newest verdict as the only one without a helper. A caller who wants ONE
/// specific verdict already has the clearest possible expression of it: <c>verdict == LlmVerdict.RateLimited</c>.
/// What that comparison cannot express is a CATEGORY spanning several members, which is what these are.</para>
/// </summary>
public static class LlmVerdictExtensions
{
    /// <summary>The call produced an answer. Mirrors <c>Lyntai.Generation.GenerationResult.IsOk</c> — the
    /// same question gets the same name in both domains.</summary>
    public static bool IsOk(this LlmVerdict verdict) => verdict == LlmVerdict.Ok;

    /// <summary>Whether re-sending the SAME request may later succeed — the "should I retry?" branch.
    /// True for the availability faults (<see cref="LlmVerdict.Failed"/>, <see cref="LlmVerdict.Timeout"/>)
    /// and for <see cref="LlmVerdict.RateLimited"/>, which recovers on its own once the window rolls (so a
    /// retry needs a DELAY, not an immediate re-send).
    /// <para>False for everything terminal for the request as sent: <see cref="LlmVerdict.AuthFailed"/>
    /// (the same credentials never start working), <see cref="LlmVerdict.NotConfigured"/> (nothing to call
    /// until setup happens), <see cref="LlmVerdict.ContextWindowExceeded"/> (the prompt has to shrink or the
    /// model has to grow), <see cref="LlmVerdict.Refused"/> and <see cref="LlmVerdict.Unsupported"/> (the
    /// answer follows the prompt/capability, not the moment) — and, conservatively, for any verdict this
    /// build does not know, so an unrecognized value can never provoke a retry loop.</para>
    /// <para><b><see cref="LlmVerdict.Failed"/> is the classifier's CATCH-ALL, and counting it as transient
    /// is a deliberate, known over-report.</b> <see cref="LlmVerdictClassifier.FromErrorText(string, LlmVerdict)"/>
    /// falls back to <see cref="LlmVerdict.Failed"/> for any text it does not recognize, so that bucket holds
    /// both real availability faults (a reset connection, a 502, a 503 — the common case, and retryable) and
    /// PERMANENT errors nothing matched: a 400/422 whose body fits no pattern is reported transient and a
    /// retry of it can never succeed. Kept anyway, because the library already takes this position where it
    /// costs something — <see cref="Routing.RoutingPolicy.Retry(LlmVerdict, int)"/> only ever re-sends to the
    /// same candidate for the <see cref="Routing.FallbackAction.PenalizeAndAdvance"/> verdicts, which are
    /// exactly <see cref="LlmVerdict.Failed"/> and <see cref="LlmVerdict.Timeout"/>, and a predicate that
    /// disagreed with the router about its own retry rule would be worse than one that over-reports. So:
    /// treat this as "a retry is worth ONE bounded attempt", never as a licence for an unbounded loop, and
    /// where certainty matters read the specific verdict instead.</para>
    /// <para>Deliberately NOT derived from <see cref="Routing.RoutingPolicy"/> even so: that table answers a
    /// different question (what the ROUTER does with this candidate — <see cref="LlmVerdict.RateLimited"/>
    /// and <see cref="LlmVerdict.AuthFailed"/> share <see cref="Routing.FallbackAction.CooldownAndAdvance"/>
    /// there while differing here). <c>LlmVerdictExtensionsTests.Every_verdict_states_whether_it_is_transient</c>
    /// fails until a newly added member is given a classification — not merely listed — the same obligation
    /// D31 places on the policy table.</para></summary>
    public static bool IsTransient(this LlmVerdict verdict) =>
        verdict is LlmVerdict.Failed or LlmVerdict.Timeout or LlmVerdict.RateLimited;
}
