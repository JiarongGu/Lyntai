using Lyntai.Inference;
using Lyntai.Inference.Budgeting;

namespace Lyntai.Tests.Storage;

/// <summary>Backend-agnostic <see cref="IUsageTracker"/> contract — run by the InMemory, SQLite and
/// Postgres suites so accumulate / aggregate / reset semantics are pinned identically.
///
/// <para><b>Why this exists as a CONTRACT rather than three test classes.</b> `storage.md` states that the
/// contract facts are the dedup mechanism holding the relational pair to one behaviour. This domain had
/// none: it was held by two hand-maintained per-backend suites which had already diverged 11 facts to 8,
/// and three SQLite assertions had no Postgres counterpart at all. The domain backs
/// <c>BudgetedTextClient</c>, which REFUSES calls at a cap, so a drift here ends in overspend rather than in
/// a wrong answer.</para>
///
/// <para>Every fact is scoped to a caller-supplied <paramref name="consumer"/> so it is safe on the shared
/// Postgres container. The two that read a TABLE-WIDE total cannot be, and are excluded there by name —
/// see <see cref="PostgresContractCoverageTests"/>, which fails if an exclusion stops matching.</para></summary>
public static class UsageTrackerContract
{
    private static ProviderUsage Call(long input, long output, double cost) =>
        new(input, output, CostUsd: cost);

    public static async Task Records_accumulate_per_consumer(IUsageTracker tracker, string consumer)
    {
        await tracker.RecordAsync(consumer, Call(10, 5, 0.10));
        await tracker.RecordAsync(consumer, Call(20, 5, 0.20));

        var total = await tracker.TotalAsync(consumer);

        Assert.Equal(30, total.InputTokens);
        Assert.Equal(10, total.OutputTokens);
        Assert.Equal(40, total.TotalTokens);   // what the token cap is measured against
        Assert.Equal(0.30, total.CostUsd, 5);
        Assert.Equal(2, total.Calls);
    }

    /// <summary>A consumer nothing was ever recorded under reads Empty rather than throwing or null-ing —
    /// the pre-call read happens on EVERY budgeted request, including the first one a consumer ever makes.</summary>
    public static async Task An_unrecorded_consumer_is_Empty(IUsageTracker tracker, string consumer)
    {
        Assert.Equal(UsageTotals.Empty, await tracker.TotalAsync(consumer + "-never-recorded"));
    }

    /// <summary>Consumer identity is case-INSENSITIVE, so a cap cannot be overspent 2x by tagging "App" in
    /// one code path and "app" in another — the `PerConsumer` options map is OrdinalIgnoreCase, like every
    /// options map here, and the totals it is checked against must agree.</summary>
    public static async Task Consumer_identity_aggregates_across_casings(IUsageTracker tracker, string consumer)
    {
        await tracker.RecordAsync(consumer.ToLowerInvariant(), Call(10, 0, 0.10));
        await tracker.RecordAsync(consumer.ToUpperInvariant(), Call(20, 0, 0.20));

        Assert.Equal(2, (await tracker.TotalAsync(consumer.ToLowerInvariant())).Calls);
        Assert.Equal(2, (await tracker.TotalAsync(consumer.ToUpperInvariant())).Calls);
        Assert.Equal(30, (await tracker.TotalAsync(consumer)).InputTokens);
        Assert.Equal(0.30, (await tracker.TotalAsync(consumer)).CostUsd, 5);
    }

    public static async Task Resetting_a_consumer_clears_it(IUsageTracker tracker, string consumer)
    {
        await tracker.RecordAsync(consumer, Call(10, 0, 0.10));

        await tracker.ResetAsync(consumer);

        Assert.Equal(UsageTotals.Empty, await tracker.TotalAsync(consumer));
    }

    /// <summary><b>The fact this contract was written for.</b> A scoped reset must not clear the ledger of
    /// every OTHER consumer — and on Postgres nothing asserted it, so a <c>ResetAsync(consumer)</c> that
    /// dropped the whole table would have passed. That failure is silent and expensive in one direction: a
    /// cleared ledger stops the cap binding, and `BudgetedTextClient` stops refusing.</summary>
    public static async Task Resetting_ONE_consumer_leaves_the_others_intact(IUsageTracker tracker, string consumer)
    {
        var other = consumer + "-kept";
        await tracker.RecordAsync(consumer, Call(10, 0, 0.10));
        await tracker.RecordAsync(other, Call(20, 0, 0.20));

        await tracker.ResetAsync(consumer);

        Assert.Equal(UsageTotals.Empty, await tracker.TotalAsync(consumer));
        var survivor = await tracker.TotalAsync(other);
        Assert.Equal(20, survivor.InputTokens);
        Assert.Equal(0.20, survivor.CostUsd, 5);
        Assert.Equal(1, survivor.Calls);
    }

    /// <summary>Resetting one CASING clears the consumer, because they are one identity. The mirror of the
    /// aggregation fact: if reset were case-sensitive, a cap could be cleared under one spelling while the
    /// totals it reads kept accruing under the other.</summary>
    public static async Task Resetting_is_case_insensitive_like_the_totals(IUsageTracker tracker, string consumer)
    {
        await tracker.RecordAsync(consumer.ToLowerInvariant(), Call(10, 0, 0.10));

        await tracker.ResetAsync(consumer.ToUpperInvariant());

        Assert.Equal(UsageTotals.Empty, await tracker.TotalAsync(consumer.ToLowerInvariant()));
    }

    // ---- TABLE-WIDE: excluded on the shared Postgres container, by name, with the reason recorded -------

    /// <summary>A null consumer sums ACROSS consumers — the global spend a global cap is checked against.</summary>
    public static async Task The_global_total_sums_across_consumers(IUsageTracker tracker, string consumer)
    {
        await tracker.RecordAsync(consumer + "-a", Call(10, 0, 0.10));
        await tracker.RecordAsync(consumer + "-b", Call(1, 0, 0.01));

        Assert.Equal(0.11, (await tracker.TotalAsync()).CostUsd, 5);
        Assert.Equal(11, (await tracker.TotalAsync()).InputTokens);
    }

    /// <summary>A null consumer resets EVERYTHING — the billing-window boundary.</summary>
    public static async Task Resetting_everything_clears_every_consumer(IUsageTracker tracker, string consumer)
    {
        await tracker.RecordAsync(consumer + "-a", Call(10, 0, 0.10));
        await tracker.RecordAsync(consumer + "-b", Call(20, 0, 0.20));

        await tracker.ResetAsync();

        Assert.Equal(UsageTotals.Empty, await tracker.TotalAsync());
        Assert.Equal(UsageTotals.Empty, await tracker.TotalAsync(consumer + "-a"));
    }
}
