namespace Lyntai.Llm.Routing;

/// <summary>
/// The router's fallback policy: how each <see cref="LlmVerdict"/> maps to a <see cref="FallbackAction"/>,
/// how many times to retry the same candidate before advancing, the cooldown-key granularity, and
/// whether to exempt a sole candidate from cooldown. The defaults reproduce design §6 exactly, so a
/// consumer that never touches this gets the documented behavior; overriding turns the hard-coded
/// policy into just the default.
/// </summary>
public sealed class RoutingPolicy
{
    // Defaults mirror the pre-policy router switch: Failed/Timeout penalize + advance, RateLimited/
    // AuthFailed cool + advance, ContextWindowExceeded advance (not a host fault), Refused surface.
    private readonly Dictionary<LlmVerdict, FallbackAction> _actions = new()
    {
        [LlmVerdict.Failed] = FallbackAction.PenalizeAndAdvance,
        [LlmVerdict.Timeout] = FallbackAction.PenalizeAndAdvance,
        [LlmVerdict.RateLimited] = FallbackAction.CooldownAndAdvance,
        [LlmVerdict.AuthFailed] = FallbackAction.CooldownAndAdvance,
        [LlmVerdict.ContextWindowExceeded] = FallbackAction.Advance,
        [LlmVerdict.Refused] = FallbackAction.Surface,
    };

    private readonly Dictionary<LlmVerdict, int> _retries = [];

    /// <summary>Dead-host key granularity (default <see cref="CooldownScope.Provider"/>).</summary>
    public CooldownScope CooldownScope { get; set; } = CooldownScope.Provider;

    /// <summary>Never skip the ONLY configured candidate for being on cooldown (default true).
    /// Benching the sole option just converts "try and maybe succeed / get a real error" into an
    /// instant synthetic failure — LiteLLM exempts single-deployment groups for the same reason.</summary>
    public bool ExemptSoleCandidate { get; set; } = true;

    /// <summary>Delay between same-candidate retries (default zero — no backoff). Applies only to
    /// verdicts with a configured retry count.</summary>
    public TimeSpan RetryBackoff { get; set; } = TimeSpan.Zero;

    /// <summary>The action for a verdict; unmapped verdicts fall back to
    /// <see cref="FallbackAction.PenalizeAndAdvance"/> (treat the unknown as a transient fault).</summary>
    public FallbackAction ActionFor(LlmVerdict verdict) =>
        _actions.TryGetValue(verdict, out var a) ? a : FallbackAction.PenalizeAndAdvance;

    /// <summary>Same-candidate retries before advancing for this verdict (default 0 = immediate
    /// advance). Retries only make sense for transient faults; a cooled/surfaced verdict ignores it.</summary>
    public int RetriesFor(LlmVerdict verdict) =>
        _retries.TryGetValue(verdict, out var r) ? r : 0;

    /// <summary>Override the action for a verdict.</summary>
    public RoutingPolicy On(LlmVerdict verdict, FallbackAction action)
    {
        _actions[verdict] = action;
        return this;
    }

    /// <summary>Retry the same candidate up to <paramref name="count"/> times on this verdict before
    /// advancing (a single transient blip shouldn't fail over). Only honored for verdicts whose
    /// action advances after penalizing — a cooled or surfaced verdict never retries the same host.</summary>
    public RoutingPolicy Retry(LlmVerdict verdict, int count)
    {
        _retries[verdict] = count < 0 ? 0 : count;
        return this;
    }

    /// <summary>True when this verdict should retry the SAME candidate again — only transient
    /// availability faults (<see cref="FallbackAction.PenalizeAndAdvance"/>) with retry budget left.
    /// A cooled, surfaced, or plain-advance verdict never retries the same host (retrying a
    /// too-big-context request or a rate-limited window on the same model can't help).</summary>
    internal bool ShouldRetrySameCandidate(LlmVerdict verdict, int attemptsSoFar) =>
        ActionFor(verdict) == FallbackAction.PenalizeAndAdvance
        && attemptsSoFar <= RetriesFor(verdict);
}
