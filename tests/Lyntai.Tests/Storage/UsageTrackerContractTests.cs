using Lyntai.Inference.Budgeting;
using Lyntai.Storage.Sqlite;
using Lyntai.Inference;

namespace Lyntai.Tests.Storage;

/// <summary>Every <see cref="UsageTrackerContract"/> method as a [Fact] — derive with a tracker factory and
/// the WHOLE contract runs, so a backend can no longer silently skip one. Postgres deliberately does not
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
        await tracker.RecordAsync("persisted", new Lyntai.Inference.TextUsage(7, 3, CostUsd: 0.07));

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
