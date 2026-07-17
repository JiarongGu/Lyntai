namespace Lyntai.Llm;

/// <summary>
/// Classification of an LLM call outcome. Drives the router's fallback decision:
/// <see cref="Failed"/>/<see cref="Timeout"/> advance to the next candidate, <see cref="RateLimited"/>
/// circuit-breaks (stop, surface — resumable later), <see cref="Refused"/> surfaces without fallback
/// (content policy, not availability).
/// </summary>
public enum LlmVerdict
{
    Ok,
    RateLimited,
    Refused,
    Failed,
    Timeout,
}
