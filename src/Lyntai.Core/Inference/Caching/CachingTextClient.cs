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

        // key on the EFFECTIVE model and the LIVE route, as the router resolves them, so two consumers with
        // Model=null + identical messages don't collide, and a reply is never served across a live rebind
        IReadOnlyList<ProviderCandidate> route = [];
        if (modelRouting is not null)
        {
            try
            {
                route = await modelRouting.GetRouteAsync(req.Consumer, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // an unreadable route leaves no key to trust: read and write nothing, and let the call through
                _logger.LogWarning(ex, "response-cache: the live route read failed for consumer {Consumer}; not caching this call", req.Consumer);
                return await Inner.CompleteAsync(req, ct).ConfigureAwait(false);
            }
        }
        var key = ResponseCacheKey.For(req, EffectiveModel(req, route));
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
    /// candidate will serve, so a live route joins it whole, in fallback order, as <c>|route=</c> plus its
    /// entries (the provider id lower-cased, as the router matches it) — appended ONLY when there is one, so a
    /// consumer with no live route keeps its key.</summary>
    private string? EffectiveModel(TextRequest req, IReadOnlyList<ProviderCandidate> route)
    {
        var model = options.ResolveModel(req.Consumer, req.Model);
        if (route is not { Count: > 0 }) return model;
        return model + "|route=" + string.Join(",", route.Select(c =>
            ProviderCandidateSpec.Format(c with { ProviderId = c.ProviderId.ToLowerInvariant() })));
    }

    // StreamAsync/GetCapabilitiesAsync: base pass-through (streaming is delivered live; not a cache unit).

    // Native tool requests bypass the cache (the loop is stateful); everything else is cacheable.
    private static bool IsCacheable(TextRequest req) => req.Tools is null or { Count: 0 };
}
