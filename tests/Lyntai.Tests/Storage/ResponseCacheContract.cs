using Lyntai.Inference;
using Lyntai.Llm;
using Lyntai.Llm.Caching;

namespace Lyntai.Tests.Storage;

/// <summary>Backend-agnostic <see cref="IResponseCache"/> contract — run by the InMemory, SQLite and
/// Postgres suites so hit/miss, TTL and eviction semantics are pinned identically.
///
/// <para><b>Unlike <see cref="UsageTrackerContract"/>, this found no asymmetry to fix.</b> All three
/// backends already covered the same four behaviours; what was missing was only the MECHANISM that keeps
/// them covering the same ones, which is the argument `storage.md` makes for the contract facts existing at
/// all. Recorded so nobody reads this file as evidence the cache had drifted.</para>
///
/// <para><b>The size-cap trim is deliberately NOT here</b>, and the reason is a boundary worth keeping: it
/// is set at CONSTRUCTION (<c>CacheOptions.MaxEntries</c>) rather than exercised on a built cache, and on
/// the shared Postgres container it needs a far-future clock so this test's rows outrank every other
/// test's. A portable fact cannot express either. It stays per-backend, where each suite can say what its
/// own container requires.</para></summary>
public static class ResponseCacheContract
{
    private static TextResponse Reply(string text) =>
        new(text, ProviderVerdict.Ok, new TextUsage(3, 4, CostUsd: 0.05));

    /// <summary>A hit returns what was stored, USAGE included — the field a persistent backend is most
    /// likely to lose, since cost is a floating column and SQLite stores `1.0` as an INTEGER in the same
    /// column it stores `0.05` as a REAL (`storage.md`'s affinity trap).</summary>
    public static async Task A_stored_reply_round_trips_with_its_usage(
        IResponseCache cache, string key, Action<TimeSpan> advance)
    {
        _ = advance;
        await cache.SetAsync(key, Reply("cached"), TimeSpan.FromMinutes(5));

        var hit = await cache.GetAsync(key);

        Assert.NotNull(hit);
        Assert.Equal("cached", hit!.Text);
        Assert.Equal(ProviderVerdict.Ok, hit.Verdict);
        Assert.Equal(3, hit.Usage!.InputTokens);
        Assert.Equal(0.05, hit.Usage.CostUsd!.Value, 5);
    }

    public static async Task A_key_that_was_never_set_is_a_MISS(
        IResponseCache cache, string key, Action<TimeSpan> advance)
    {
        _ = advance;
        Assert.Null(await cache.GetAsync(key + "-never-set"));
    }

    /// <summary>Past its TTL an entry is a miss, not a stale hit. The whole point of the freshness window:
    /// serving past it would hand a caller an answer the deployment has decided is too old.</summary>
    public static async Task An_entry_past_its_TTL_is_a_MISS(
        IResponseCache cache, string key, Action<TimeSpan> advance)
    {
        await cache.SetAsync(key, Reply("fresh"), TimeSpan.FromMinutes(5));
        Assert.NotNull(await cache.GetAsync(key));

        advance(TimeSpan.FromMinutes(6));

        Assert.Null(await cache.GetAsync(key));
    }

    /// <summary>The escape hatch for a poisoned reply evicts exactly one entry. A remove that took its
    /// neighbours with it would silently empty the cache on every correction.</summary>
    public static async Task Remove_evicts_ONE_entry_and_leaves_the_others(
        IResponseCache cache, string key, Action<TimeSpan> advance)
    {
        _ = advance;
        var keep = key + "-keep";
        await cache.SetAsync(key, Reply("poisoned"));
        await cache.SetAsync(keep, Reply("keep"));

        await cache.RemoveAsync(key);

        Assert.Null(await cache.GetAsync(key));
        Assert.NotNull(await cache.GetAsync(keep));
    }

    public static async Task Removing_a_key_that_is_not_there_is_a_NO_OP(
        IResponseCache cache, string key, Action<TimeSpan> advance)
    {
        _ = advance;
        await cache.RemoveAsync(key + "-absent");   // must not throw
        await cache.SetAsync(key, Reply("still works"));
        Assert.NotNull(await cache.GetAsync(key));
    }
}
