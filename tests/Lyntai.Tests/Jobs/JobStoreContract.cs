using Lyntai.Jobs;
using Lyntai.Storage;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Jobs;

/// <summary>
/// Backend-agnostic <see cref="IJobStore"/> contract, run by the InMemory, SQLite, and Postgres test
/// classes against a store built over a shared <see cref="MutableClock"/> — so claim/lease/fencing/retry
/// semantics are pinned identically for every backend.
/// <para>Every lane-scoped fact takes its <c>lane</c>, so the Postgres leg runs it on the shared container
/// under a unique one; InMemory and SQLite pass the default. The slot facts are table-wide by design and
/// clear the slot table instead.</para>
/// </summary>
public static class JobStoreContract
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);

    private static JobSpec Spec(string lane = "default", string type = "t", string payload = "{}") =>
        new(lane, type, payload);

    public static async Task Claim_flips_to_running_and_increments_attempts(IJobStore store, MutableClock clock, string lane = "default")
    {
        var id = await store.EnqueueAsync(Spec(lane));
        var job = await store.ClaimNextAsync(lane, "w1", Lease);

        Assert.NotNull(job);
        Assert.Equal(id, job!.Id);
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Equal(1, job.Attempts);
        Assert.Equal("w1", job.ClaimedBy);
    }

    public static async Task Empty_lane_claims_null(IJobStore store, MutableClock clock, string lane = "default")
    {
        Assert.Null(await store.ClaimNextAsync(lane + "-nothing-here", "w1", Lease));
    }

    public static async Task Two_claims_never_return_the_same_job(IJobStore store, MutableClock clock, string lane = "default")
    {
        await store.EnqueueAsync(Spec(lane));
        await store.EnqueueAsync(Spec(lane));

        var a = await store.ClaimNextAsync(lane, "w1", Lease);
        var b = await store.ClaimNextAsync(lane, "w1", Lease);
        var c = await store.ClaimNextAsync(lane, "w1", Lease);

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotEqual(a!.Id, b!.Id);
        Assert.Null(c); // only two enqueued
    }

    public static async Task Complete_is_terminal(IJobStore store, MutableClock clock, string lane = "default")
    {
        var id = await store.EnqueueAsync(Spec(lane));
        await store.ClaimNextAsync(lane, "w1", Lease);

        Assert.True(await store.CompleteAsync(id, "w1"));
        Assert.Equal(JobStatus.Succeeded, (await store.GetAsync(id))!.Status);
        Assert.Null(await store.ClaimNextAsync(lane, "w1", Lease)); // not re-runnable
    }

    public static async Task Fail_with_retry_requeues_available_later(IJobStore store, MutableClock clock, string lane = "default")
    {
        var id = await store.EnqueueAsync(Spec(lane));
        await store.ClaimNextAsync(lane, "w1", Lease);

        var retryAt = clock.Now + TimeSpan.FromMinutes(5);
        Assert.True(await store.FailAsync(id, "w1", "boom", retryAt));
        Assert.Equal(JobStatus.Pending, (await store.GetAsync(id))!.Status);

        Assert.Null(await store.ClaimNextAsync(lane, "w1", Lease)); // not yet available
        clock.Advance(TimeSpan.FromMinutes(6));
        var again = await store.ClaimNextAsync(lane, "w1", Lease);
        Assert.NotNull(again);
        Assert.Equal(2, again!.Attempts); // second attempt
        Assert.Equal("boom", again.LastError);
    }

    public static async Task Fail_without_retry_is_terminal(IJobStore store, MutableClock clock, string lane = "default")
    {
        var id = await store.EnqueueAsync(Spec(lane));
        await store.ClaimNextAsync(lane, "w1", Lease);

        Assert.True(await store.FailAsync(id, "w1", "fatal"));
        Assert.Equal(JobStatus.Failed, (await store.GetAsync(id))!.Status);
        Assert.Null(await store.ClaimNextAsync(lane, "w1", Lease));
    }

    public static async Task Checkpoint_round_trips_and_renews_the_lease(IJobStore store, MutableClock clock, string lane = "default")
    {
        var id = await store.EnqueueAsync(Spec(lane));
        await store.ClaimNextAsync(lane, "w1", Lease);

        clock.Advance(TimeSpan.FromSeconds(50));                 // within the 60s lease
        Assert.True(await store.SaveCheckpointAsync(id, "w1", """{"step":2}"""));
        Assert.Equal("""{"step":2}""", (await store.GetAsync(id))!.Checkpoint);

        // the checkpoint renewed the lease at t+50s, so at t+80s (30s after) it's NOT yet stale
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Null(await store.ClaimNextAsync(lane, "w2", Lease)); // still owned by w1
    }

    public static async Task Stale_lease_is_reclaimed_with_the_checkpoint(IJobStore store, MutableClock clock, string lane = "default")
    {
        var id = await store.EnqueueAsync(Spec(lane));
        await store.ClaimNextAsync(lane, "w1", Lease);
        await store.SaveCheckpointAsync(id, "w1", """{"step":1}""");

        clock.Advance(TimeSpan.FromMinutes(2)); // > lease → w1 presumed dead
        var reclaimed = await store.ClaimNextAsync(lane, "w2", Lease);

        Assert.NotNull(reclaimed);
        Assert.Equal(id, reclaimed!.Id);
        Assert.Equal("w2", reclaimed.ClaimedBy);
        Assert.Equal("""{"step":1}""", reclaimed.Checkpoint); // resumes from the checkpoint
        Assert.Equal(2, reclaimed.Attempts);
    }

    public static async Task Writes_are_fenced_by_worker_id(IJobStore store, MutableClock clock, string lane = "default")
    {
        var id = await store.EnqueueAsync(Spec(lane));
        await store.ClaimNextAsync(lane, "w1", Lease);

        // a different worker (a zombie / re-claimer) cannot mutate w1's job
        Assert.False(await store.SaveCheckpointAsync(id, "intruder", "x"));
        Assert.False(await store.CompleteAsync(id, "intruder"));
        Assert.False(await store.FailAsync(id, "intruder", "no"));
        Assert.Equal(JobStatus.Running, (await store.GetAsync(id))!.Status); // untouched
    }

    /// <summary>A poll returns the job to Pending WITHOUT spending an attempt, and is fenced like every other
    /// write. The un-counting is the whole point of the member (a Retry would dead-letter a healthy render
    /// after MaxAttempts looks), and it is asserted here rather than per backend because the SQL does it with
    /// `attempts=attempts-1` and the in-process store with a floored subtraction.</summary>
    public static async Task Poll_requeues_without_spending_an_attempt_and_is_fenced(
        IJobStore store, MutableClock clock, string lane = "default")
    {
        var id = await store.EnqueueAsync(Spec(lane));
        await store.ClaimNextAsync(lane, "w1", Lease);
        var claimed = (await store.GetAsync(id))!;
        Assert.Equal(1, claimed.Attempts);                       // the claim counted one

        // a different worker cannot drive it backwards — the one outcome that MOVES A JOB BACK, so an
        // unfenced version would let a zombie reset another worker's job forever
        Assert.False(await store.PollAgainAsync(id, "intruder", clock.Now.AddMinutes(1)));
        Assert.Equal(JobStatus.Running, (await store.GetAsync(id))!.Status);

        Assert.True(await store.PollAgainAsync(id, "w1", clock.Now.AddMinutes(1)));
        var polled = (await store.GetAsync(id))!;
        Assert.Equal(JobStatus.Pending, polled.Status);
        Assert.Equal(0, polled.Attempts);                        // the claim's increment is UNDONE
        Assert.Null(polled.LastError);                           // nothing failed, so nothing is reported
        Assert.Null(polled.ClaimedBy);

        // and it is genuinely re-claimable, so polling can continue indefinitely
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.NotNull(await store.ClaimNextAsync(lane, "w1", Lease));
        Assert.Equal(1, (await store.GetAsync(id))!.Attempts);
    }

    // CancelAsync takes a job that has NOT STARTED (Pending here, Paused in
    // Cancel_reaches_a_paused_job_without_resuming_it) and never a Running one — a running job is cancelled
    // cooperatively through RequestCancelAsync, which is the other half of IJobQueue.CancelAsync.
    public static async Task Cancel_takes_a_pending_job_but_not_a_running_one(IJobStore store, MutableClock clock, string lane = "default")
    {
        var pending = await store.EnqueueAsync(Spec(lane));
        Assert.True(await store.CancelAsync(pending));
        Assert.Equal(JobStatus.Cancelled, (await store.GetAsync(pending))!.Status);

        var running = await store.EnqueueAsync(Spec(lane));
        await store.ClaimNextAsync(lane, "w1", Lease);
        Assert.False(await store.CancelAsync(running)); // can't cancel a running job
    }

    // A spec that names no attempt budget gets the ONE shared default (JobSpec.DefaultMaxAttempts) — the
    // number used to be a bare `3` hand-copied into each store's enqueue, so a change to one drifted from
    // the other two silently. Pinned here so every backend answers with the same budget.
    public static async Task Enqueue_without_max_attempts_uses_the_shared_default(IJobStore store, MutableClock clock, string lane = "default")
    {
        var id = await store.EnqueueAsync(Spec(lane)); // JobSpec.MaxAttempts is null — the store fills it in
        Assert.Equal(JobSpec.DefaultMaxAttempts, (await store.GetAsync(id))!.MaxAttempts);

        var pinned = await store.EnqueueAsync(Spec(lane) with { MaxAttempts = 7 }); // an explicit budget still wins
        Assert.Equal(7, (await store.GetAsync(pinned))!.MaxAttempts);
    }

    public static async Task Active_lanes_and_running_count(IJobStore store, MutableClock clock, string lane = "default")
    {
        await store.EnqueueAsync(Spec(lane + "-a"));
        await store.EnqueueAsync(Spec(lane + "-b"));
        await store.ClaimNextAsync(lane + "-a", "w1", Lease);

        var lanes = await store.ActiveLanesAsync();
        Assert.Contains(lane + "-a", lanes);
        Assert.Contains(lane + "-b", lanes);
        Assert.Single(await store.ListAsync(JobStatus.Running, lane + "-a"));
        Assert.Empty(await store.ListAsync(JobStatus.Running, lane + "-b"));
    }

    public static async Task Higher_priority_is_claimed_first(IJobStore store, MutableClock clock, string lane = "default")
    {
        await store.EnqueueAsync(Spec(lane) with { Priority = 1 });          // low, enqueued FIRST
        var hi = await store.EnqueueAsync(Spec(lane) with { Priority = 5 }); // high, enqueued second

        var claimed = await store.ClaimNextAsync(lane, "w1", Lease);
        Assert.Equal(hi, claimed!.Id); // priority beats FIFO within the lane
        Assert.Equal(5, claimed.Priority);
    }

    public static async Task Dead_letter_is_terminal_inspectable_and_fenced(IJobStore store, MutableClock clock, string lane = "default")
    {
        var id = await store.EnqueueAsync(Spec(lane));
        await store.ClaimNextAsync(lane, "w1", Lease);

        Assert.False(await store.DeadLetterAsync(id, "intruder", "nope")); // fenced by worker
        Assert.True(await store.DeadLetterAsync(id, "w1", "exhausted"));

        var job = await store.GetAsync(id);
        Assert.Equal(JobStatus.Dead, job!.Status);
        Assert.Equal("exhausted", job.LastError);
        Assert.Contains(await store.ListAsync(JobStatus.Dead, lane), j => j.Id == id); // shows in the DLQ
        Assert.Null(await store.ClaimNextAsync(lane, "w1", Lease));         // terminal, not reclaimable
    }

    public static async Task Replay_requeues_a_dead_job(IJobStore store, MutableClock clock, string lane = "default")
    {
        var id = await store.EnqueueAsync(Spec(lane));
        await store.ClaimNextAsync(lane, "w1", Lease);
        await store.DeadLetterAsync(id, "w1", "exhausted");

        Assert.True(await store.ReplayAsync(id));
        var job = await store.GetAsync(id);
        Assert.Equal(JobStatus.Pending, job!.Status);
        Assert.Equal(0, job.Attempts);   // attempts reset
        Assert.Null(job.LastError);      // error cleared

        var reclaimed = await store.ClaimNextAsync(lane, "w1", Lease); // runnable again
        Assert.Equal(id, reclaimed!.Id);
        Assert.Equal(1, reclaimed.Attempts);
        Assert.False(await store.ReplayAsync(id)); // now Running (not Dead/Failed) → no-op
    }

    public static async Task Same_tick_same_priority_claims_in_id_order(IJobStore store, MutableClock clock, string lane = "default")
    {
        // two jobs, identical lane/priority/available_at (clock not advanced) → the tiebreak is the id,
        // consistently on every backend (SQL: ORDER BY …, id; InMemory now matches via the id string)
        var id1 = await store.EnqueueAsync(Spec(lane));
        var id2 = await store.EnqueueAsync(Spec(lane));
        var expectedFirst = string.CompareOrdinal(id1.ToString(), id2.ToString()) < 0 ? id1 : id2;

        var first = await store.ClaimNextAsync(lane, "w1", Lease);
        Assert.Equal(expectedFirst, first!.Id);
    }

    public static async Task Pause_holds_a_pending_job_out_of_claims_then_resume_restores_it(IJobStore store, MutableClock clock, string lane = "default")
    {
        var id = await store.EnqueueAsync(Spec(lane));

        Assert.True(await store.PauseAsync(id));                            // Pending → Paused
        Assert.Equal(JobStatus.Paused, (await store.GetAsync(id))!.Status);
        Assert.Null(await store.ClaimNextAsync(lane, "w1", Lease));    // a Paused job is not claimable
        Assert.False(await store.PauseAsync(id));                           // already Paused → no-op

        Assert.True(await store.ResumeAsync(id));                           // Paused → Pending
        Assert.Equal(JobStatus.Pending, (await store.GetAsync(id))!.Status);
        Assert.False(await store.ResumeAsync(id));                          // not Paused → no-op
        Assert.Equal(id, (await store.ClaimNextAsync(lane, "w1", Lease))!.Id); // runnable again
    }

    public static async Task Progress_and_steps_are_readable_while_running_and_fenced(IJobStore store, MutableClock clock, string lane = "default")
    {
        var id = await store.EnqueueAsync(Spec(lane));
        await store.ClaimNextAsync(lane, "w1", Lease);

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

    public static async Task Concurrent_step_reports_all_land(IJobStore store, MutableClock clock, string lane = "default")
    {
        var id = await store.EnqueueAsync(Spec(lane));
        await store.ClaimNextAsync(lane, "w1", Lease);

        // Many concurrent reports from the "handler" — none may be lost to a read-modify-write race. Each on
        // its own thread: a store whose async completes synchronously would otherwise run them in sequence.
        const int n = 25;
        var oks = await Task.WhenAll(Enumerable.Range(0, n)
            .Select(i => Task.Run(() => store.ReportStepAsync(id, "w1", $"step-{i}"))));
        Assert.All(oks, Assert.True);

        var messages = JobStepLog.Parse((await store.GetAsync(id))!.StepLog).Select(s => s.Message).ToList();
        Assert.Equal(n, messages.Count);
        Assert.Equal(Enumerable.Range(0, n).Select(i => $"step-{i}").OrderBy(m => m),
            messages.OrderBy(m => m)); // every step present, exactly once
    }

    public static async Task Pause_only_affects_a_pending_job(IJobStore store, MutableClock clock, string lane = "default")
    {
        var running = await store.EnqueueAsync(Spec(lane));
        await store.ClaimNextAsync(lane, "w1", Lease);
        Assert.False(await store.PauseAsync(running)); // can't pause a Running job (use cancel/admission control)
        Assert.Equal(JobStatus.Running, (await store.GetAsync(running))!.Status);
    }

    // A Paused job is Pending to nobody and Running to nobody, so BOTH halves of the queue's cancel
    // (CancelAsync || RequestCancelAsync) used to miss it and an operator had to ResumeAsync first — which
    // puts the job back in the CLAIMABLE set, so a polling runner could take it in the gap. The pending half
    // now reaches Paused too (the shared JobStoreSql.CancelNotStarted matches `status IN ('Pending','Paused')`);
    // the RUNNING half deliberately stays narrow, because cancelling a running job is a cooperative request
    // to a worker and a held job has no worker to ask.
    public static async Task Cancel_reaches_a_paused_job_without_resuming_it(IJobStore store, MutableClock clock, string lane = "default")
    {
        var id = await store.EnqueueAsync(Spec(lane));
        Assert.True(await store.PauseAsync(id));

        Assert.False(await store.RequestCancelAsync(id));  // not Running → the running half still misses it
        Assert.True(await store.CancelAsync(id));          // Paused → cancelled outright, no resume needed
        Assert.Equal(JobStatus.Cancelled, (await store.GetAsync(id))!.Status);

        // terminal, exactly like a cancelled Pending job: never claimable, not resumable, not cancelled twice
        Assert.Null(await store.ClaimNextAsync(lane, "w1", Lease));
        Assert.False(await store.ResumeAsync(id));
        Assert.False(await store.CancelAsync(id));
    }

    public static async Task Request_cancel_flags_a_running_job_then_cancel_running_finalizes(IJobStore store, MutableClock clock, string lane = "default")
    {
        var id = await store.EnqueueAsync(Spec(lane));
        Assert.False(await store.RequestCancelAsync(id)); // still Pending → no-op (Pending uses CancelAsync)

        await store.ClaimNextAsync(lane, "w1", Lease);
        Assert.True(await store.RequestCancelAsync(id));  // Running → flag set
        Assert.True((await store.GetAsync(id))!.CancelRequested);

        Assert.False(await store.CancelRunningAsync(id, "intruder")); // fenced by worker
        Assert.True(await store.CancelRunningAsync(id, "w1"));
        Assert.Equal(JobStatus.Cancelled, (await store.GetAsync(id))!.Status);
    }

    // ---- partition keys (actor-mailbox: same key serial+FIFO, different keys parallel) ----------------
    // Partition keys are namespaced off the lane string, as the lanes are. FIFO within a
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

    // ---- cross-process concurrency slots (D73) ------------------------------------------------------
    // These pin the properties `InMemoryJobStore.TryAcquireSlotAsync` names as "the semantics the SQL stores
    // must match": lowest free index wins, an index at or above the cap is never handed out, and a slot older
    // than the lease is reclaimable. They run on every backend because the in-process store is the one where a
    // cross-PROCESS cap is meaningless; the SQL stores are the ones that ship it.

    /// <summary>The cap is a real ceiling, and a released slot is reused rather than leaked.</summary>
    public static async Task Slots_are_handed_out_up_to_the_cap_and_reused_after_release(
        IJobStore store, MutableClock clock)
    {
        var lease = TimeSpan.FromMinutes(5);
        var first = await store.TryAcquireSlotAsync(2, "w1", lease);
        var second = await store.TryAcquireSlotAsync(2, "w2", lease);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
        Assert.Null(await store.TryAcquireSlotAsync(2, "w3", lease));   // the cap holds

        await store.ReleaseSlotAsync(second!.Value, "w2");
        Assert.Equal(second, await store.TryAcquireSlotAsync(2, "w3", lease));  // lowest free index wins
    }

    /// <summary>A slot whose holder stopped heartbeating past its lease is reclaimable — the property that
    /// keeps a crashed worker from consuming a slot forever. Heartbeating one keeps it.</summary>
    public static async Task A_slot_past_its_lease_is_reclaimed_and_a_heartbeat_prevents_it(
        IJobStore store, MutableClock clock)
    {
        var lease = TimeSpan.FromMinutes(1);
        var mine = await store.TryAcquireSlotAsync(1, "w1", lease);
        Assert.NotNull(mine);
        Assert.Null(await store.TryAcquireSlotAsync(1, "w2", lease));

        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(mine, await store.TryAcquireSlotAsync(1, "w2", lease));   // stale, so reclaimed

        // …and a live holder keeps it PAST the lease: 90s after w2 took it, but 45s after its heartbeat. Without
        // the heartbeat the slot would be 30s stale here and w3 would take it.
        clock.Advance(TimeSpan.FromSeconds(45));
        await store.HeartbeatSlotsAsync("w2");
        clock.Advance(TimeSpan.FromSeconds(45));
        Assert.Null(await store.TryAcquireSlotAsync(1, "w3", lease));
    }

    /// <summary>The property the heartbeat buys: a holder that keeps beating keeps its slot however many leases
    /// it runs for, and one that stops is reclaimable a lease later. A single expiry cannot serve both — long
    /// enough for an hours-long job lets a crash throttle the fleet for hours.</summary>
    public static async Task A_heartbeating_holder_keeps_its_slot_across_many_leases(IJobStore store, MutableClock clock)
    {
        var lease = TimeSpan.FromSeconds(30);
        Assert.NotNull(await store.TryAcquireSlotAsync(1, "long-runner", lease));

        for (var beat = 0; beat < 10; beat++)
        {
            clock.Advance(TimeSpan.FromSeconds(10));
            await store.HeartbeatSlotsAsync("long-runner");
        }
        Assert.Null(await store.TryAcquireSlotAsync(1, "someone-else", lease));    // still held, 100s in

        clock.Advance(lease + TimeSpan.FromSeconds(1));                            // the beats stop: it died
        Assert.NotNull(await store.TryAcquireSlotAsync(1, "someone-else", lease));
    }

    /// <summary>A stalled holder that wakes and beats must not steal back the slot its successor now holds:
    /// the heartbeat is fenced by worker id exactly as the release is.</summary>
    public static async Task A_heartbeat_does_not_revive_a_slot_already_reclaimed(IJobStore store, MutableClock clock)
    {
        var lease = TimeSpan.FromSeconds(30);
        var slot = await store.TryAcquireSlotAsync(1, "stalled", lease);
        Assert.NotNull(slot);

        clock.Advance(lease + TimeSpan.FromSeconds(1));
        Assert.Equal(slot, await store.TryAcquireSlotAsync(1, "successor", lease));   // taken over
        await store.HeartbeatSlotsAsync("stalled");                                   // too late

        // the successor still OWNS it: its fenced release frees the slot, which a stolen-back one would refuse
        await store.ReleaseSlotAsync(slot!.Value, "successor");
        Assert.Equal(slot, await store.TryAcquireSlotAsync(1, "third", lease));
    }

    /// <summary>The cap is CONFIGURATION, not schema: slots are created lazily and taken only below the cap, so
    /// lowering it strands nothing and needs no migration.</summary>
    public static async Task Lowering_the_cap_needs_no_cleanup(IJobStore store, MutableClock clock)
    {
        var lease = TimeSpan.FromMinutes(5);
        Assert.Equal(0, await store.TryAcquireSlotAsync(3, "w", lease));
        Assert.Equal(1, await store.TryAcquireSlotAsync(3, "w", lease));
        Assert.Equal(2, await store.TryAcquireSlotAsync(3, "w", lease));

        await store.ReleaseSlotAsync(2, "w");
        Assert.Null(await store.TryAcquireSlotAsync(2, "w", lease));   // index 2 exists but is above the cap
    }

    /// <summary>Release is FENCED by worker id, so a worker whose slot was already reclaimed cannot free
    /// the slot its successor now holds — the same fencing every other write on this store carries.</summary>
    public static async Task Releasing_a_slot_is_fenced_by_worker_id(IJobStore store, MutableClock clock)
    {
        var lease = TimeSpan.FromMinutes(5);
        var slot = await store.TryAcquireSlotAsync(1, "w1", lease);
        Assert.NotNull(slot);

        await store.ReleaseSlotAsync(slot!.Value, "someone-else");

        Assert.Null(await store.TryAcquireSlotAsync(1, "w2", lease));   // still held by w1
    }

    /// <summary>A cap of zero or less hands out nothing rather than throwing or handing out slot 0 — the
    /// boundary a caller reaches by computing the cap from configuration.</summary>
    public static async Task A_non_positive_cap_hands_out_no_slot(IJobStore store, MutableClock clock)
    {
        Assert.Null(await store.TryAcquireSlotAsync(0, "w1", TimeSpan.FromMinutes(5)));
        Assert.Null(await store.TryAcquireSlotAsync(-1, "w1", TimeSpan.FromMinutes(5)));
    }

    /// <summary>A non-positive limit asks for nothing, on every backend.
    /// <para>Left unguarded the three disagreed, and one of them dangerously: <c>.Take(limit)</c> gave an
    /// empty list, SQLite reads a NEGATIVE <c>LIMIT</c> as no limit at all and returned the whole matching
    /// table, and Postgres threw. Reachable through the public front door, whose <c>ListAsync</c> /
    /// <c>ListDeadAsync</c> document no bound — an admin page computing <c>pageSize - offset</c> gets there.</para>
    /// <para>This is the same guard the memory-graph reads already carry; the job stores were simply outside
    /// the convention.</para></summary>
    public static async Task A_non_positive_list_limit_returns_nothing(IJobStore store, MutableClock clock)
    {
        var lane = "lim-" + Guid.NewGuid().ToString("N");   // self-isolating: the container is shared
        await store.EnqueueAsync(Spec(lane));
        await store.EnqueueAsync(Spec(lane));

        Assert.NotEmpty(await store.ListAsync(lane: lane, limit: 10));   // sanity: the rows are really there
        Assert.Empty(await store.ListAsync(lane: lane, limit: 0));
        Assert.Empty(await store.ListAsync(lane: lane, limit: -1));
    }
}
