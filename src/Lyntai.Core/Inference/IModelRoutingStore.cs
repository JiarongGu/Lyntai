using System.Collections.ObjectModel;
using Lyntai.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Inference;

/// <summary>
/// A LIVE per-consumer model override, read fresh each call — so an admin retune takes effect WITHOUT a
/// restart (the model analogue of a prompt override). Opt in with <c>AddLiveModelRouting()</c>; without it
/// the router resolves models from code/env config as before.
/// <para>An override is consumer-wide, or SCOPED to one provider id (<see cref="ModelOverrides"/>). A model id
/// belongs to one backend, so each candidate takes its own provider's scoped entry, else the consumer-wide
/// one — a fallback backend is never handed a model scoped to another.</para>
/// <para>Precedence, per candidate: the candidate's or request's own model → the live override for that
/// candidate's provider → the configured per-consumer default → the <c>"default"</c> entry (see
/// <see cref="LyntaiOptions.ResolveModel(string,string,string)"/>).</para>
/// </summary>
public interface IModelRoutingStore
{
    /// <summary>The consumer-wide live override for <paramref name="consumer"/>, or null when none is set.</summary>
    Task<string?> GetModelOverrideAsync(string consumer, CancellationToken ct = default);

    /// <summary>Every live override for <paramref name="consumer"/> — the consumer-wide one and each
    /// provider-scoped one — as one set; the router and the response cache read it once per call. The default
    /// body serves <see cref="GetModelOverrideAsync"/> as <see cref="ModelOverrides.Any"/> with no scoped
    /// entries, so a store that does not scope by provider need not implement it.</summary>
    async Task<ModelOverrides> GetModelOverridesAsync(string consumer, CancellationToken ct = default) =>
        new(await GetModelOverrideAsync(consumer, ct).ConfigureAwait(false), ReadOnlyDictionary<string, string>.Empty);
}

/// <summary>A consumer's live model overrides, as <see cref="IModelRoutingStore.GetModelOverridesAsync"/>
/// returns them. Read one for a candidate through <see cref="For"/>.</summary>
/// <param name="Any">The consumer-wide override, for whichever provider serves; null when none is set.</param>
/// <param name="ByProvider">Overrides scoped to one provider id each. <see cref="For"/> matches the id
/// case-insensitively whatever comparer this map carries.</param>
public sealed record ModelOverrides(string? Any, IReadOnlyDictionary<string, string> ByProvider)
{
    /// <summary>No override of either kind.</summary>
    public static ModelOverrides None { get; } = new(null, ReadOnlyDictionary<string, string>.Empty);

    /// <summary>The override for a candidate served by <paramref name="providerId"/>: that provider's scoped
    /// entry (matched case-insensitively, as the router matches provider ids), else <see cref="Any"/>, else
    /// null. A blank scoped entry counts as absent.</summary>
    public string? For(string providerId)
    {
        if (ByProvider.TryGetValue(providerId, out var exact) && !string.IsNullOrWhiteSpace(exact)) return exact;
        foreach (var (id, model) in ByProvider)
        {
            if (string.Equals(id, providerId, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(model))
                return model;
        }
        return Any;
    }
}

/// <summary>KV-backed <see cref="IModelRoutingStore"/>, reading the registered <see cref="IKeyValueStore"/> each
/// call: <c>&lt;prefix&gt;&lt;consumer&gt;</c> holds the consumer-wide override, and
/// <c>&lt;prefix&gt;&lt;consumer&gt;@&lt;providerId&gt;</c> one scoped to that provider (e.g.
/// <c>lyntai.model.memory@llama</c>) — so a consumer's own name should not contain <c>@</c>. A blank value is
/// unset. Fail-open: no store yields no override, and a store fault logs a warning and yields what was already
/// read; only the caller's cancellation propagates. Mirrors how <c>PromptRegistry</c> reads a live prompt
/// override.</summary>
public sealed class KeyValueModelRoutingStore(
    IKeyValueStore? kv = null, ILogger<KeyValueModelRoutingStore>? logger = null, string? keyPrefix = null) : IModelRoutingStore
{
    /// <summary>Default KV key prefix for a per-consumer model override (e.g. <c>lyntai.model.scoring</c>).</summary>
    public const string DefaultKeyPrefix = "lyntai.model.";

    private const char ScopeSeparator = '@';

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

    /// <inheritdoc/>
    public async Task<ModelOverrides> GetModelOverridesAsync(string consumer, CancellationToken ct = default)
    {
        if (kv is null) return ModelOverrides.None;
        string? any = null;
        try
        {
            any = Unblank(await kv.GetAsync(KeyPrefix + consumer, ct).ConfigureAwait(false));
            var scope = KeyPrefix + consumer + ScopeSeparator;
            Dictionary<string, string>? byProvider = null;
            foreach (var key in await kv.ListKeysAsync(scope, ct).ConfigureAwait(false))
            {
                // a BYO store may match the prefix loosely; only an exact, non-empty scope names a provider
                if (key.Length <= scope.Length || !key.StartsWith(scope, StringComparison.Ordinal)) continue;
                if (Unblank(await kv.GetAsync(key, ct).ConfigureAwait(false)) is { } model)
                    (byProvider ??= new(StringComparer.OrdinalIgnoreCase)).TryAdd(key[scope.Length..], model);
            }
            return Overrides(any, byProvider);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "live model overrides read failed for consumer {Consumer}; using the consumer-wide override if read, else the configured default", consumer);
            return Overrides(any, byProvider: null); // a partial scoped set is dropped whole
        }
    }

    private static string? Unblank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static ModelOverrides Overrides(string? any, Dictionary<string, string>? byProvider) =>
        any is null && byProvider is null
            ? ModelOverrides.None
            : new(any, byProvider ?? (IReadOnlyDictionary<string, string>)ReadOnlyDictionary<string, string>.Empty);
}
