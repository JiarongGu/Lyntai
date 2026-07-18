using Lyntai.Diagnostics;
using Lyntai.Llm.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Llm.Caching;

/// <summary>
/// Decorates the front door (<see cref="ILlmClient"/>) with a read-through response cache: on a cacheable
/// request it returns a stored Ok reply when one is fresh, otherwise calls the inner client and stores an
/// Ok result. Wired by <c>AddResponseCache()</c>, so the whole library (tool loop, orchestrator, scorers,
/// pairwise judge) reads through it once enabled.
/// <para>NOT cached: <b>streaming</b> (delivered live, not a single unit); requests carrying <b>native
/// tools</b> (the tool loop is stateful and its tools can side-effect); and <b>non-Ok</b> replies (a
/// transient failure must never stick). Caching assumes the consumer accepts that identical inputs return
/// an identical stored answer — that determinism is the point (cost + latency), so a request whose output
/// must vary per call should skip the cache or use a short TTL.</para>
/// </summary>
public sealed class CachingLlmClient(
    ILlmClient inner, IResponseCache cache, LyntaiOptions options,
    ILogger<CachingLlmClient>? logger = null, IModelRoutingStore? modelRouting = null) : ILlmClient
{
    private readonly ILogger _logger = logger ?? NullLogger<CachingLlmClient>.Instance;

    public async Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default)
    {
        if (!IsCacheable(req)) return await inner.CompleteAsync(req, ct).ConfigureAwait(false);

        // key on the EFFECTIVE model — the router resolves per-consumer defaults + a LIVE override, so two
        // consumers (or a pre/post admin retune) with Model=null + identical messages don't collide, and a
        // stale-model reply is never served after a live retune
        var live = modelRouting is null ? null : await modelRouting.GetModelOverrideAsync(req.Consumer, ct).ConfigureAwait(false);
        var key = ResponseCacheKey.For(req, options.ResolveModel(req.Consumer, req.Model, live));
        var cached = await cache.TryGetAsync(key, ct).ConfigureAwait(false);
        if (cached is not null)
        {
            _logger.LogDebug("response-cache hit (consumer {Consumer})", req.Consumer);
            LyntaiDiagnostics.RecordCacheAccess(hit: true);
            return cached;
        }

        LyntaiDiagnostics.RecordCacheAccess(hit: false);
        var reply = await inner.CompleteAsync(req, ct).ConfigureAwait(false);
        // cache only clean successes — never an error (transient) or a tool-call reply (stateful/deferred)
        if (reply.Verdict == LlmVerdict.Ok && reply.ToolCalls is null or { Count: 0 })
            await cache.SetAsync(key, reply, options.Cache.Ttl, ct).ConfigureAwait(false);
        return reply;
    }

    public IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, CancellationToken ct = default) =>
        inner.StreamAsync(req, ct); // streaming is delivered live; not a cache unit

    public bool SupportsToolCalls(LlmRequest req) => inner.SupportsToolCalls(req);

    // Native tool requests bypass the cache (the loop is stateful); everything else is cacheable.
    private static bool IsCacheable(LlmRequest req) => req.Tools is null or { Count: 0 };
}
