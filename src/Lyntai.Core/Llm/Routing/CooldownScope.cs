namespace Lyntai.Llm.Routing;

/// <summary>The granularity at which the dead-host tracker keys a candidate's cooldown state.</summary>
public enum CooldownScope
{
    /// <summary>Key by provider id alone (default). A downed host is a downed host regardless of
    /// model — the least-surprising behavior and correct for connection/availability faults.</summary>
    Provider,

    /// <summary>Key by (provider id, resolved model). A per-model rate limit (OpenAI quotas are
    /// per-model) then cools only that model, not its siblings on the same host.</summary>
    ProviderAndModel,
}
