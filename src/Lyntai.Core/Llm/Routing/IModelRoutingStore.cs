using Lyntai.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Llm.Routing;

/// <summary>
/// A LIVE per-consumer model override, read fresh each call — so an admin retune takes effect WITHOUT a
/// restart (the model analogue of a prompt override). Opt in with <c>AddLiveModelRouting()</c>; without it
/// the router resolves models from code/env config as before. Consulted BELOW an explicit request/candidate
/// model but ABOVE the code/env per-consumer default (see <see cref="LyntaiOptions.ResolveModel(string,string,string)"/>).
/// </summary>
public interface IModelRoutingStore
{
    /// <summary>The live model override for <paramref name="consumer"/>, or null when none is set.</summary>
    Task<string?> GetModelOverrideAsync(string consumer, CancellationToken ct = default);
}

/// <summary>KV-backed <see cref="IModelRoutingStore"/>: reads <c>lyntai.model.&lt;consumer&gt;</c> from the
/// registered <see cref="IKeyValueStore"/> each call (fail-open — a store outage or no store yields no
/// override). Mirrors how <c>PromptRegistry</c> reads a live prompt override.</summary>
public sealed class KeyValueModelRoutingStore(
    IKeyValueStore? kv = null, ILogger<KeyValueModelRoutingStore>? logger = null, string? keyPrefix = null) : IModelRoutingStore
{
    /// <summary>Default KV key prefix for a per-consumer model override (e.g. <c>lyntai.model.scoring</c>).</summary>
    public const string DefaultKeyPrefix = "lyntai.model.";

    /// <summary>The KV key namespace this store reads model overrides under. Defaults to
    /// <see cref="DefaultKeyPrefix"/>; set an app's OWN namespace (e.g. <c>llm.model.</c>) to point Lyntai's
    /// live model routing at the app's existing keys — no shim, no duplication.</summary>
    public string KeyPrefix { get; } = keyPrefix ?? DefaultKeyPrefix;
    private readonly ILogger _logger = logger ?? NullLogger<KeyValueModelRoutingStore>.Instance;

    public async Task<string?> GetModelOverrideAsync(string consumer, CancellationToken ct = default)
    {
        if (kv is null) return null;
        try
        {
            var v = await kv.GetAsync(KeyPrefix + consumer, ct).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "live model override read failed for consumer {Consumer}; using the configured default", consumer);
            return null; // fail-open — never sink a request because the override lookup faulted
        }
    }
}
