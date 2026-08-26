using Lyntai.Storage;

namespace Lyntai.Tests.Storage;

/// <summary>Backend-agnostic <see cref="IMemoryStore"/> contract — run by the InMemory, SQLite, and
/// Postgres test classes so remember/recall/dedup/TTL/cap/prune/forget semantics are pinned identically.
/// <para>DELIBERATELY OMITTED (backend-divergent by design — see <see cref="IMemoryStore"/> docs, kept as
/// backend-SPECIFIC tests instead): same-match ORDERING — SQLite ranks by bm25 relevance while
/// Postgres/InMemory rank by matched-term count then recency. <b>MULTI-WORD matching used to be on this
/// list and is not any more</b> (3.0, <c>docs/DECISIONS.md</c> D55): every backend splits a query the same
/// way, so which entries are found is portable and only the ranking among them is not. The cross-backend
/// assertion lives in <c>MemoryStoreTests.Recall_matching_is_consistent_for_single_tokens_and_for_separated_words</c>.
/// This contract asserts backend-agnostic facts: a ≥3-char term substring recalls; scope/task filtering;
/// dedup; TTL expiry; cap COUNT + set membership (NOT the sequence); prune; forget.</para>
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

        Assert.True(await store.PruneAsync() >= 1);       // removed by prune
    }

    /// <summary>T9 (promoted from the SQLite-only lifecycle tests): re-remembering an identical fact
    /// dedups AND resets its TTL clock — the refresh keeps it alive past the original expiry.</summary>
    public static async Task Refreshing_a_fact_extends_its_ttl(IMemoryStore store, string key, Action<TimeSpan> advance)
    {
        await store.RememberAsync(key, "s", "keep me", ttl: TimeSpan.FromMinutes(5));
        advance(TimeSpan.FromMinutes(3));
        await store.RememberAsync(key, "s", "keep me", ttl: TimeSpan.FromMinutes(5)); // dedup refresh at t=3m
        advance(TimeSpan.FromMinutes(4)); // t=7m: past the original 5m expiry, within the refreshed 3m+5m=8m

        Assert.Single(await store.RecallAsync(key)); // still alive — the refresh reset the clock
    }

    /// <summary><b>Re-remembering WITHOUT a ttl replaces an explicitly-set one</b> — the mixed case the
    /// refresh fact above does not reach, since that one passes the same ttl twice.
    ///
    /// <para><b>Asserted because it is the shape <c>docs/DECISIONS.md</c> D91 is about</b>, checked here and
    /// found DEFENSIBLE rather than defective — which is worth pinning precisely because the neighbouring
    /// three were not. In the graph engine a caller's omission was resolved into a value and then written as
    /// though they had chosen it, three times, destroying their data. Here the omission also resolves (to
    /// the store's own default TTL, or to no expiry) and is also written — but a TTL is not the caller's
    /// data, nothing is destroyed, and a deployment's retention default legitimately applies to every write.
    /// The distinction between the two cases is what makes D91 a rule rather than a reflex.</para>
    ///
    /// <para>The consequence is still worth knowing: refreshing a long-lived fact with a bare
    /// <c>RememberAsync</c> does not preserve the longer expiry.</para></summary>
    public static async Task Re_remembering_without_a_ttl_replaces_an_explicit_one(
        IMemoryStore store, string key, Action<TimeSpan> advance)
    {
        await store.RememberAsync(key, "s", "keep me", ttl: TimeSpan.FromMinutes(60));
        await store.RememberAsync(key, "s", "keep me");   // no ttl restated

        advance(TimeSpan.FromMinutes(90));

        // Still present: with no store-level default the bare write cleared the expiry outright. What is
        // asserted is that the explicit 60m did NOT survive as an expiry — the entry outlives it either way.
        Assert.Single(await store.RecallAsync(key));
    }

    /// <summary>T9: a re-remembered (deduped) fact refreshes its recall RECENCY — bare (no-query) recall is
    /// recency-ordered on every backend, so the reinforced fact must come back first.</summary>
    public static async Task Re_remembering_refreshes_recall_recency(IMemoryStore store, string key, Action<TimeSpan> advance)
    {
        await store.RememberAsync(key, "s", "important");
        advance(TimeSpan.FromMinutes(1));
        await store.RememberAsync(key, "s", "trivial");
        advance(TimeSpan.FromMinutes(1));
        await store.RememberAsync(key, "s", "important"); // dedup refresh — must move to the top

        var hits = await store.RecallAsync(key);
        Assert.Equal("important", hits[0].Content); // most recently reinforced ⇒ first in recall
    }

    /// <summary>T9: <c>PruneAsync(olderThan:)</c> removes by AGE regardless of TTL. Task-scoped so it is
    /// safe on the shared Postgres container (an unscoped age prune would remove other tests' rows).</summary>
    public static async Task Prune_older_than_removes_by_age_within_a_task(IMemoryStore store, string key, Action<TimeSpan> advance)
    {
        await store.RememberAsync(key, "s", "old fact"); // no TTL, at t=0
        advance(TimeSpan.FromMinutes(10));
        await store.RememberAsync(key, "s", "new fact"); // at t=10m

        var removed = await store.PruneAsync(taskKey: key, olderThan: TimeSpan.FromMinutes(5));

        Assert.Equal(1, removed); // "old fact" is 10m old (> 5m); "new fact" just written
        var hits = await store.RecallAsync(key);
        Assert.Single(hits);
        Assert.Equal("new fact", hits[0].Content);
    }

    /// <summary>T9: a task-scoped prune removes only that task — the sibling task's expired row survives
    /// (proved by its own scoped prune still finding it, so no cross-backend table peeking).</summary>
    public static async Task Prune_scoped_to_one_task_leaves_the_sibling(IMemoryStore store, string key, Action<TimeSpan> advance)
    {
        await store.RememberAsync(key + "-a", "s", "a", ttl: TimeSpan.FromMinutes(5));
        await store.RememberAsync(key + "-b", "s", "b", ttl: TimeSpan.FromMinutes(5));
        advance(TimeSpan.FromMinutes(6));

        Assert.Equal(1, await store.PruneAsync(taskKey: key + "-a"));
        Assert.Empty(await store.RecallAsync(key + "-a"));
        Assert.Equal(1, await store.PruneAsync(taskKey: key + "-b")); // still there → untouched by the scoped prune
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

    /// <summary>Store built with <c>CountCap(3, Lru)</c> + a controllable clock: a recalled entry survives
    /// eviction that a FIFO cap would have dropped.</summary>
    public static async Task Lru_evicts_least_recently_recalled(IMemoryStore store, string key, Action<TimeSpan> advance)
    {
        await store.RememberAsync(key, "s", "alpha one");   advance(TimeSpan.FromMinutes(1));
        await store.RememberAsync(key, "s", "beta two");    advance(TimeSpan.FromMinutes(1));
        await store.RememberAsync(key, "s", "gamma three"); advance(TimeSpan.FromMinutes(1));

        Assert.Single(await store.RecallAsync(key, query: "alpha")); // refreshes "alpha one" → most-recently-used
        advance(TimeSpan.FromMinutes(1));

        await store.RememberAsync(key, "s", "delta four"); // 4th entry, cap 3 → evict least-recently-USED
        var contents = (await store.RecallAsync(key)).Select(h => h.Content).ToHashSet();
        Assert.Equal(3, contents.Count);
        Assert.Contains("alpha one", contents);      // survived because it was recalled (a FIFO cap would drop it)
        Assert.Contains("gamma three", contents);
        Assert.Contains("delta four", contents);
        Assert.DoesNotContain("beta two", contents); // least-recently-used → evicted
    }

    /// <summary>Store built with <c>CountCap(2, Lru)</c> + a controllable clock: a BARE (no-query) recall
    /// does NOT refresh LRU recency — only a queried recall counts as "use", so a routine list-all can't
    /// churn the working set.</summary>
    public static async Task Lru_bare_recall_does_not_refresh_recency(IMemoryStore store, string key, Action<TimeSpan> advance)
    {
        await store.RememberAsync(key, "s", "alpha fact"); advance(TimeSpan.FromMinutes(1));
        await store.RememberAsync(key, "s", "beta fact");  advance(TimeSpan.FromMinutes(1));

        Assert.Single(await store.RecallAsync(key, query: "alpha")); // queried recall → "alpha fact" is now recently-used
        advance(TimeSpan.FromMinutes(1));

        Assert.Equal(2, (await store.RecallAsync(key)).Count); // BARE list-all — must NOT bump "beta fact"'s recency
        advance(TimeSpan.FromMinutes(1));

        await store.RememberAsync(key, "s", "gamma fact"); // 3rd entry, cap 2 → evict least-recently-USED
        var contents = (await store.RecallAsync(key)).Select(h => h.Content).ToHashSet();
        Assert.Equal(2, contents.Count);
        Assert.Contains("alpha fact", contents);      // survived (queried) — a refreshing bare recall would evict this instead
        Assert.Contains("gamma fact", contents);
        Assert.DoesNotContain("beta fact", contents); // never queried → least-recently-used → evicted
    }

    /// <summary>Store built with <c>TimeToLive(5min)</c> + a controllable clock: the policy default TTL
    /// applies to entries remembered without a per-call ttl; a per-call ttl still wins.</summary>
    public static async Task Default_ttl_expires_entries_without_per_call_ttl(IMemoryStore store, string key, Action<TimeSpan> advance)
    {
        await store.RememberAsync(key, "s", "uses default ttl");                                // → policy DefaultTtl (5min)
        await store.RememberAsync(key, "s", "explicit long", ttl: TimeSpan.FromMinutes(100));   // per-call ttl overrides default
        Assert.Equal(2, (await store.RecallAsync(key)).Count);

        advance(TimeSpan.FromMinutes(6)); // past the 5min default, before the 100min explicit
        var live = await store.RecallAsync(key);
        Assert.Single(live);
        Assert.Equal("explicit long", live[0].Content);
    }

    /// <summary>Store built with <c>SizeBudget(25 chars, Fifo)</c>: entries are trimmed to keep the newest
    /// under the per-scope character budget.</summary>
    public static async Task Size_budget_evicts_to_fit(IMemoryStore store, string key)
    {
        // each content is 10 chars; budget 25 → at most 2 fit (20 ≤ 25; a 3rd would be 30 > 25)
        await store.RememberAsync(key, "s", new string('a', 10));
        await store.RememberAsync(key, "s", new string('b', 10));
        await store.RememberAsync(key, "s", new string('c', 10));

        var contents = (await store.RecallAsync(key)).Select(h => h.Content).ToHashSet();
        Assert.Equal(2, contents.Count);                       // trimmed to fit the char budget
        Assert.Contains(new string('c', 10), contents);        // newest 2 kept (FIFO)
        Assert.Contains(new string('b', 10), contents);
        Assert.DoesNotContain(new string('a', 10), contents);  // oldest evicted to fit
    }

    /// <summary>Store built with <c>SizeBudget(2)</c>: the budget counts Unicode CODE POINTS (== SQL
    /// LENGTH), NOT UTF-16 units, so astral-plane content evicts identically on every backend.</summary>
    public static async Task Size_budget_counts_code_points_not_utf16_units(IMemoryStore store, string key)
    {
        // each entry is ONE emoji = 1 code point but 2 UTF-16 units. Under a 2-code-point budget both fit;
        // a string.Length (UTF-16) measure would keep only one — this locks the cross-backend measure.
        await store.RememberAsync(key, "s", "😀");
        await store.RememberAsync(key, "s", "🎉");
        Assert.Equal(2, (await store.RecallAsync(key)).Count);
    }

    /// <summary>Store built with a policy that sets BOTH a count cap AND a size budget: both bind (the
    /// count cap first, then the char budget). Exercises the size-budget routing that also applies the cap
    /// — the presets never set both.</summary>
    public static async Task Both_count_cap_and_size_budget_apply(IMemoryStore store, string key)
    {
        // cap 3, budget 25 chars, entries of 10 chars each: the cap alone would keep 3 (30 chars), but the
        // 25-char budget trims to the newest 2 (20 ≤ 25; a 3rd = 30 > 25). Both bounds bind.
        await store.RememberAsync(key, "s", new string('a', 10));
        await store.RememberAsync(key, "s", new string('b', 10));
        await store.RememberAsync(key, "s", new string('c', 10));
        await store.RememberAsync(key, "s", new string('d', 10));

        var contents = (await store.RecallAsync(key)).Select(h => h.Content).ToHashSet();
        Assert.Equal(2, contents.Count);
        Assert.Contains(new string('d', 10), contents); // newest 2 survive both bounds
        Assert.Contains(new string('c', 10), contents);
    }

    /// <summary>Store built with <c>CountCap(2, Lru)</c>: entries with a TIED recency (same tick, none
    /// recalled) are broken by <c>id DESC</c> — the atomic SQL count-cap path must match <c>Survivors</c>.</summary>
    public static async Task Lru_recency_tie_broken_by_id(IMemoryStore store, string key)
    {
        // no clock advance + no queried recall → all three share last_accessed_at (= created_at); cap 2 →
        // keep the two highest ids, evict the lowest, by the id-DESC tiebreak (identical on every backend).
        await store.RememberAsync(key, "s", "first");
        await store.RememberAsync(key, "s", "second");
        await store.RememberAsync(key, "s", "third");

        var contents = (await store.RecallAsync(key)).Select(h => h.Content).ToHashSet();
        Assert.Equal(2, contents.Count);
        Assert.Contains("second", contents);
        Assert.Contains("third", contents);
        Assert.DoesNotContain("first", contents); // lowest id evicted on the recency tie
    }

    /// <summary>Store built with <c>Manual</c> (no size bound) + a high recall limit: nothing is auto-evicted.</summary>
    public static async Task Manual_policy_never_evicts(IMemoryStore store, string key)
    {
        for (var i = 1; i <= 12; i++) await store.RememberAsync(key, "s", $"fact {i}");
        Assert.Equal(12, (await store.RecallAsync(key)).Count); // no cap/budget → all kept
    }

    public static async Task Limit_caps_results_and_composes_with_scope(IMemoryStore store, string key)
    {
        await store.RememberAsync(key, "a", "alpha fact one");
        await store.RememberAsync(key, "a", "alpha fact two");
        await store.RememberAsync(key, "a", "alpha fact three");
        await store.RememberAsync(key, "b", "beta fact");

        var hits = await store.RecallAsync(key, scope: "a", limit: 2);
        Assert.Equal(2, hits.Count);                       // limit caps the result count...
        Assert.All(hits, h => Assert.Equal("a", h.Scope)); // ...and composes with the scope filter
    }

    public static async Task Forget_clears_a_task(IMemoryStore store, string key)
    {
        await store.RememberAsync(key, "s", "x");
        await store.ForgetAsync(key);
        Assert.Empty(await store.RecallAsync(key));
    }

    public static async Task Forget_scoped_clears_only_that_scope(IMemoryStore store, string key)
    {
        await store.RememberAsync(key, "a", "fact in a");
        await store.RememberAsync(key, "b", "fact in b");

        await store.ForgetAsync(key, scope: "a");

        Assert.Empty(await store.RecallAsync(key, scope: "a")); // the forgotten scope is empty
        var b = await store.RecallAsync(key, scope: "b");       // the sibling scope is untouched
        Assert.Single(b);
        Assert.Equal("fact in b", b[0].Content);
    }

    public static async Task Recall_is_fail_open_on_empty_query(IMemoryStore store, string key)
    {
        await store.RememberAsync(key, "s", "newest");
        Assert.Single(await store.RecallAsync(key, query: "   ")); // whitespace query → most-recent, no throw
    }
}
