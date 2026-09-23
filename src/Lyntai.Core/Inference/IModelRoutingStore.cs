using Lyntai.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Inference;

/// <summary>
/// A consumer's LIVE route, read fresh on each call — so an admin can move a consumer to another backend and
/// model WITHOUT a restart. Opt in with <c>AddLiveModelRouting()</c>; without it every call routes over the
/// candidates it was given.
/// <para>A route is a whole fallback list of <see cref="ProviderCandidate"/> pairs. When one is set the router
/// uses it IN PLACE of the given candidates, and each entry carries its own model, so a fallback backend is
/// never asked for another backend's model. An entry resolves its model as a given candidate does: its own,
/// else the request's, else the consumer's configured default
/// (<see cref="LyntaiOptions.ResolveModel(string,string)"/>). A route naming no provider the router knows is
/// ignored, with a warning, and the given candidates serve.</para>
/// </summary>
public interface IModelRoutingStore
{
    /// <summary>The live route for <paramref name="consumer"/>, in fallback order — EMPTY when none is set,
    /// which leaves the given candidates in force. The router and the response cache each read it once per call.
    /// <para>Fail open: a fault should read as no route, since a throw here fails the call that asked. Only the
    /// caller's own cancellation should escape.</para></summary>
    Task<IReadOnlyList<ProviderCandidate>> GetRouteAsync(string consumer, CancellationToken ct = default);
}

/// <summary>KV-backed <see cref="IModelRoutingStore"/>, reading the registered <see cref="IKeyValueStore"/> on each
/// call. <c>&lt;prefix&gt;&lt;consumer&gt;</c> holds the route as comma-separated candidate specs — e.g.
/// <c>lyntai.route.memory</c> = <c>llama:qwen3-4b-gguf, claude:haiku</c> — each <c>provider</c> or
/// <c>provider:model</c>, split at the FIRST colon as every candidate spec in the library is (so
/// <c>ollama:qwen3:4b</c> is <c>ollama</c> serving <c>qwen3:4b</c>). A blank value is no route, a blank entry is
/// skipped, and an entry naming no provider is skipped with a warning.
/// <para>Fail-open: no store yields no route, and a store fault logs a warning and yields none; only the
/// caller's cancellation propagates.</para>
/// <para>A key under <c>lyntai.model.</c> — whose values name a model, not a route — is never read; the first
/// call that finds any logs one warning.</para></summary>
public sealed class KeyValueModelRoutingStore(
    IKeyValueStore? kv = null, ILogger<KeyValueModelRoutingStore>? logger = null, string? keyPrefix = null) : IModelRoutingStore
{
    /// <summary>Default KV key prefix for a consumer's route (e.g. <c>lyntai.route.scoring</c>).</summary>
    public const string DefaultKeyPrefix = "lyntai.route.";

    private const string ModelOnlyKeyPrefix = "lyntai.model.";

    /// <summary>The KV key namespace this store reads routes under. Defaults to <see cref="DefaultKeyPrefix"/>;
    /// set an app's OWN namespace (e.g. <c>llm.route.</c>) to point live routing at the app's existing keys — no
    /// shim, no duplication — whose values must then be routes.</summary>
    public string KeyPrefix { get; } = keyPrefix ?? DefaultKeyPrefix;
    private readonly ILogger _logger = logger ?? NullLogger<KeyValueModelRoutingStore>.Instance;

    // 1 once the lyntai.model. check has run, or while it runs; a failed check resets it so a later call asks again
    private int _modelOnlyKeysChecked;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ProviderCandidate>> GetRouteAsync(string consumer, CancellationToken ct = default)
    {
        if (kv is null) return [];
        IReadOnlyList<ProviderCandidate> route;
        try
        {
            route = Parse(consumer, await kv.GetAsync(KeyPrefix + consumer, ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "live route read failed for consumer {Consumer}; routing over the given candidates", consumer);
            route = []; // fail-open — never sink a request because the route lookup faulted
        }
        await WarnOfModelOnlyKeysOnceAsync(kv, ct).ConfigureAwait(false);
        return route;
    }

    private List<ProviderCandidate> Parse(string consumer, string? value)
    {
        var route = new List<ProviderCandidate>();
        if (string.IsNullOrWhiteSpace(value)) return route;
        foreach (var entry in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = ProviderCandidateSpec.Parse(entry);
            if (candidate.ProviderId.Length > 0) route.Add(candidate);
            else _logger.LogWarning("live route for consumer {Consumer}: entry '{Entry}' names no provider; skipped", consumer, entry);
        }
        return route;
    }

    /// <summary>Its own step after the route read, so a failed listing never costs the route.</summary>
    private async Task WarnOfModelOnlyKeysOnceAsync(IKeyValueStore store, CancellationToken ct)
    {
        if (KeyPrefix == ModelOnlyKeyPrefix || Interlocked.Exchange(ref _modelOnlyKeysChecked, 1) == 1) return;
        var checkedOk = false;
        try
        {
            var keys = await store.ListKeysAsync(ModelOnlyKeyPrefix, ct).ConfigureAwait(false);
            if (keys.Any(k => k.StartsWith(ModelOnlyKeyPrefix, StringComparison.Ordinal)))
                _logger.LogWarning(
                    "live routing: keys remain under {ModelOnlyPrefix}; their values name a model alone and are not read — write each consumer's route as {RoutePrefix}<consumer> = provider:model[, provider:model…]",
                    ModelOnlyKeyPrefix, KeyPrefix);
            checkedOk = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "live routing: could not list {ModelOnlyPrefix} keys; will ask again", ModelOnlyKeyPrefix);
        }
        finally
        {
            if (!checkedOk) Volatile.Write(ref _modelOnlyKeysChecked, 0);
        }
    }
}
