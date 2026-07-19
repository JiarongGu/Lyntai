using Lyntai.Storage;

namespace Lyntai.Tests.Storage;

/// <summary>Backend-agnostic <see cref="IMemoryStore"/> contract — run by the InMemory, SQLite, and
/// Postgres test classes so remember/recall/dedup/TTL/cap/prune/forget semantics are pinned identically.
/// <para>DELIBERATELY OMITTED (backend-divergent by design — see <see cref="IMemoryStore"/> docs, kept as
/// backend-SPECIFIC tests instead): (1) same-match ORDERING — SQLite ranks by bm25 relevance while
/// Postgres/InMemory rank by recency; (2) MULTI-WORD/any-token matching — SQLite's FTS matches any token,
/// Postgres/InMemory match a contiguous substring. This contract asserts only backend-agnostic facts: a
/// single ≥3-char token substring recalls; scope/task filtering; dedup; TTL expiry; cap COUNT + set
/// membership (NOT the sequence); prune; forget.</para>
/// Every method is namespaced by a caller-supplied <paramref name="key"/> (the task key) so it is safe on
/// the shared Postgres container.</summary>
public static class MemoryStoreContract
{
    public static async Task Remember_then_recall_by_single_token_substring(IMemoryStore store, string key)
    {
        await store.RememberAsync(key, "prod", "the deploy pipeline requires manual approval");
        await store.RememberAsync(key, "prod", "rollbacks must page the on-call");

        // AGNOSTIC guarantee: a single ≥3-char token substring recalls on every backend.
        var hits = await store.RecallAsync(key, query: "pipeline");
        Assert.Single(hits);
        Assert.Contains("manual approval", hits[0].Content);
    }

    public static async Task Cjk_substring_recall(IMemoryStore store, string key)
    {
        await store.RememberAsync(key, "notes", "灵台平台负责智能代理的记忆存储");
        await store.RememberAsync(key, "notes", "另一条无关的记录");

        // a mid-phrase CJK substring recalls on every backend (SQLite FTS5-trigram / Postgres pg_trgm /
        // InMemory substring) — unicode61 would index the whole phrase as one token and never match.
        var hits = await store.RecallAsync(key, query: "智能代理");
        Assert.Single(hits);
        Assert.Contains("智能代理", hits[0].Content);
    }

    public static async Task Scope_filter_applies(IMemoryStore store, string key)
    {
        await store.RememberAsync(key, "alpha", "fact in alpha scope");
        await store.RememberAsync(key, "beta", "fact in beta scope");

        var alpha = await store.RecallAsync(key, scope: "alpha");
        Assert.Single(alpha);
        Assert.Equal("alpha", alpha[0].Scope);

        Assert.Equal(2, (await store.RecallAsync(key)).Count);
    }

    public static async Task Task_isolation_applies(IMemoryStore store, string key)
    {
        await store.RememberAsync(key + "-a", "s", "belongs to a");
        await store.RememberAsync(key + "-b", "s", "belongs to b");

        var hits = await store.RecallAsync(key + "-a");
        Assert.Single(hits);
        Assert.Equal(key + "-a", hits[0].TaskKey);
    }

    public static async Task Remembering_an_identical_fact_dedups(IMemoryStore store, string key)
    {
        await store.RememberAsync(key, "s", "the same fact");
        await store.RememberAsync(key, "s", "the same fact");
        await store.RememberAsync(key, "s", "the same fact");

        var hits = await store.RecallAsync(key);
        Assert.Single(hits); // one entry, not three
        Assert.Equal("the same fact", hits[0].Content);
    }

    public static async Task Different_scopes_are_not_deduped_together(IMemoryStore store, string key)
    {
        await store.RememberAsync(key, "a", "shared text");
        await store.RememberAsync(key, "b", "shared text");

        Assert.Equal(2, (await store.RecallAsync(key)).Count);
    }

    /// <summary>Requires the store to be built over a controllable clock (see <paramref name="advance"/>)
    /// so TTL expiry is deterministic on every backend.</summary>
    public static async Task Ttl_entries_expire_from_recall_and_are_pruned(IMemoryStore store, string key, Action<TimeSpan> advance)
    {
        await store.RememberAsync(key, "s", "durable");
        await store.RememberAsync(key, "s", "ephemeral", ttl: TimeSpan.FromMinutes(5));
        Assert.Equal(2, (await store.RecallAsync(key)).Count); // both live now

        advance(TimeSpan.FromMinutes(6)); // past the ephemeral entry's TTL
        var live = await store.RecallAsync(key);
        Assert.Single(live);
        Assert.Equal("durable", live[0].Content);       // expired dropped from recall
        Assert.DoesNotContain(live, m => m.Content == "ephemeral");

        Assert.True(await store.PruneAsync() >= 1);       // reaped by prune
    }

    public static async Task Cap_trims_to_the_newest_entries(IMemoryStore store, string key)
    {
        // The store must be built with MemoryCapPerScope = 3. Assert the COUNT and SET membership only —
        // NOT the sequence (no-query recall recency-orders, but we don't pin the exact order here to stay
        // strictly backend-agnostic).
        for (var i = 1; i <= 5; i++) await store.RememberAsync(key, "s", $"entry {i}");

        var hits = await store.RecallAsync(key);
        var contents = hits.Select(h => h.Content).ToHashSet();
        Assert.Equal(3, hits.Count);                                 // capped to 3
        Assert.Equal(["entry 3", "entry 4", "entry 5"], contents.OrderBy(c => c)); // newest 3 kept
    }

    public static async Task Forget_clears_a_task(IMemoryStore store, string key)
    {
        await store.RememberAsync(key, "s", "x");
        await store.ForgetAsync(key);
        Assert.Empty(await store.RecallAsync(key));
    }

    public static async Task Recall_is_fail_open_on_empty_query(IMemoryStore store, string key)
    {
        await store.RememberAsync(key, "s", "newest");
        Assert.Single(await store.RecallAsync(key, query: "   ")); // whitespace query → most-recent, no throw
    }
}
