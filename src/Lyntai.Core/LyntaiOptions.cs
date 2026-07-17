using System.Globalization;
using Lyntai.Jobs;
using Lyntai.Llm;
using Lyntai.Llm.Routing;

namespace Lyntai;

/// <summary>
/// Library-wide options. Configure in <c>AddLyntai(cfg => …)</c>; after configuration,
/// <c>LYNTAI_*</c> environment variables override what was set in code (env beats config):
/// <c>LYNTAI_TIMEOUT_SECONDS</c>, <c>LYNTAI_DEADHOST_THRESHOLD</c>, <c>LYNTAI_DEADHOST_COOLDOWN_SECONDS</c>,
/// <c>LYNTAI_DEFAULT_CANDIDATES</c> (comma-separated <c>providerId[:model]</c>), <c>LYNTAI_MODEL_&lt;CONSUMER&gt;</c>,
/// <c>LYNTAI_RETRY_FAILED</c>, <c>LYNTAI_RETRY_TIMEOUT</c>, <c>LYNTAI_RETRY_BACKOFF_SECONDS</c>,
/// <c>LYNTAI_COOLDOWN_SCOPE</c> (<c>Provider</c> | <c>ProviderAndModel</c>),
/// <c>LYNTAI_TOOL_LOOP_MAX_ITERATIONS</c>, <c>LYNTAI_CACHE_TTL_SECONDS</c>, <c>LYNTAI_CACHE_MAX_ENTRIES</c>,
/// <c>LYNTAI_BUDGET_MAX_COST_USD</c>, <c>LYNTAI_BUDGET_MAX_TOKENS</c>.
/// </summary>
public sealed class LyntaiOptions
{
    /// <summary>Per-call provider timeout (CLI spawn / HTTP call).</summary>
    public TimeSpan ProviderTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Consecutive failures before a provider/host is marked dead.</summary>
    public int DeadHostThreshold { get; set; } = 3;

    /// <summary>How long a dead provider/host stays out of rotation.</summary>
    public TimeSpan DeadHostCooldown { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The router's fallback policy: per-verdict action, same-candidate retries, cooldown-key
    /// granularity, sole-candidate exemption. Defaults reproduce design §6 — override to tune.</summary>
    public RoutingPolicy Routing { get; } = new();

    /// <summary>Router fallback order used when a caller doesn't pass explicit candidates
    /// (scorers, composition helpers, the Playground).</summary>
    public List<LlmCandidate> DefaultCandidates { get; } = [];

    /// <summary>Default model per consumer tag ("default" applies when the tag has no entry).</summary>
    public Dictionary<string, string> DefaultModelByConsumer { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Max entries kept per (task, scope) in the memory store — oldest trimmed beyond this.</summary>
    public int MemoryCapPerScope { get; set; } = 500;

    /// <summary>Default max entries returned by a memory recall.</summary>
    public int MemoryRecallLimit { get; set; } = 20;

    /// <summary>Default iteration budget for an <see cref="Lyntai.Agents.IToolLoop"/> run (tool
    /// round-trips before it gives up). Per-call override on <c>RunAsync</c>.</summary>
    public int ToolLoopMaxIterations { get; set; } = 8;

    /// <summary>Durable-job tuning (lanes, lease, poll interval, retries).</summary>
    public JobOptions Jobs { get; } = new();

    /// <summary>Response-cache tuning (TTL, size cap) for the opt-in <c>AddResponseCache</c>.</summary>
    public CacheOptions Cache { get; } = new();

    /// <summary>Usage caps (cost/token ceilings) for the opt-in <c>AddUsageBudget</c>.</summary>
    public BudgetOptions Budget { get; } = new();

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
    public void ApplyEnvOverrides(Func<string, string?>? getEnv = null,
        IEnumerable<KeyValuePair<string, string>>? allEnv = null)
    {
        // production reads the real environment (both a single-key getter and a full enumeration for
        // the LYNTAI_MODEL_<CONSUMER> prefix scan); a test that injects getEnv but no allEnv gets an
        // empty enumeration so the real machine env can't leak into the assertion.
        var production = getEnv is null;
        getEnv ??= Environment.GetEnvironmentVariable;
        allEnv ??= production ? EnumerateEnv() : [];

        if (double.TryParse(getEnv("LYNTAI_TIMEOUT_SECONDS"), out var t) && t > 0)
            ProviderTimeout = TimeSpan.FromSeconds(t);

        if (int.TryParse(getEnv("LYNTAI_DEADHOST_THRESHOLD"), out var n) && n > 0)
            DeadHostThreshold = n;

        if (double.TryParse(getEnv("LYNTAI_DEADHOST_COOLDOWN_SECONDS"), out var c) && c > 0)
            DeadHostCooldown = TimeSpan.FromSeconds(c);

        if (int.TryParse(getEnv("LYNTAI_TOOL_LOOP_MAX_ITERATIONS"), out var mi) && mi > 0)
            ToolLoopMaxIterations = mi;

        // durable-job knobs
        if (double.TryParse(getEnv("LYNTAI_JOBS_LEASE_SECONDS"), out var jl) && jl > 0)
            Jobs.Lease = TimeSpan.FromSeconds(jl);
        if (double.TryParse(getEnv("LYNTAI_JOBS_POLL_SECONDS"), out var jp) && jp > 0)
            Jobs.PollInterval = TimeSpan.FromSeconds(jp);
        if (int.TryParse(getEnv("LYNTAI_JOBS_MAX_ATTEMPTS"), out var jma) && jma > 0)
            Jobs.DefaultMaxAttempts = jma;
        if (double.TryParse(getEnv("LYNTAI_JOBS_BACKOFF_SECONDS"), out var jb) && jb >= 0)
            Jobs.RetryBackoff = TimeSpan.FromSeconds(jb);
        if (int.TryParse(getEnv("LYNTAI_JOBS_DEFAULT_CONCURRENCY"), out var jc) && jc > 0)
            Jobs.DefaultLaneConcurrency = jc;

        // response-cache knobs
        if (double.TryParse(getEnv("LYNTAI_CACHE_TTL_SECONDS"), out var ct) && ct >= 0)
            Cache.Ttl = TimeSpan.FromSeconds(ct);
        if (int.TryParse(getEnv("LYNTAI_CACHE_MAX_ENTRIES"), out var cm) && cm > 0)
            Cache.MaxEntries = cm;

        // usage-budget knobs (global caps; per-consumer caps are code-only)
        if (double.TryParse(getEnv("LYNTAI_BUDGET_MAX_COST_USD"), NumberStyles.Float, CultureInfo.InvariantCulture, out var bc) && bc >= 0)
            Budget.MaxCostUsd = bc;
        if (long.TryParse(getEnv("LYNTAI_BUDGET_MAX_TOKENS"), out var bt) && bt >= 0)
            Budget.MaxTokens = bt;

        // routing policy knobs (design §6 is the default; these tune it without code)
        if (int.TryParse(getEnv("LYNTAI_RETRY_FAILED"), out var rf) && rf >= 0)
            Routing.Retry(LlmVerdict.Failed, rf);
        if (int.TryParse(getEnv("LYNTAI_RETRY_TIMEOUT"), out var rt) && rt >= 0)
            Routing.Retry(LlmVerdict.Timeout, rt);
        if (double.TryParse(getEnv("LYNTAI_RETRY_BACKOFF_SECONDS"), out var rb) && rb >= 0)
            Routing.RetryBackoff = TimeSpan.FromSeconds(rb);
        var scope = getEnv("LYNTAI_COOLDOWN_SCOPE");
        if (!string.IsNullOrWhiteSpace(scope) && Enum.TryParse<CooldownScope>(scope, ignoreCase: true, out var cs))
            Routing.CooldownScope = cs;

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

        // LYNTAI_MODEL_<CONSUMER> → per-consumer model (the dictionary is case-insensitive, so an
        // upper-cased env suffix resolves a lower-cased consumer tag like "scoring"). DEFAULT handled above.
        const string modelPrefix = "LYNTAI_MODEL_";
        foreach (var (key, value) in allEnv)
        {
            if (!key.StartsWith(modelPrefix, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(value))
                continue;
            var consumer = key[modelPrefix.Length..];
            if (consumer.Length == 0 || consumer.Equals("DEFAULT", StringComparison.OrdinalIgnoreCase))
                continue;
            DefaultModelByConsumer[consumer] = value;
        }
    }

    private static IEnumerable<KeyValuePair<string, string>> EnumerateEnv()
    {
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
            if (e.Key is string k && e.Value is string v)
                yield return new KeyValuePair<string, string>(k, v);
    }
}
