namespace Lyntai.Inference;

/// <summary>Why a backend call ended the way it did — ONE taxonomy for every domain this library routes
/// over, read by every router to decide fallback.
///
/// <para><b>What a verdict MEANS is shared; what a router DOES about it is not.</b> <see cref="RoutingPolicy"/>
/// is the default action table every router starts from — it surfaces <see cref="Unsupported"/> — and
/// <c>GenerationRoutingPolicy</c> is the media domain's own, which advances on it instead. Never read a
/// verdict as a promise about what happens next; read the policy. Why it is named for no domain:
/// <c>docs/DECISIONS.md</c> D136.</para>
///
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
/// <item><see cref="Unsupported"/> — a capability/transport gap, kept distinct from
/// <see cref="Refused"/> so telemetry and scorers do not conflate the two.</item>
/// </list>
/// </summary>
public enum ProviderVerdict
{
    Ok,
    RateLimited,
    Refused,
    Failed,
    Timeout,

    /// <summary>The prompt exceeded the model's context window; the remedy is a bigger-context candidate.
    /// <para>Reachable in every domain, though only text backends raise it today. A policy with no entry for
    /// it advances without penalty, which is the right answer everywhere: too big for THIS backend is a
    /// capability gap, not ill health.</para></summary>
    ContextWindowExceeded,

    /// <summary>Authentication/authorization rejected (401/403, invalid key). Terminal per host —
    /// retrying the same credentials never helps — but a fallback candidate may have valid ones.</summary>
    AuthFailed,

    /// <summary>The backend can't fulfill THIS request shape via THIS path — a capability/transport gap,
    /// not a content-policy refusal and not a host fault (a native tool call streaming can't carry; a medium
    /// a renderer does not serve). A DISTINCT verdict so telemetry/scorers don't conflate a capability gap
    /// with a policy refusal.</summary>
    Unsupported,

    /// <summary>The backend was never set up — no credentials to authenticate with, no endpoint to call.
    /// NOT a fault and NOT a rejected credential: routing advances with no dead-host penalty and no
    /// cooldown, and a host can offer setup instead of reporting an error. The distinction from
    /// <see cref="AuthFailed"/> is load-bearing — a rejected key BENCHES the host for the cooldown window,
    /// so a backend a consumer merely LISTED without configuring would otherwise be penalised on every first
    /// attempt for a fact known before the call.
    /// <para>See <see cref="ProviderVerdictClassifier.FromHttpFailure(System.Net.HttpStatusCode, string, bool)"/>
    /// for how a transport failure reaches it — deliberately NOT "no key means unconfigured", because an
    /// endpoint run locally (LM Studio, vLLM, Ollama, llama-server) legitimately needs none; only no key
    /// AND a server that demanded one is a configuration gap.</para></summary>
    NotConfigured,
}
