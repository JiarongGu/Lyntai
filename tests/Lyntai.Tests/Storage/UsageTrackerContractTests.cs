using Lyntai.Inference.Budgeting;
using Lyntai.Storage.Sqlite;
using Lyntai.Inference;

namespace Lyntai.Tests.Storage;

/// <summary>Every <see cref="UsageTrackerContract"/> method as a [Fact] — derive with a tracker factory and
/// the WHOLE contract runs, so a backend cannot silently skip one. Postgres deliberately does not
/// derive: it runs the Uid-scoped subset against the shared container (see
/// <c>PostgresGovernanceStoreTests</c>), and <see cref="PostgresContractCoverageTests"/> is what makes that
/// subset structural rather than remembered.</summary>
public abstract class UsageTrackerContractFacts
{
    protected abstract IUsageTracker NewTracker();

    /// <summary>A fresh handle over the SAME store, so a persistent backend is asserted to have PERSISTED
    /// rather than to have cached in the instance under test. InMemory returns the same instance.</summary>
    protected virtual IUsageTracker Reopen(IUsageTracker tracker) => tracker;

    [Fact] public Task Accumulates() => UsageTrackerContract.Records_accumulate_per_consumer(NewTracker(), "c");
    [Fact] public Task Unrecorded() => UsageTrackerContract.An_unrecorded_consumer_is_Empty(NewTracker(), "c");
    [Fact] public Task Casings() => UsageTrackerContract.Consumer_identity_aggregates_across_casings(NewTracker(), "c");
    [Fact] public Task Non_ascii_casings() => UsageTrackerContract.Consumer_identity_aggregates_across_non_ASCII_casings(NewTracker(), "c");
    [Fact] public Task Reset_one() => UsageTrackerContract.Resetting_a_consumer_clears_it(NewTracker(), "c");
    [Fact] public Task Reset_scoped() => UsageTrackerContract.Resetting_ONE_consumer_leaves_the_others_intact(NewTracker(), "c");
    [Fact] public Task Reset_casing() => UsageTrackerContract.Resetting_is_case_insensitive_like_the_totals(NewTracker(), "c");
    [Fact] public Task Global_total() => UsageTrackerContract.The_global_total_sums_across_consumers(NewTracker(), "c");
    [Fact] public Task Reset_all() => UsageTrackerContract.Resetting_everything_clears_every_consumer(NewTracker(), "c");

    /// <summary>Totals survive the instance that recorded them. Vacuous on InMemory by construction, which
    /// is why <see cref="Reopen"/> is overridden only where it can mean something.</summary>
    [Fact]
    public async Task Totals_are_read_back_by_a_FRESH_handle_over_the_same_store()
    {
        var tracker = NewTracker();
        await tracker.RecordAsync("persisted", new Lyntai.Inference.ProviderUsage(7, 3, CostUsd: 0.07));

        Assert.Equal(10, (await Reopen(tracker).TotalAsync("persisted")).TotalTokens);
    }
}

/// <summary>The <see cref="UsageTrackerContract"/> against the in-process default.</summary>
public class InMemoryUsageTrackerContractTests : UsageTrackerContractFacts
{
    private readonly InMemoryUsageTracker _tracker = new();
    protected override IUsageTracker NewTracker() => _tracker;
}

/// <summary>The <see cref="UsageTrackerContract"/> against SQLite over a per-test temp db.</summary>
public class SqliteUsageTrackerContractTests : UsageTrackerContractFacts, IDisposable
{
    private readonly TempDb _db = new();
    protected override IUsageTracker NewTracker() => new SqliteUsageTracker(_db.Factory);
    protected override IUsageTracker Reopen(IUsageTracker tracker) => new SqliteUsageTracker(_db.Factory);
    public void Dispose() => _db.Dispose();
}

/// <summary>Every <see cref="UsageReservationContract"/> method as a [Fact], for each tracker that can reserve.
/// Postgres runs the consumer-scoped subset in <c>PostgresGovernanceStoreTests</c>.</summary>
public abstract class UsageReservationContractFacts
{
    protected abstract IReservingUsageTracker NewTracker();

    [Fact] public Task Fits_then_refuses() => UsageReservationContract.A_hold_that_fits_is_granted_and_the_next_that_does_not_is_refused(NewTracker(), "c");
    [Fact] public Task Reconcile_records() => UsageReservationContract.Reconciling_records_the_actual_and_releases_the_hold(NewTracker(), "c");
    [Fact] public Task Reconcile_without_usage() => UsageReservationContract.Reconciling_without_usage_releases_and_records_nothing(NewTracker(), "c");
    [Fact] public Task Committed_spend() => UsageReservationContract.Committed_spend_counts_against_the_cap(NewTracker(), "c");
    [Fact] public Task Token_cap() => UsageReservationContract.A_token_cap_binds_tokens(NewTracker(), "c");
    [Fact] public Task Racing() => UsageReservationContract.Racing_reservations_admit_exactly_what_fits(NewTracker(), "c");
    [Fact] public Task Expired() => UsageReservationContract.An_expired_hold_no_longer_counts(NewTracker(), "c");
    [Fact] public Task Casing() => UsageReservationContract.Holds_fold_consumer_casing_like_the_totals(NewTracker(), "c");
    [Fact] public Task Other_consumer() => UsageReservationContract.A_consumer_cap_does_not_count_another_consumers_holds(NewTracker(), "c");
    [Fact] public Task Reset_keeps_holds() => UsageReservationContract.Resetting_spend_leaves_holds_in_place(NewTracker(), "c");
    [Fact] public Task Global_cap() => UsageReservationContract.A_global_cap_counts_every_consumers_holds(NewTracker(), "c");
}

/// <summary>The <see cref="UsageReservationContract"/> against the in-process default.</summary>
public class InMemoryUsageReservationContractTests : UsageReservationContractFacts
{
    protected override IReservingUsageTracker NewTracker() => new InMemoryUsageTracker();
}

/// <summary>The <see cref="UsageReservationContract"/> against SQLite over a per-test temp db.</summary>
public class SqliteUsageReservationContractTests : UsageReservationContractFacts, IDisposable
{
    private readonly TempDb _db = new();
    protected override IReservingUsageTracker NewTracker() => new SqliteUsageTracker(_db.Factory);
    public void Dispose() => _db.Dispose();
}
