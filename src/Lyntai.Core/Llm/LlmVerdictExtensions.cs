namespace Lyntai.Llm;

/// <summary>
/// Call-site predicates over <see cref="LlmVerdict"/>, so the common branches read as questions rather
/// than as a chain of enum comparisons. They hang off the ENUM, not off <see cref="LlmReply"/>, because
/// five released types carry a verdict (<see cref="LlmReply"/>, <see cref="LlmChunk"/>,
/// <see cref="Agents.SessionEnded"/>, <see cref="Agents.AgentSessionResult"/>,
/// <see cref="Agents.ToolLoopResult"/>) and one definition should serve all of them.
/// <para><b>Why categories and not one method per verdict.</b> <see cref="LlmVerdict"/> grows —
/// <see cref="LlmVerdict.NotConfigured"/> was appended after the 1.0 freeze (<c>docs/DECISIONS.md</c> D38)
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
    /// <para>Deliberately NOT derived from <see cref="Routing.RoutingPolicy"/>: that table answers a
    /// different question (what the ROUTER does with this candidate — <see cref="LlmVerdict.RateLimited"/>
    /// and <see cref="LlmVerdict.AuthFailed"/> share <see cref="Routing.FallbackAction.CooldownAndAdvance"/>
    /// there while differing here). <c>LlmVerdictExtensionsTests.Every_verdict_is_deliberately_classified</c>
    /// fails if a new member is added without a decision, the same obligation D38 places on the policy
    /// table.</para></summary>
    public static bool IsTransient(this LlmVerdict verdict) =>
        verdict is LlmVerdict.Failed or LlmVerdict.Timeout or LlmVerdict.RateLimited;
}
