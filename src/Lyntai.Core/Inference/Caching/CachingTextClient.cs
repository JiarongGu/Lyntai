using Lyntai.Inference;
using Lyntai.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Inference.Caching;

/// <summary>
/// Decorates the front door (<see cref="ITextClient"/>) with a read-through response cache: on a cacheable
/// request it returns a stored Ok reply when one is fresh, otherwise calls the inner client and stores an
/// Ok result. Wired by <c>AddResponseCache()</c>, so the whole library (tool loop, orchestrator, scorers,
/// pairwise judge) reads through it once enabled.
/// <para>NOT cached: <b>streaming</b> (delivered live, not a single unit); requests carrying <b>native
/// tools</b> (the tool loop is stateful and its tools can side-effect); and <b>non-Ok</b> replies (a
/// transient failure must never stick). Caching assumes the consumer accepts that identical inputs return
/// an identical stored answer — that determinism is the point (cost + latency), so a request whose output
/// must vary per call should skip the cache or use a short TTL.</para>
/// </summary>
public sealed class CachingTextClient(
    ITextClient inner, IResponseCache cache, LyntaiOptions options,
    ILogger<CachingTextClient>? logger = null, IModelRoutingStore? modelRouting = null) : DelegatingTextClient(inner)
{
    private readonly ILogger _logger = logger ?? NullLogger<CachingTextClient>.Instance;

    public override async Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default)
    {
        if (!IsCacheable(req)) return await Inner.CompleteAsync(req, ct).ConfigureAwait(false);

        // key on the EFFECTIVE model — the router resolves per-consumer defaults + LIVE overrides, so two
        // consumers (or a pre/post admin retune) with Model=null + identical messages don't collide, and a
        // stale-model reply is never served after a live retune
        var live = modelRouting is null
            ? ModelOverrides.None
            : await modelRouting.GetModelOverridesAsync(req.Consumer, ct).ConfigureAwait(false);
        var key = ResponseCacheKey.For(req, EffectiveModel(req, live));
        var cached = await cache.GetAsync(key, ct).ConfigureAwait(false);
        if (cached is not null)
        {
            _logger.LogDebug("response-cache hit (consumer {Consumer})", req.Consumer);
            LyntaiDiagnostics.RecordCacheAccess(hit: true);
            return cached;
        }

        LyntaiDiagnostics.RecordCacheAccess(hit: false);
        var reply = await Inner.CompleteAsync(req, ct).ConfigureAwait(false);
        // cache only clean successes — never an error (transient) or a tool-call reply (stateful/deferred)
        if (reply.Verdict == ProviderVerdict.Ok && reply.ToolCalls is null or { Count: 0 })
            await cache.SetAsync(key, reply, options.Cache.Ttl, ct).ConfigureAwait(false);
        return reply;
    }

    /// <summary>The key's model component. The cache sits in front of the router and cannot know which
    /// provider will serve, so provider-scoped overrides join it as <c>|provider=model</c> pairs in a canonical
    /// order — appended ONLY when there are any, so every key without one is unchanged. A request naming its
    /// own model outranks every live entry and keys on that model alone.</summary>
    private string? EffectiveModel(TextRequest req, ModelOverrides live)
    {
        var model = options.ResolveModel(req.Consumer, req.Model, live.Any);
        if (!string.IsNullOrEmpty(req.Model) || live.ByProvider.Count == 0) return model;

        var scoped = string.Concat(live.ByProvider
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => (Provider: p.Key.ToLowerInvariant(), Model: p.Value))
            .OrderBy(p => p.Provider, StringComparer.Ordinal)
            .ThenBy(p => p.Model, StringComparer.Ordinal)
            .Select(p => $"|{p.Provider}={p.Model}"));
        return scoped.Length == 0 ? model : model + scoped;
    }

    // StreamAsync/SupportsToolCalls: base pass-through (streaming is delivered live; not a cache unit).

    // Native tool requests bypass the cache (the loop is stateful); everything else is cacheable.
    private static bool IsCacheable(TextRequest req) => req.Tools is null or { Count: 0 };
}
