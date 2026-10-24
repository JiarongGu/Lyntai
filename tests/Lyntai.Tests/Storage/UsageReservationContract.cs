using Lyntai.Inference;
using Lyntai.Inference.Budgeting;

namespace Lyntai.Tests.Storage;

/// <summary>Backend-agnostic <see cref="IReservingUsageTracker"/> contract — run by the InMemory, SQLite and Postgres
/// suites. The capability exists to make a cap HARD, so the fact that matters most is the parallel one: N racing
/// reservations against one cap admit exactly the ones that fit, on every backend.
///
/// <para>Every fact caps the caller-supplied <paramref name="consumer"/> rather than the global ledger, so it is safe
/// on the shared Postgres container. The one that needs a global cap is <see cref="UnscopedAttribute"/>, so a
/// shared store is cleared before it.</para></summary>
public static class UsageReservationContract
{
    private static DateTimeOffset Later => DateTimeOffset.UtcNow.AddMinutes(10);

    private static UsageReservationRequest Hold(string consumer, double cost, double cap, DateTimeOffset? expiresAt = null) =>
        new(consumer, new ProviderUsage(CostUsd: cost), new UsageCaps(ConsumerMaxCostUsd: cap), expiresAt ?? Later);

    public static async Task A_hold_that_fits_is_granted_and_the_next_that_does_not_is_refused(
        IReservingUsageTracker tracker, string consumer)
    {
        var first = await tracker.ReserveAsync(Hold(consumer, 0.6, cap: 1.0));
        var second = await tracker.ReserveAsync(Hold(consumer, 0.6, cap: 1.0));

        Assert.True(first.Granted);
        Assert.Equal(consumer, first.Reservation!.Consumer);
        Assert.False(second.Granted);
        Assert.Equal(UsageCap.ConsumerCost, second.ExceededCap);
        Assert.Equal(UsageTotals.Empty, await tracker.TotalAsync(consumer)); // a hold is not spend
    }

    public static async Task Reconciling_records_the_actual_and_releases_the_hold(IReservingUsageTracker tracker, string consumer)
    {
        var held = await tracker.ReserveAsync(Hold(consumer, 0.6, cap: 1.0));

        await tracker.ReconcileAsync(held.Reservation!, new ProviderUsage(10, 5, CostUsd: 0.2));

        var total = await tracker.TotalAsync(consumer);
        Assert.Equal(0.2, total.CostUsd, 5);
        Assert.Equal(15, total.TotalTokens);
        Assert.Equal(1, total.Calls);
        Assert.True((await tracker.ReserveAsync(Hold(consumer, 0.8, cap: 1.0))).Granted); // 0.2 + 0.8 fits
    }

    /// <summary>A failed or cancelled call reports nothing: the hold goes, and nothing is billed for it.</summary>
    public static async Task Reconciling_without_usage_releases_and_records_nothing(IReservingUsageTracker tracker, string consumer)
    {
        var held = await tracker.ReserveAsync(Hold(consumer, 0.9, cap: 1.0));

        await tracker.ReconcileAsync(held.Reservation!, actual: null);

        Assert.Equal(UsageTotals.Empty, await tracker.TotalAsync(consumer));
        Assert.True((await tracker.ReserveAsync(Hold(consumer, 0.9, cap: 1.0))).Granted);
    }

    public static async Task Committed_spend_counts_against_the_cap(IReservingUsageTracker tracker, string consumer)
    {
        await tracker.RecordAsync(consumer, new ProviderUsage(CostUsd: 0.5));

        Assert.False((await tracker.ReserveAsync(Hold(consumer, 0.6, cap: 1.0))).Granted);
        Assert.True((await tracker.ReserveAsync(Hold(consumer, 0.5, cap: 1.0))).Granted);  // exactly at the cap fits
        Assert.False((await tracker.ReserveAsync(Hold(consumer, 0.0, cap: 1.0))).Granted); // a reached cap refuses even a free call
    }

    public static async Task A_token_cap_binds_tokens(IReservingUsageTracker tracker, string consumer)
    {
        var caps = new UsageCaps(ConsumerMaxTokens: 100);

        var first = await tracker.ReserveAsync(new(consumer, new ProviderUsage(0, 60), caps, Later));
        var second = await tracker.ReserveAsync(new(consumer, new ProviderUsage(50, 0), caps, Later));

        Assert.True(first.Granted);
        Assert.Equal(UsageCap.ConsumerTokens, second.ExceededCap);
    }

    /// <summary>The point of the capability. Twelve racing holds of 0.25 against a cap of 1.0 admit four, never
    /// five — a check-then-write that is not atomic admits more under exactly this load.</summary>
    public static async Task Racing_reservations_admit_exactly_what_fits(IReservingUsageTracker tracker, string consumer)
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => Task.Run(async () => await tracker.ReserveAsync(Hold(consumer, 0.25, cap: 1.0)))));

        Assert.Equal(4, results.Count(r => r.Granted));
        Assert.All(results.Where(r => !r.Granted), r => Assert.Equal(UsageCap.ConsumerCost, r.ExceededCap));
    }

    /// <summary>A caller that died never reconciles; its hold stops counting once it expires.</summary>
    public static async Task An_expired_hold_no_longer_counts(IReservingUsageTracker tracker, string consumer)
    {
        Assert.True((await tracker.ReserveAsync(Hold(consumer, 0.9, cap: 1.0, DateTimeOffset.UtcNow.AddMinutes(-1)))).Granted);

        Assert.True((await tracker.ReserveAsync(Hold(consumer, 0.9, cap: 1.0))).Granted);
    }

    public static async Task Holds_fold_consumer_casing_like_the_totals(IReservingUsageTracker tracker, string consumer)
    {
        Assert.True((await tracker.ReserveAsync(Hold(consumer.ToLowerInvariant(), 0.6, cap: 1.0))).Granted);

        Assert.False((await tracker.ReserveAsync(Hold(consumer.ToUpperInvariant(), 0.6, cap: 1.0))).Granted);
    }

    public static async Task A_consumer_cap_does_not_count_another_consumers_holds(IReservingUsageTracker tracker, string consumer)
    {
        Assert.True((await tracker.ReserveAsync(Hold(consumer + "-a", 0.9, cap: 1.0))).Granted);

        Assert.True((await tracker.ReserveAsync(Hold(consumer + "-b", 0.9, cap: 1.0))).Granted);
    }

    /// <summary>Resetting clears SPEND; a call still in flight keeps its hold.</summary>
    public static async Task Resetting_spend_leaves_holds_in_place(IReservingUsageTracker tracker, string consumer)
    {
        await tracker.RecordAsync(consumer, new ProviderUsage(CostUsd: 0.3));
        Assert.True((await tracker.ReserveAsync(Hold(consumer, 0.6, cap: 1.0))).Granted);

        await tracker.ResetAsync(consumer);

        Assert.Equal(UsageTotals.Empty, await tracker.TotalAsync(consumer));
        Assert.False((await tracker.ReserveAsync(Hold(consumer, 0.6, cap: 1.0))).Granted);
    }

    // ---- TABLE-WIDE: excluded on the shared Postgres container, by name, with the reason recorded -------

    /// <summary>A global cap counts every consumer's holds.</summary>
    [Unscoped("a global cap reads the whole ledger and every live hold")]
    public static async Task A_global_cap_counts_every_consumers_holds(IReservingUsageTracker tracker, string consumer)
    {
        var caps = new UsageCaps(MaxCostUsd: 1.0);
        Assert.True((await tracker.ReserveAsync(new(consumer + "-a", new ProviderUsage(CostUsd: 0.6), caps, Later))).Granted);

        var refused = await tracker.ReserveAsync(new(consumer + "-b", new ProviderUsage(CostUsd: 0.6), caps, Later));

        Assert.Equal(UsageCap.GlobalCost, refused.ExceededCap);
    }
}
