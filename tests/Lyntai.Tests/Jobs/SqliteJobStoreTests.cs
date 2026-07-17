using Lyntai.Jobs;
using Lyntai.Storage;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage;

namespace Lyntai.Tests.Jobs;

/// <summary>Runs the full <see cref="JobStoreContract"/> against <see cref="SqliteJobStore"/> over a
/// per-test temp db, plus the SQL-specific concerns: no double-claim under real concurrency, and the
/// TEXT-timestamp lease boundary.</summary>
public class SqliteJobStoreTests
{
    private static async Task Run(Func<IJobStore, MutableClock, Task> scenario)
    {
        using var db = new TempDb();
        var clock = new MutableClock();
        await scenario(new SqliteJobStore(db.Factory, clock.Get), clock);
    }

    [Fact] public Task Claim_flips_running() => Run(JobStoreContract.Claim_flips_to_running_and_increments_attempts);
    [Fact] public Task Empty_lane_null() => Run(JobStoreContract.Empty_lane_claims_null);
    [Fact] public Task Two_claims_distinct() => Run(JobStoreContract.Two_claims_never_return_the_same_job);
    [Fact] public Task Complete_terminal() => Run(JobStoreContract.Complete_is_terminal);
    [Fact] public Task Fail_retry_requeues() => Run(JobStoreContract.Fail_with_retry_requeues_available_later);
    [Fact] public Task Fail_terminal() => Run(JobStoreContract.Fail_without_retry_is_terminal);
    [Fact] public Task Checkpoint_renews_lease() => Run(JobStoreContract.Checkpoint_round_trips_and_renews_the_lease);
    [Fact] public Task Stale_reclaim() => Run(JobStoreContract.Stale_lease_is_reclaimed_with_the_checkpoint);
    [Fact] public Task Fenced_by_worker() => Run(JobStoreContract.Writes_are_fenced_by_worker_id);
    [Fact] public Task Cancel_pending_only() => Run(JobStoreContract.Cancel_only_affects_pending);
    [Fact] public Task Active_lanes_and_count() => Run(JobStoreContract.Active_lanes_and_running_count);

    [Fact]
    public async Task Concurrent_claims_never_double_grab()
    {
        using var db = new TempDb();
        var store = new SqliteJobStore(db.Factory); // real clock — this is a genuine concurrency test
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
        using var db = new TempDb();
        var clock = new MutableClock();
        var store = new SqliteJobStore(db.Factory, clock.Get);
        var lease = TimeSpan.FromMinutes(1);
        var id = await store.EnqueueAsync(new JobSpec("b", "t", "{}"));
        await store.ClaimNextAsync("b", "w1", lease);

        clock.Advance(lease - TimeSpan.FromMilliseconds(1));         // just inside the lease
        Assert.Null(await store.ClaimNextAsync("b", "w2", lease));    // not yet stale

        clock.Advance(TimeSpan.FromMilliseconds(2));                  // now just past
        Assert.Equal(id, (await store.ClaimNextAsync("b", "w2", lease))!.Id);
    }
}
