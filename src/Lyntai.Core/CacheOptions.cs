namespace Lyntai;

/// <summary>Tuning for the opt-in response cache (<c>AddResponseCache</c>). Read at runtime by the built-in
/// <see cref="Lyntai.Llm.Caching.InMemoryResponseCache"/>, so <c>LYNTAI_CACHE_*</c> env overrides applied
/// after configuration still take effect.</summary>
public sealed class CacheOptions
{
    /// <summary>How long a cached completion stays fresh. A non-positive value disables storing (every
    /// request misses). Default 1 hour.</summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Soft cap on entries kept by the built-in in-memory cache — expired entries are evicted
    /// first, then the oldest-inserted, once the count exceeds this. Default 1000.</summary>
    public int MaxEntries { get; set; } = 1000;
}
