using Lyntai.Llm;

namespace Lyntai;

/// <summary>
/// Library-wide options. Configure in <c>AddLyntai(cfg => …)</c>; after configuration,
/// <c>LYNTAI_*</c> environment variables override what was set in code (env beats config):
/// <c>LYNTAI_TIMEOUT_SECONDS</c>, <c>LYNTAI_DEADHOST_THRESHOLD</c>, <c>LYNTAI_DEADHOST_COOLDOWN_SECONDS</c>,
/// <c>LYNTAI_DEFAULT_CANDIDATES</c> (comma-separated <c>providerId[:model]</c>), <c>LYNTAI_MODEL_&lt;CONSUMER&gt;</c>.
/// </summary>
public sealed class LyntaiOptions
{
    /// <summary>Per-call provider timeout (CLI spawn / HTTP call).</summary>
    public TimeSpan ProviderTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Consecutive failures before a provider/host is marked dead.</summary>
    public int DeadHostThreshold { get; set; } = 3;

    /// <summary>How long a dead provider/host stays out of rotation.</summary>
    public TimeSpan DeadHostCooldown { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Router fallback order used when a caller doesn't pass explicit candidates
    /// (scorers, composition helpers, the Playground).</summary>
    public List<LlmCandidate> DefaultCandidates { get; } = [];

    /// <summary>Default model per consumer tag ("default" applies when the tag has no entry).</summary>
    public Dictionary<string, string> DefaultModelByConsumer { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Max entries kept per (task, scope) in the memory store — oldest trimmed beyond this.</summary>
    public int MemoryCapPerScope { get; set; } = 500;

    /// <summary>Default max entries returned by a memory recall.</summary>
    public int MemoryRecallLimit { get; set; } = 20;

    /// <summary>Resolve the model for a request: explicit request model wins, then the consumer's
    /// configured default, then the "default" consumer entry, then null (provider default).</summary>
    public string? ResolveModel(string consumer, string? requestModel)
    {
        if (!string.IsNullOrEmpty(requestModel)) return requestModel;
        if (DefaultModelByConsumer.TryGetValue(consumer, out var m)) return m;
        return DefaultModelByConsumer.TryGetValue("default", out var d) ? d : null;
    }

    /// <summary>Apply <c>LYNTAI_*</c> environment overrides. The env getter is injectable so tests
    /// are deterministic; production uses <see cref="Environment.GetEnvironmentVariable(string)"/>.</summary>
    public void ApplyEnvOverrides(Func<string, string?>? getEnv = null)
    {
        getEnv ??= Environment.GetEnvironmentVariable;

        if (double.TryParse(getEnv("LYNTAI_TIMEOUT_SECONDS"), out var t) && t > 0)
            ProviderTimeout = TimeSpan.FromSeconds(t);

        if (int.TryParse(getEnv("LYNTAI_DEADHOST_THRESHOLD"), out var n) && n > 0)
            DeadHostThreshold = n;

        if (double.TryParse(getEnv("LYNTAI_DEADHOST_COOLDOWN_SECONDS"), out var c) && c > 0)
            DeadHostCooldown = TimeSpan.FromSeconds(c);

        var candidates = getEnv("LYNTAI_DEFAULT_CANDIDATES");
        if (!string.IsNullOrWhiteSpace(candidates))
        {
            DefaultCandidates.Clear();
            foreach (var part in candidates.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var sep = part.IndexOf(':');
                DefaultCandidates.Add(sep < 0
                    ? new LlmCandidate(part)
                    : new LlmCandidate(part[..sep], part[(sep + 1)..]));
            }
        }

        var defaultModel = getEnv("LYNTAI_MODEL_DEFAULT") ?? getEnv("LYNTAI_DEFAULT_MODEL");
        if (!string.IsNullOrWhiteSpace(defaultModel))
            DefaultModelByConsumer["default"] = defaultModel;
    }
}
