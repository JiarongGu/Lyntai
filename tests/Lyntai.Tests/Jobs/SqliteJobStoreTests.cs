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
    public async Task Step_reports_for_DIFFERENT_jobs_do_not_serialize_behind_one_gate()
    {
        // The step-log read-modify-write needs same-JOB serialization only — the fenced UPDATE already
        // makes cross-process interleaving safe — but a store-wide gate held across two DB round-trips
        // serialized every concurrent job's reporting. The clock is called INSIDE the gate, so two reports
        // reaching it at once is the overlap a store-wide gate makes impossible: `inside` is a live count
        // (peak overlap), not an arrival total, because sequential reports also reach two eventually.
        var armed = false;
        var inside = 0;
        var bothInside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DateTimeOffset Clock()
        {
            if (Volatile.Read(ref armed))
            {
                if (Interlocked.Increment(ref inside) >= 2) bothInside.TrySetResult();
                try { bothInside.Task.Wait(TimeSpan.FromSeconds(5)); }   // false on timeout — the serialized path's exit
                finally { Interlocked.Decrement(ref inside); }
            }
            return DateTimeOffset.UtcNow;
        }

        var store = new SqliteJobStore(_db.Factory, Clock);
        var a = await store.EnqueueAsync(new JobSpec("overlap", "t", "{}"));
        var b = await store.EnqueueAsync(new JobSpec("overlap", "t", "{}"));
        await store.ClaimNextAsync("overlap", "w1", TimeSpan.FromMinutes(5));
        await store.ClaimNextAsync("overlap", "w1", TimeSpan.FromMinutes(5));

        Volatile.Write(ref armed, true);
        // Task.Run is load-bearing: Microsoft.Data.Sqlite completes its async methods synchronously, so
        // called inline the first report would block THIS thread inside the clock before the second call
        // even started — sequential by construction, whatever the store's gate does.
        var reports = Task.WhenAll(
            Task.Run(() => store.ReportStepAsync(a, "w1", "a step")),
            Task.Run(() => store.ReportStepAsync(b, "w1", "b step")));
        await reports.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(bothInside.Task.IsCompleted,
            "the two reports never overlapped — a store-wide gate serialized them");
        Assert.All(await reports, Assert.True);
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
