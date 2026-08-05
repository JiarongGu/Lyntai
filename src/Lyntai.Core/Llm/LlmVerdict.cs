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
/// <item><see cref="NotConfigured"/> — the backend was never set up, which is not a fault either:
/// advance with no penalty and no cooldown.</item>
/// <item><see cref="Refused"/> — content policy follows the prompt, not the host: surface, never
/// fall back.</item>
/// <item><see cref="Unsupported"/> — a capability/transport gap: surfaces like <see cref="Refused"/>
/// (no fallback, no cooldown) since another candidate has the same limitation, but stays a distinct
/// verdict so telemetry/scorers don't conflate it with a policy refusal.</item>
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

    /// <summary>The provider can't fulfill THIS request shape via THIS path — a capability/transport gap,
    /// not a content-policy refusal and not a host fault (e.g. a native tool call that streaming can't
    /// carry; use <c>CompleteAsync</c>). Surfaces like <see cref="Refused"/> (no fallback/cooldown — another
    /// candidate has the same limitation), but is a DISTINCT verdict so telemetry/scorers don't conflate a
    /// capability gap with a policy refusal.</summary>
    Unsupported,

    /// <summary>The backend was never set up — no credentials to authenticate with, no endpoint to call
    /// (added 2026-08-05; APPENDED so the existing members keep their numeric values). NOT a fault and NOT a
    /// rejected credential: routing advances with no dead-host penalty and no cooldown, and a host can offer
    /// setup instead of reporting an error. The distinction from <see cref="AuthFailed"/> is load-bearing —
    /// a rejected key BENCHES the host for the cooldown window, so a backend a consumer merely LISTED without
    /// configuring would otherwise be penalised on every first attempt for a fact known before the call.
    /// <para>Mirrors <c>Lyntai.Generation.GenerationVerdict.NotConfigured</c>: the same situation gets the
    /// same answer in both domains. See <see cref="LlmVerdictClassifier.FromHttpFailure(System.Net.HttpStatusCode, string, bool)"/>
    /// for how a transport failure reaches it — deliberately NOT "no key means unconfigured", because an
    /// OpenAI-compatible endpoint run locally (LM Studio, vLLM, Ollama) legitimately needs none; only no key
    /// AND a server that demanded one is a configuration gap.</para></summary>
    NotConfigured,
}
