using Lyntai.Agents;

namespace Lyntai.Inference;

/// <summary>
/// Call-site predicates over <see cref="ProviderVerdict"/>, so the common branches read as questions rather
/// than as a chain of enum comparisons. They hang off the ENUM, not off <see cref="TextResponse"/>, because
/// five released types carry a verdict (<see cref="TextResponse"/>, <see cref="TextChunk"/>,
/// <see cref="Agents.SessionEnded"/>, <see cref="Agents.AgentSessionResult"/>,
/// <see cref="Agents.ToolLoopResult"/>) and one definition should serve all of them.
/// <para><b>Why categories and not one method per verdict.</b> <see cref="ProviderVerdict"/> grows —
/// <see cref="ProviderVerdict.NotConfigured"/> was appended after the 1.0 freeze (<c>docs/DECISIONS.md</c> D31)
/// — so an <c>IsRateLimited</c>/<c>IsRefused</c>/… set would make every future member a public-surface
/// addition, and would leave the newest verdict as the only one without a helper. A caller who wants ONE
/// specific verdict already has the clearest possible expression of it: <c>verdict == ProviderVerdict.RateLimited</c>.
/// What that comparison cannot express is a CATEGORY spanning several members, which is what these are.</para>
/// </summary>
public static class ProviderVerdictExtensions
{
    /// <summary>The call produced an answer. Mirrors <see cref="MediaResponse.IsOk"/> — the
    /// same question gets the same name in both domains.</summary>
    public static bool IsOk(this ProviderVerdict verdict) => verdict == ProviderVerdict.Ok;

    /// <summary>Whether this verdict is a BLAME-FREE rejection — the backend declined without anything
    /// being wrong with it. A router keeps these apart from real failures and reports one only when there
    /// was no real failure at all.
    ///
    /// <para>Without the split, <c>[downHost → Failed, neverConfigured → NotConfigured]</c> tells the caller
    /// "not configured" and sends them to set up a key, while the backend they HAD configured is the one
    /// that is down.</para>
    ///
    /// <para><b>Deliberately keyed on the VERDICT, not on the routing action.</b> Keying on "advance" would
    /// also swallow <see cref="ProviderVerdict.ContextWindowExceeded"/>, and "your prompt is too big" is a
    /// real, actionable answer that must still surface.</para>
    ///
    /// <para><b>It decides ELIGIBILITY only, never which failure wins.</b> The two routers differ there on
    /// purpose — <c>TextRouter</c> keeps the LAST substantive failure, <c>GenerationRouter</c> the FIRST,
    /// because the first backend's error explains a media run better. That difference is untouched.</para>
    ///
    /// <para>It is ONE function since <b>D136</b>. Both routers carried a private copy, each docblock
    /// pointing at the other for parity, because the two domains had separate verdict enums — which is
    /// exactly the cost a duplicated taxonomy imposes on everything downstream of it.</para></summary>
    public static bool IsBlameless(this ProviderVerdict verdict) =>
        verdict is ProviderVerdict.NotConfigured or ProviderVerdict.Unsupported;

    /// <summary>Whether re-sending the SAME request may later succeed — the "should I retry?" branch.
    /// True for the availability faults (<see cref="ProviderVerdict.Failed"/>, <see cref="ProviderVerdict.Timeout"/>)
    /// and for <see cref="ProviderVerdict.RateLimited"/>, which recovers on its own once the window rolls (so a
    /// retry needs a DELAY, not an immediate re-send).
    /// <para>False for everything terminal for the request as sent — <see cref="ProviderVerdict.AuthFailed"/>,
    /// <see cref="ProviderVerdict.NotConfigured"/>, <see cref="ProviderVerdict.ContextWindowExceeded"/>,
    /// <see cref="ProviderVerdict.Refused"/>, <see cref="ProviderVerdict.Unsupported"/> — and, conservatively, for any
    /// verdict this build does not know, so an unrecognized value can never provoke a retry loop.</para>
    /// <para><b><see cref="ProviderVerdict.Failed"/> is the classifier's CATCH-ALL, so this over-reports on
    /// purpose.</b> That bucket holds real availability faults (a reset connection, a 502) AND permanent
    /// errors nothing matched, so a 400 whose body fits no pattern reads transient. Kept because
    /// <see cref="RoutingPolicy.Retry(ProviderVerdict, int)"/> re-sends to the same candidate for exactly
    /// <see cref="ProviderVerdict.Failed"/> and <see cref="ProviderVerdict.Timeout"/>, and a predicate that disagreed
    /// with the router about its own retry rule would be worse than one that over-reports. <b>Read it as "a
    /// retry is worth ONE bounded attempt", never as a licence for a loop</b>; where certainty matters, read
    /// the specific verdict.</para>
    /// <para>Deliberately NOT derived from <see cref="RoutingPolicy"/>: that table answers what the
    /// ROUTER does with a candidate, and the two differ (<see cref="ProviderVerdict.RateLimited"/> and
    /// <see cref="ProviderVerdict.AuthFailed"/> share an action there, not here).
    /// <c>LlmVerdictExtensionsTests.Every_verdict_states_whether_it_is_transient</c> fails until a new
    /// member is CLASSIFIED, not merely listed — the obligation D31 places on the policy table.</para></summary>
    public static bool IsTransient(this ProviderVerdict verdict) =>
        verdict is ProviderVerdict.Failed or ProviderVerdict.Timeout or ProviderVerdict.RateLimited;
}
