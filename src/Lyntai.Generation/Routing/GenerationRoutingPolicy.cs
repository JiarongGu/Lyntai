namespace Lyntai.Generation.Routing;

/// <summary>What the router does with a candidate's failure.</summary>
/// <remarks>Deliberately only the two actions this platform actually implements. Penalty/cooldown
/// bookkeeping (the LLM router's <c>PenalizeAndAdvance</c> / <c>CooldownAndAdvance</c>) arrives with the
/// governance plan — naming them here before they do anything would be a lying API.</remarks>
public enum GenerationFallbackAction
{
    /// <summary>Stop and return this result to the caller.</summary>
    Surface,

    /// <summary>Try the next capable candidate.</summary>
    Advance,
}

/// <summary>Per-verdict fallback behaviour for <see cref="GenerationRouter"/> — a POLICY, not a law.
///
/// The defaults reproduce the semantics the router shipped with: a <see cref="GenerationVerdict.Refused"/>
/// SURFACES (a content refusal is the backend's judgement, and quietly re-submitting the same prompt to
/// another vendor is not a library's decision to make), and everything else advances to the next capable
/// candidate.
///
/// It is configurable because that default is wrong for at least one real setup: a host that deliberately
/// lists a hosted backend AND a locally-run one, where the hosted one refuses content the local one has no
/// policy against. Then <c>On(Refused, Advance)</c> is exactly right — and it is the HOST's call, not
/// Lyntai's. Same reasoning, and the same shape, as the LLM router's policy (<c>docs/DECISIONS.md</c> D10).</summary>
public sealed class GenerationRoutingPolicy
{
    private readonly Dictionary<GenerationVerdict, GenerationFallbackAction> _actions = new()
    {
        // the backend judged the CONTENT, not the transport — surface it
        [GenerationVerdict.Refused] = GenerationFallbackAction.Surface,
        // "not for me" — advance without blame
        [GenerationVerdict.NotConfigured] = GenerationFallbackAction.Advance,
        [GenerationVerdict.Unsupported] = GenerationFallbackAction.Advance,
        // transport/quota/credential faults — another backend may well work
        [GenerationVerdict.RateLimited] = GenerationFallbackAction.Advance,
        [GenerationVerdict.AuthFailed] = GenerationFallbackAction.Advance,
        [GenerationVerdict.Timeout] = GenerationFallbackAction.Advance,
        [GenerationVerdict.Failed] = GenerationFallbackAction.Advance,
    };

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
