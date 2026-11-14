using Lyntai.Inference;
using Lyntai.Inference.Budgeting;

namespace Lyntai.Tests.Storage;

/// <summary>Backend-agnostic <see cref="IIdempotentUsageTracker"/> contract — run by the InMemory, SQLite and Postgres
/// suites. The capability exists so a spend that is FETCHED twice (a model calling <c>generate_fetch</c> again, a job
/// reclaimed after it billed) is billed once, so the facts that matter are that a key bills once whoever asks, and that
/// racing callers with one key record one entry.
///
/// <para>Every key is built from the caller-supplied <paramref name="consumer"/>, so each case's keys are its own on
/// the shared Postgres container. Pruning reaches every key older than an instant, the whole table, so that fact is
/// <see cref="UnscopedAttribute"/>.</para></summary>
public static class IdempotentUsageContract
{
    private static ProviderUsage Cost(double usd) => new(CostUsd: usd);

    public static async Task RecordOnce_records_a_key_once(IIdempotentUsageTracker tracker, string consumer)
    {
        var key = $"generation:video:{consumer}";

        Assert.True(await tracker.RecordOnceAsync(consumer, Cost(0.5), key));
        Assert.False(await tracker.RecordOnceAsync(consumer, Cost(0.5), key));

        var total = await tracker.TotalAsync(consumer);
        Assert.Equal(0.5, total.CostUsd, 6);
        Assert.Equal(1, total.Calls);
    }

    /// <summary>A key names one spend in the LEDGER: the same operation fetched under a second consumer was still
    /// paid for once.</summary>
    public static async Task A_key_is_spent_once_whatever_consumer_names_it(IIdempotentUsageTracker tracker, string consumer)
    {
        var key = $"generation:video:{consumer}";
        Assert.True(await tracker.RecordOnceAsync(consumer + "-a", Cost(0.5), key));

        Assert.False(await tracker.RecordOnceAsync(consumer + "-b", Cost(0.5), key));
        Assert.Equal(UsageTotals.Empty, await tracker.TotalAsync(consumer + "-b"));
    }

    /// <summary>Keys compare exactly: an operation id is the backend's, and two that differ in case are two
    /// operations.</summary>
    public static async Task Keys_that_differ_only_in_case_are_two_keys(IIdempotentUsageTracker tracker, string consumer)
    {
        Assert.True(await tracker.RecordOnceAsync(consumer, Cost(0.1), $"generation:video:{consumer}-abc"));

        Assert.True(await tracker.RecordOnceAsync(consumer, Cost(0.1), $"generation:video:{consumer}-ABC"));
        Assert.Equal(2, (await tracker.TotalAsync(consumer)).Calls);
    }

    /// <summary>A reset clears SPEND at a billing-window boundary; the keys stay, so an operation billed in the last
    /// window and fetched again in this one is not billed a second time.</summary>
    public static async Task Resetting_spend_keeps_the_keys(IIdempotentUsageTracker tracker, string consumer)
    {
        var key = $"generation:video:{consumer}";
        await tracker.RecordOnceAsync(consumer, Cost(0.5), key);

        await tracker.ResetAsync(consumer);

        Assert.False(await tracker.RecordOnceAsync(consumer, Cost(0.5), key));
        Assert.Equal(UsageTotals.Empty, await tracker.TotalAsync(consumer));
    }

    /// <summary>The point of the capability under load: racing callers with one key record exactly one entry.</summary>
    public static async Task Racing_callers_with_one_key_record_it_once(IIdempotentUsageTracker tracker, string consumer)
    {
        var key = $"generation:video:{consumer}";

        var recorded = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => Task.Run(async () => await tracker.RecordOnceAsync(consumer, Cost(0.25), key))));

        Assert.Equal(1, recorded.Count(r => r));
        var total = await tracker.TotalAsync(consumer);
        Assert.Equal(0.25, total.CostUsd, 6);
        Assert.Equal(1, total.Calls);
    }

    public static async Task A_blank_key_is_refused(IIdempotentUsageTracker tracker, string consumer)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await tracker.RecordOnceAsync(consumer, Cost(0.5), " "));
        Assert.Equal(UsageTotals.Empty, await tracker.TotalAsync(consumer));
    }

    // ---- TABLE-WIDE: excluded on the shared Postgres container, by name, with the reason recorded -------

    /// <summary>The host's retention: a key recorded before the instant is forgotten, so that key bills again, and
    /// one recorded after it stays.</summary>
    [Unscoped("a prune reaches every key older than the instant")]
    public static async Task Pruning_forgets_only_keys_recorded_before_the_instant(IIdempotentUsageTracker tracker, string consumer)
    {
        var key = $"generation:video:{consumer}";
        await tracker.RecordOnceAsync(consumer, Cost(0.5), key);

        Assert.Equal(0, await tracker.PruneRecordKeysAsync(DateTimeOffset.UtcNow.AddHours(-1)));
        Assert.False(await tracker.RecordOnceAsync(consumer, Cost(0.5), key));

        Assert.Equal(1, await tracker.PruneRecordKeysAsync(DateTimeOffset.UtcNow.AddMinutes(1)));
        Assert.True(await tracker.RecordOnceAsync(consumer, Cost(0.5), key));
        Assert.Equal(1.0, (await tracker.TotalAsync(consumer)).CostUsd, 6);   // pruning forgets keys, never spend
    }
}
