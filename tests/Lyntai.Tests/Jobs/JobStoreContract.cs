using Lyntai.Jobs;
using Lyntai.Storage;

namespace Lyntai.Tests.Jobs;

/// <summary>A controllable clock cell the store is built over, so lease/retry timing is deterministic.</summary>
public sealed class MutableClock
{
    public DateTimeOffset Now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
    public DateTimeOffset Get() => Now;
    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>
/// Backend-agnostic <see cref="IJobStore"/> contract, run by the InMemory, SQLite, and Postgres test
/// classes against a store built over a shared <see cref="MutableClock"/> — so claim/lease/fencing/retry
/// semantics are pinned identically for every backend.
/// </summary>
public static class JobStoreContract
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);

    private static JobSpec Spec(string lane = "default", string type = "t", string payload = "{}") =>
        new(lane, type, payload);

    public static async Task Claim_flips_to_running_and_increments_attempts(IJobStore store, MutableClock clock)
    {
        var id = await store.EnqueueAsync(Spec());
        var job = await store.ClaimNextAsync("default", "w1", Lease);

        Assert.NotNull(job);
        Assert.Equal(id, job!.Id);
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Equal(1, job.Attempts);
        Assert.Equal("w1", job.ClaimedBy);
    }

    public static async Task Empty_lane_claims_null(IJobStore store, MutableClock clock)
    {
        Assert.Null(await store.ClaimNextAsync("nothing-here", "w1", Lease));
    }

    public static async Task Two_claims_never_return_the_same_job(IJobStore store, MutableClock clock)
    {
        await store.EnqueueAsync(Spec());
        await store.EnqueueAsync(Spec());

        var a = await store.ClaimNextAsync("default", "w1", Lease);
        var b = await store.ClaimNextAsync("default", "w1", Lease);
        var c = await store.ClaimNextAsync("default", "w1", Lease);

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotEqual(a!.Id, b!.Id);
        Assert.Null(c); // only two enqueued
    }

    public static async Task Complete_is_terminal(IJobStore store, MutableClock clock)
    {
        var id = await store.EnqueueAsync(Spec());
        await store.ClaimNextAsync("default", "w1", Lease);

        Assert.True(await store.CompleteAsync(id, "w1"));
        Assert.Equal(JobStatus.Succeeded, (await store.GetAsync(id))!.Status);
        Assert.Null(await store.ClaimNextAsync("default", "w1", Lease)); // not re-runnable
    }

    public static async Task Fail_with_retry_requeues_available_later(IJobStore store, MutableClock clock)
    {
        var id = await store.EnqueueAsync(Spec());
        await store.ClaimNextAsync("default", "w1", Lease);

        var retryAt = clock.Now + TimeSpan.FromMinutes(5);
        Assert.True(await store.FailAsync(id, "w1", "boom", retryAt));
        Assert.Equal(JobStatus.Pending, (await store.GetAsync(id))!.Status);

        Assert.Null(await store.ClaimNextAsync("default", "w1", Lease)); // not yet available
        clock.Advance(TimeSpan.FromMinutes(6));
        var again = await store.ClaimNextAsync("default", "w1", Lease);
        Assert.NotNull(again);
        Assert.Equal(2, again!.Attempts); // second attempt
        Assert.Equal("boom", again.LastError);
    }

    public static async Task Fail_without_retry_is_terminal(IJobStore store, MutableClock clock)
    {
        var id = await store.EnqueueAsync(Spec());
        await store.ClaimNextAsync("default", "w1", Lease);

        Assert.True(await store.FailAsync(id, "w1", "fatal"));
        Assert.Equal(JobStatus.Failed, (await store.GetAsync(id))!.Status);
        Assert.Null(await store.ClaimNextAsync("default", "w1", Lease));
    }

    public static async Task Checkpoint_round_trips_and_renews_the_lease(IJobStore store, MutableClock clock)
    {
        var id = await store.EnqueueAsync(Spec());
        await store.ClaimNextAsync("default", "w1", Lease);

        clock.Advance(TimeSpan.FromSeconds(50));                 // within the 60s lease
        Assert.True(await store.SaveCheckpointAsync(id, "w1", """{"step":2}"""));
        Assert.Equal("""{"step":2}""", (await store.GetAsync(id))!.Checkpoint);

        // the checkpoint renewed the lease at t+50s, so at t+80s (30s after) it's NOT yet stale
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Null(await store.ClaimNextAsync("default", "w2", Lease)); // still owned by w1
    }

    public static async Task Stale_lease_is_reclaimed_with_the_checkpoint(IJobStore store, MutableClock clock)
    {
        var id = await store.EnqueueAsync(Spec());
        await store.ClaimNextAsync("default", "w1", Lease);
        await store.SaveCheckpointAsync(id, "w1", """{"step":1}""");

        clock.Advance(TimeSpan.FromMinutes(2)); // > lease → w1 presumed dead
        var reclaimed = await store.ClaimNextAsync("default", "w2", Lease);

        Assert.NotNull(reclaimed);
        Assert.Equal(id, reclaimed!.Id);
        Assert.Equal("w2", reclaimed.ClaimedBy);
        Assert.Equal("""{"step":1}""", reclaimed.Checkpoint); // resumes from the checkpoint
        Assert.Equal(2, reclaimed.Attempts);
    }

    public static async Task Writes_are_fenced_by_worker_id(IJobStore store, MutableClock clock)
    {
        var id = await store.EnqueueAsync(Spec());
        await store.ClaimNextAsync("default", "w1", Lease);

        // a different worker (a zombie / re-claimer) cannot mutate w1's job
        Assert.False(await store.SaveCheckpointAsync(id, "intruder", "x"));
        Assert.False(await store.CompleteAsync(id, "intruder"));
        Assert.False(await store.FailAsync(id, "intruder", "no"));
        Assert.Equal(JobStatus.Running, (await store.GetAsync(id))!.Status); // untouched
    }

    public static async Task Cancel_only_affects_pending(IJobStore store, MutableClock clock)
    {
        var pending = await store.EnqueueAsync(Spec());
        Assert.True(await store.CancelAsync(pending));
        Assert.Equal(JobStatus.Cancelled, (await store.GetAsync(pending))!.Status);

        var running = await store.EnqueueAsync(Spec());
        await store.ClaimNextAsync("default", "w1", Lease);
        Assert.False(await store.CancelAsync(running)); // can't cancel a running job
    }

    public static async Task Active_lanes_and_running_count(IJobStore store, MutableClock clock)
    {
        await store.EnqueueAsync(Spec(lane: "a"));
        await store.EnqueueAsync(Spec(lane: "b"));
        await store.ClaimNextAsync("a", "w1", Lease);

        var lanes = await store.ActiveLanesAsync();
        Assert.Contains("a", lanes);
        Assert.Contains("b", lanes);
        Assert.Equal(1, await store.CountRunningAsync("a"));
        Assert.Equal(0, await store.CountRunningAsync("b"));
    }

    public static async Task Higher_priority_is_claimed_first(IJobStore store, MutableClock clock)
    {
        await store.EnqueueAsync(Spec() with { Priority = 1 });          // low, enqueued FIRST
        var hi = await store.EnqueueAsync(Spec() with { Priority = 5 }); // high, enqueued second

        var claimed = await store.ClaimNextAsync("default", "w1", Lease);
        Assert.Equal(hi, claimed!.Id); // priority beats FIFO within the lane
        Assert.Equal(5, claimed.Priority);
    }

    public static async Task Dead_letter_is_terminal_inspectable_and_fenced(IJobStore store, MutableClock clock)
    {
        var id = await store.EnqueueAsync(Spec());
        await store.ClaimNextAsync("default", "w1", Lease);

        Assert.False(await store.DeadLetterAsync(id, "intruder", "nope")); // fenced by worker
        Assert.True(await store.DeadLetterAsync(id, "w1", "exhausted"));

        var job = await store.GetAsync(id);
        Assert.Equal(JobStatus.Dead, job!.Status);
        Assert.Equal("exhausted", job.LastError);
        Assert.Contains(await store.ListAsync(JobStatus.Dead), j => j.Id == id); // shows in the DLQ
        Assert.Null(await store.ClaimNextAsync("default", "w1", Lease));         // terminal, not reclaimable
    }

    public static async Task Replay_requeues_a_dead_job(IJobStore store, MutableClock clock)
    {
        var id = await store.EnqueueAsync(Spec());
        await store.ClaimNextAsync("default", "w1", Lease);
        await store.DeadLetterAsync(id, "w1", "exhausted");

        Assert.True(await store.ReplayAsync(id));
        var job = await store.GetAsync(id);
        Assert.Equal(JobStatus.Pending, job!.Status);
        Assert.Equal(0, job.Attempts);   // attempts reset
        Assert.Null(job.LastError);      // error cleared

        var reclaimed = await store.ClaimNextAsync("default", "w1", Lease); // runnable again
        Assert.Equal(id, reclaimed!.Id);
        Assert.Equal(1, reclaimed.Attempts);
        Assert.False(await store.ReplayAsync(id)); // now Running (not Dead/Failed) → no-op
    }
}
