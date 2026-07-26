using Lyntai.Jobs;
using Lyntai.Storage;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Storage;

namespace Lyntai.Tests.Jobs;

/// <summary>The full <see cref="JobStoreContract"/> against <see cref="SqliteJobStore"/> over a per-test
/// temp db (every fact inherited from <see cref="JobStoreContractFacts"/>), plus the SQL-specific
/// concerns: no double-claim under real concurrency, and the TEXT-timestamp lease boundary.</summary>
public class SqliteJobStoreTests : JobStoreContractFacts, IDisposable
{
    private readonly TempDb _db = new();
    public void Dispose() => _db.Dispose();

    protected override (IJobStore, MutableClock) New()
    {
        var clock = new MutableClock();
        return (new SqliteJobStore(_db.Factory, clock.Get), clock);
    }

    [Fact]
    public async Task Concurrent_claims_never_double_grab()
    {
        var store = new SqliteJobStore(_db.Factory); // real clock — this is a genuine concurrency test
        const int n = 20;
        for (var i = 0; i < n; i++) await store.EnqueueAsync(new JobSpec("race", "t", "{}"));

        // 40 workers race for 20 jobs; the atomic claim must give each job to exactly one
        var claims = await Task.WhenAll(Enumerable.Range(0, n * 2)
            .Select(i => store.ClaimNextAsync("race", $"w{i}", TimeSpan.FromMinutes(5))));

        var ids = claims.Where(j => j is not null).Select(j => j!.Id).ToList();
        Assert.Equal(n, ids.Count);              // exactly the 20 jobs claimed
        Assert.Equal(n, ids.Distinct().Count()); // every one distinct — no double-grab
    }

    [Fact]
    public async Task Stale_lease_boundary_compares_text_timestamps_correctly()
    {
        // the lease comparison is a TEXT (ISO-8601) string compare; prove it's chronologically correct
        // right at the boundary (values with and without fractional seconds)
        var (store, clock) = New();
        var lease = TimeSpan.FromMinutes(1);
        var id = await store.EnqueueAsync(new JobSpec("b", "t", "{}"));
        await store.ClaimNextAsync("b", "w1", lease);

        clock.Advance(lease - TimeSpan.FromMilliseconds(1));         // just inside the lease
        Assert.Null(await store.ClaimNextAsync("b", "w2", lease));    // not yet stale
        clock.Advance(TimeSpan.FromMilliseconds(2));                  // now just past
        Assert.Equal(id, (await store.ClaimNextAsync("b", "w2", lease))!.Id);
    }
}
