namespace Lyntai;

/// <summary>Tuning for the opt-in response cache (<c>AddResponseCache</c>). Read at runtime by the built-in
/// <see cref="Lyntai.Inference.Caching.InMemoryResponseCache"/>, so <c>LYNTAI_CACHE_*</c> env overrides applied
/// after configuration still take effect.</summary>
public sealed class CacheOptions
{
    /// <summary>How long a cached completion stays fresh. A non-positive value disables storing (every
    /// request misses). Default 1 hour.</summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Soft cap on entries kept by the built-in in-memory cache — expired entries are evicted
    /// first, then the oldest-inserted, once the count exceeds this. Default 1000.</summary>
    public int MaxEntries { get; set; } = 1000;

    /// <summary>Isolate entries per <see cref="Lyntai.Inference.TextRequest.Consumer"/>: when true, the front door
    /// stores and reads under <see cref="Lyntai.Inference.Caching.ResponseCacheKey.ForConsumer"/>, so two consumers
    /// never share a hit. Consumers match case-insensitively, as the budget and rate limit match them. Set it when
    /// consumers are separate trust domains (tenants); leave it off for one domain, where sharing is the saving.
    /// Turning it on starts every consumer with a cold cache. Default false.</summary>
    public bool PartitionByConsumer { get; set; }
}
