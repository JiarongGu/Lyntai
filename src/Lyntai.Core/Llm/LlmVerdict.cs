namespace Lyntai.Llm;

/// <summary>
/// Classification of an LLM call outcome. Drives the router's fallback decision:
/// <list type="bullet">
/// <item><see cref="Failed"/>/<see cref="Timeout"/> — availability problem: count toward the
/// dead-host threshold and advance to the next candidate.</item>
/// <item><see cref="RateLimited"/>/<see cref="AuthFailed"/> — terminal for THIS host (immediate
/// cooldown; re-asking the same window/credentials is always wrong) but transient for the fleet:
/// advance.</item>
/// <item><see cref="ContextWindowExceeded"/> — the request is too big for THIS model, not a host
/// fault: advance with no dead-host penalty (a larger-context candidate is the correct remedy).</item>
/// <item><see cref="Refused"/> — content policy follows the prompt, not the host: surface, never
/// fall back.</item>
/// </list>
/// </summary>
public enum LlmVerdict
{
    Ok,
    RateLimited,
    Refused,
    Failed,
    Timeout,

    /// <summary>The prompt exceeded the model's context window (added 2026-07-17; production
    /// routers key a dedicated fallback list on this — the remedy is a bigger-context candidate).</summary>
    ContextWindowExceeded,

    /// <summary>Authentication/authorization rejected (401/403, invalid key). Terminal per host —
    /// retrying the same credentials never helps — but a fallback candidate may have valid ones.</summary>
    AuthFailed,
}
