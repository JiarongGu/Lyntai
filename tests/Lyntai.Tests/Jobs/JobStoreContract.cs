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

    public static async Task Same_tick_same_priority_claims_in_id_order(IJobStore store, MutableClock clock)
    {
        // two jobs, identical lane/priority/available_at (clock not advanced) → the tiebreak is the id,
        // consistently on every backend (SQL: ORDER BY …, id; InMemory now matches via the id string)
        var id1 = await store.EnqueueAsync(Spec());
        var id2 = await store.EnqueueAsync(Spec());
        var expectedFirst = string.CompareOrdinal(id1.ToString(), id2.ToString()) < 0 ? id1 : id2;

        var first = await store.ClaimNextAsync("default", "w1", Lease);
        Assert.Equal(expectedFirst, first!.Id);
    }

    public static async Task Pause_holds_a_pending_job_out_of_claims_then_resume_restores_it(IJobStore store, MutableClock clock)
    {
        var id = await store.EnqueueAsync(Spec());

        Assert.True(await store.PauseAsync(id));                            // Pending → Paused
        Assert.Equal(JobStatus.Paused, (await store.GetAsync(id))!.Status);
        Assert.Null(await store.ClaimNextAsync("default", "w1", Lease));    // a Paused job is not claimable
        Assert.False(await store.PauseAsync(id));                           // already Paused → no-op

        Assert.True(await store.ResumeAsync(id));                           // Paused → Pending
        Assert.Equal(JobStatus.Pending, (await store.GetAsync(id))!.Status);
        Assert.False(await store.ResumeAsync(id));                          // not Paused → no-op
        Assert.Equal(id, (await store.ClaimNextAsync("default", "w1", Lease))!.Id); // runnable again
    }

    public static async Task Progress_and_steps_are_readable_while_running_and_fenced(IJobStore store, MutableClock clock)
    {
        var id = await store.EnqueueAsync(Spec());
        await store.ClaimNextAsync("default", "w1", Lease);

        Assert.True(await store.ReportProgressAsync(id, "w1", 3, 10, "phase-1"));
        Assert.True(await store.ReportStepAsync(id, "w1", "started"));
        Assert.True(await store.ReportStepAsync(id, "w1", "halfway"));

        // readable WHILE the job is still Running — the point of live progress
        var job = await store.GetAsync(id);
        Assert.Equal(JobStatus.Running, job!.Status);
        Assert.Equal(3, job.Progress);
        Assert.Equal(10, job.Total);
        Assert.Equal("phase-1", job.Stage);
        Assert.Equal(["started", "halfway"], JobStepLog.Parse(job.StepLog).Select(s => s.Message));

        // fenced: a worker that doesn't hold the claim can't report
        Assert.False(await store.ReportProgressAsync(id, "intruder", 9, 10, "x"));
        Assert.False(await store.ReportStepAsync(id, "intruder", "nope"));
        Assert.Equal(3, (await store.GetAsync(id))!.Progress); // unchanged
    }

    public static async Task Concurrent_step_reports_all_land(IJobStore store, MutableClock clock)
    {
        var id = await store.EnqueueAsync(Spec());
        await store.ClaimNextAsync("default", "w1", Lease);

        const int n = 25; // fire many concurrent reports from the "handler" — none may be lost to a RMW race
        var oks = await Task.WhenAll(Enumerable.Range(0, n).Select(i => store.ReportStepAsync(id, "w1", $"step-{i}")));
        Assert.All(oks, Assert.True);

        var messages = JobStepLog.Parse((await store.GetAsync(id))!.StepLog).Select(s => s.Message).ToList();
        Assert.Equal(n, messages.Count);
        Assert.Equal(Enumerable.Range(0, n).Select(i => $"step-{i}").OrderBy(m => m),
            messages.OrderBy(m => m)); // every step present, exactly once
    }

    public static async Task Pause_only_affects_a_pending_job(IJobStore store, MutableClock clock)
    {
        var running = await store.EnqueueAsync(Spec());
        await store.ClaimNextAsync("default", "w1", Lease);
        Assert.False(await store.PauseAsync(running)); // can't pause a Running job (use cancel/admission control)
        Assert.Equal(JobStatus.Running, (await store.GetAsync(running))!.Status);
    }

    public static async Task Request_cancel_flags_a_running_job_then_cancel_running_finalizes(IJobStore store, MutableClock clock)
    {
        var id = await store.EnqueueAsync(Spec());
        Assert.False(await store.RequestCancelAsync(id)); // still Pending → no-op (Pending uses CancelAsync)

        await store.ClaimNextAsync("default", "w1", Lease);
        Assert.True(await store.RequestCancelAsync(id));  // Running → flag set
        Assert.True((await store.GetAsync(id))!.CancelRequested);

        Assert.False(await store.CancelRunningAsync(id, "intruder")); // fenced by worker
        Assert.True(await store.CancelRunningAsync(id, "w1"));
        Assert.Equal(JobStatus.Cancelled, (await store.GetAsync(id))!.Status);
    }

    // ---- partition keys (actor-mailbox: same key serial+FIFO, different keys parallel) ----------------
    // Parameterized by `lane` so the Postgres backend can namespace each run to a unique lane on its shared
    // container (partition keys are also namespaced off the lane string for the same reason). FIFO within a
    // partition is `ORDER BY available_at, id` — so these tests advance the clock a tick between enqueues to
    // make each job's available_at strictly earlier than the next (a real, order-independent FIFO), matching
    // how the store defines "earliest" (see Same_tick_same_priority_claims_in_id_order for the same-tick case).
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    public static async Task Same_partition_serializes_and_is_fifo(IJobStore store, MutableClock clock, string lane = "default")
    {
        var p = lane + "-P";
        // enqueue J1, J2, J3 in strict FIFO order on ONE lane with the SAME partition key
        var j1 = await store.EnqueueAsync(new JobSpec(lane, "t", "1", PartitionKey: p));
        clock.Advance(Tick);
        var j2 = await store.EnqueueAsync(new JobSpec(lane, "t", "2", PartitionKey: p));
        clock.Advance(Tick);
        var j3 = await store.EnqueueAsync(new JobSpec(lane, "t", "3", PartitionKey: p));

        // first claim → J1 (earliest of the partition)
        var first = await store.ClaimNextAsync(lane, "w1", Lease);
        Assert.Equal(j1, first!.Id);

        // a SECOND worker claiming while J1 is Running → null: the partition is busy, J2/J3 are blocked
        Assert.Null(await store.ClaimNextAsync(lane, "w2", Lease));

        // complete J1 → the partition frees; next claim returns J2 (strictly, not J3)
        Assert.True(await store.CompleteAsync(j1, "w1"));
        var second = await store.ClaimNextAsync(lane, "w2", Lease);
        Assert.Equal(j2, second!.Id);
        Assert.Null(await store.ClaimNextAsync(lane, "w3", Lease)); // J3 still blocked behind J2

        // and finally J3 after J2 completes
        Assert.True(await store.CompleteAsync(j2, "w2"));
        Assert.Equal(j3, (await store.ClaimNextAsync(lane, "w3", Lease))!.Id);
    }

    public static async Task Different_partitions_run_in_parallel(IJobStore store, MutableClock clock, string lane = "default")
    {
        // two jobs on ONE lane with DIFFERENT partition keys — they must not block each other
        var a = await store.EnqueueAsync(new JobSpec(lane, "t", "a", PartitionKey: lane + "-A"));
        var b = await store.EnqueueAsync(new JobSpec(lane, "t", "b", PartitionKey: lane + "-B"));

        var first = await store.ClaimNextAsync(lane, "w1", Lease);
        var second = await store.ClaimNextAsync(lane, "w2", Lease); // different key → still claimable

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal([a, b], new[] { first!.Id, second!.Id }.OrderBy(x => x == a ? 0 : 1)); // both, distinct
        Assert.NotEqual(first.Id, second.Id);
    }

    public static async Task Priority_is_ignored_within_a_partition_but_honored_across(IJobStore store, MutableClock clock, string lane = "default")
    {
        var p = lane + "-P";
        // WITHIN the partition: J1 enqueued first at low priority, J2 second at HIGH priority. FIFO wins —
        // J2 must NOT jump ahead of J1 despite its higher priority.
        var j1 = await store.EnqueueAsync(new JobSpec(lane, "t", "1", Priority: 1, PartitionKey: p));
        clock.Advance(Tick);
        var j2 = await store.EnqueueAsync(new JobSpec(lane, "t", "2", Priority: 9, PartitionKey: p));
        clock.Advance(Tick);
        // an UNPARTITIONED job at middling priority — across-partition ordering IS by priority
        var mid = await store.EnqueueAsync(new JobSpec(lane, "t", "mid", Priority: 5));

        // across partitions/unpartitioned, higher priority claims first: the partition's ELIGIBLE job is J1
        // (pri 1, the earliest of P), so `mid` (pri 5) outranks it globally and claims first
        Assert.Equal(mid, (await store.ClaimNextAsync(lane, "w1", Lease))!.Id);
        // now the only eligible job is J1 (J2 is blocked behind it despite pri 9) → FIFO within the partition
        Assert.Equal(j1, (await store.ClaimNextAsync(lane, "w2", Lease))!.Id);
        Assert.Null(await store.ClaimNextAsync(lane, "w3", Lease)); // J2 blocked while J1 runs
        Assert.True(await store.CompleteAsync(j1, "w2"));
        Assert.Equal(j2, (await store.ClaimNextAsync(lane, "w3", Lease))!.Id); // J2 only after J1
    }

    public static async Task Stale_partition_running_is_reclaimed_before_later_pending(IJobStore store, MutableClock clock, string lane = "default")
    {
        var p = lane + "-P";
        var j1 = await store.EnqueueAsync(new JobSpec(lane, "t", "1", PartitionKey: p));
        clock.Advance(Tick);
        var j2 = await store.EnqueueAsync(new JobSpec(lane, "t", "2", PartitionKey: p));

        // J1 claimed + running, J2 blocked behind it
        Assert.Equal(j1, (await store.ClaimNextAsync(lane, "w1", Lease))!.Id);

        // w1 crashes: J1's lease goes stale. The next claim must RE-CLAIM J1 (resume its position), NOT skip
        // to the later Pending J2.
        clock.Advance(Lease + TimeSpan.FromSeconds(1));
        var reclaimed = await store.ClaimNextAsync(lane, "w2", Lease);
        Assert.Equal(j1, reclaimed!.Id);       // the stale Running of P, not J2
        Assert.Equal("w2", reclaimed.ClaimedBy);
        Assert.Equal(2, reclaimed.Attempts);   // reclaimed → attempts incremented

        // J2 still blocked while J1 (now freshly leased by w2) runs
        Assert.Null(await store.ClaimNextAsync(lane, "w3", Lease));
        Assert.True(await store.CompleteAsync(j1, "w2"));
        Assert.Equal(j2, (await store.ClaimNextAsync(lane, "w3", Lease))!.Id);
    }
}
