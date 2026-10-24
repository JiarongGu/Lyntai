using Lyntai.Inference.Budgeting;
using Lyntai.Storage.Sqlite;
using Lyntai.Inference;

namespace Lyntai.Tests.Storage;

/// <summary>Every <see cref="UsageTrackerContract"/> fact as a theory case — derive with a tracker factory and
/// the whole contract runs on that backend (<see cref="StoreContractSuite{TStore}"/>).</summary>
public abstract class UsageTrackerContractFacts : StoreContractSuite<IUsageTracker>
{
    protected abstract IUsageTracker NewTracker();
    protected sealed override IUsageTracker CreateStore(string fact) => NewTracker();

    /// <summary>A fresh handle over the SAME store, so a persistent backend is asserted to have PERSISTED
    /// rather than to have cached in the instance under test. InMemory returns the same instance.</summary>
    protected virtual IUsageTracker Reopen(IUsageTracker tracker) => tracker;

    [SkippableTheory, ContractFacts(typeof(UsageTrackerContract))]
    public Task Satisfies_the_contract(string fact) => RunAsync(typeof(UsageTrackerContract), fact);

    /// <summary>Totals survive the instance that recorded them. Vacuous on InMemory by construction, which
    /// is why <see cref="Reopen"/> is overridden only where it can mean something.</summary>
    [SkippableFact]
    public async Task Totals_are_read_back_by_a_FRESH_handle_over_the_same_store()
    {
        var tracker = NewTracker();
        var consumer = "persisted-" + Guid.NewGuid().ToString("N");   // unique: a store may be shared
        await tracker.RecordAsync(consumer, new ProviderUsage(7, 3, CostUsd: 0.07));

        Assert.Equal(10, (await Reopen(tracker).TotalAsync(consumer)).TotalTokens);
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

/// <summary>Every <see cref="UsageReservationContract"/> fact as a theory case, for each tracker that can
/// reserve (<see cref="StoreContractSuite{TStore}"/>).</summary>
public abstract class UsageReservationContractFacts : StoreContractSuite<IReservingUsageTracker>
{
    protected abstract IReservingUsageTracker NewTracker();
    protected sealed override IReservingUsageTracker CreateStore(string fact) => NewTracker();

    [SkippableTheory, ContractFacts(typeof(UsageReservationContract))]
    public Task Satisfies_the_contract(string fact) => RunAsync(typeof(UsageReservationContract), fact);
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
