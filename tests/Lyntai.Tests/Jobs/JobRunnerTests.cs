using Lyntai;
using Lyntai.Jobs;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Jobs;

/// <summary>The runner over an InMemory store + fake handlers: dispatch, resume-from-checkpoint, retry to
/// max, lane concurrency, and lost-lease abandon — all deterministic via the injected clock.</summary>
public class JobRunnerTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);

    private static (JobRunner runner, InMemoryJobStore store, JobQueue queue, MutableClock clock) Build(
        Action<LyntaiOptions>? tune, params IJobHandler[] handlers)
    {
        var clock = new MutableClock();
        var store = new InMemoryJobStore(clock.Get);
        var options = new LyntaiOptions();
        options.Jobs.Lease = Lease;
        options.Jobs.RetryBackoff = TimeSpan.FromMinutes(1);
        tune?.Invoke(options);
        var runner = new JobRunner(store, new JobHandlerRegistry(handlers), options, clock: clock.Get);
        return (runner, store, new JobQueue(store, options), clock);
    }

    [Fact]
    public async Task Dispatches_to_the_handler_and_completes()
    {
        var handler = new FakeJobHandler("greet", _ => Task.FromResult(JobOutcome.Complete));
        var (runner, store, queue, _) = Build(null, handler);
        var id = await queue.EnqueueAsync("default", "greet", """{"name":"x"}""");

        var ran = await runner.RunOnceAsync();

        Assert.Equal(1, ran);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(JobStatus.Succeeded, (await store.GetAsync(id))!.Status);
    }

    [Fact]
    public async Task Queue_read_side_gets_one_job_and_lists_by_status_and_lane()
    {
        // the front door's read side — an app watches jobs through IJobQueue, never the storage-layer IJobStore
        var handler = new FakeJobHandler("greet", _ => Task.FromResult(JobOutcome.Complete));
        var (runner, _, queue, clock) = Build(null, handler);
        var done = await queue.EnqueueAsync("lane-a", "greet", "{}");
        var pending = await queue.EnqueueAsync(new JobSpec("lane-b", "greet", "{}",
            AvailableAt: clock.Get() + TimeSpan.FromHours(1))); // stays Pending this pass

        Assert.Equal(1, await runner.RunOnceAsync()); // only lane-a's job is runnable

        var record = await queue.GetAsync(done);
        Assert.NotNull(record);
        Assert.Equal(JobStatus.Succeeded, record.Status);
        Assert.Null(await queue.GetAsync(Guid.NewGuid())); // unknown id → null, not a throw

        Assert.Equal([pending], (await queue.ListAsync(JobStatus.Pending)).Select(j => j.Id));
        Assert.Equal([done], (await queue.ListAsync(lane: "lane-a")).Select(j => j.Id));
        Assert.Equal(2, (await queue.ListAsync()).Count);
        Assert.Empty(await queue.ListAsync(JobStatus.Dead));
    }

    [Fact]
    public async Task Unknown_type_fails_the_job()
    {
        var (runner, store, queue, _) = Build(null); // no handlers
        var id = await queue.EnqueueAsync("default", "nope", "{}");

        await runner.RunOnceAsync();

        Assert.Equal(JobStatus.Failed, (await store.GetAsync(id))!.Status);
    }

    [Fact]
    public async Task Resumes_a_crashed_job_from_its_checkpoint()
    {
        // handler completes only when it sees the checkpoint left by the "crashed" first attempt
        var handler = new FakeJobHandler("resumable",
            ctx => Task.FromResult(ctx.Checkpoint == "step1" ? JobOutcome.Complete : JobOutcome.Fail("no checkpoint")));
        var (runner, store, queue, clock) = Build(null, handler);
        var id = await queue.EnqueueAsync("default", "resumable", "{}");

        // simulate a crashed first attempt: a (now-dead) worker claimed it and checkpointed, never finished
        await store.ClaimNextAsync("default", "dead", Lease);
        await store.SaveCheckpointAsync(id, "dead", "step1");
        clock.Advance(Lease + TimeSpan.FromSeconds(1)); // lease lapses → reclaimable

        var ran = await runner.RunOnceAsync();

        Assert.Equal(1, ran);
        Assert.Equal(JobStatus.Succeeded, (await store.GetAsync(id))!.Status); // resumed + finished
    }

    [Fact]
    public async Task Retry_requeues_then_dead_letters_after_max_attempts_and_replays()
    {
        var handler = new FakeJobHandler("flaky", _ => Task.FromResult(JobOutcome.Retry()));
        var (runner, store, queue, clock) = Build(o => o.Jobs.DefaultMaxAttempts = 2, handler);
        var id = await queue.EnqueueAsync("default", "flaky", "{}");

        await runner.RunOnceAsync();                                  // attempt 1 → Retry → requeued
        Assert.Equal(JobStatus.Pending, (await store.GetAsync(id))!.Status);

        clock.Advance(TimeSpan.FromMinutes(2));                       // past the backoff
        await runner.RunOnceAsync();                                  // attempt 2 → Retry but at max → dead-lettered

        Assert.Equal(JobStatus.Dead, (await store.GetAsync(id))!.Status);
        Assert.Equal(2, handler.Calls);
        Assert.Contains(await queue.ListDeadAsync(), j => j.Id == id); // inspectable in the DLQ

        // replay it → runnable again (attempts reset); this run completes it
        handler.Result = _ => Task.FromResult(JobOutcome.Complete);
        Assert.True(await queue.ReplayAsync(id));
        await runner.RunOnceAsync();
        Assert.Equal(JobStatus.Succeeded, (await store.GetAsync(id))!.Status);
    }

    [Fact]
    public async Task Poll_requeues_without_spending_an_attempt_so_a_long_operation_is_never_dead_lettered()
    {
        // Found 2026-08-14 by the whole-codebase review. GenerationRenderJobHandler expressed "the render is
        // still running, come back later" as JobOutcome.Retry — and a retry SPENDS an attempt, so at the
        // default MaxAttempts of 3 the third poll dead-lettered the job. At a 15s poll delay that meant any
        // render over ~30 seconds died, against a class doc promising to "poll to completion across as many
        // process lifetimes as it takes", and the operator was told "retries exhausted" while a PAID render
        // carried on unwatched. Polling and retrying are different things; Retry keeps its meaning (a failed
        // attempt worth repeating) and Poll is the word that was missing.
        var polls = 0;
        var handler = new FakeJobHandler("render",
            _ => Task.FromResult(++polls < 5 ? JobOutcome.Poll(TimeSpan.FromSeconds(1)) : JobOutcome.Complete));
        var (runner, store, queue, clock) = Build(o => o.Jobs.DefaultMaxAttempts = 2, handler);
        var id = await queue.EnqueueAsync("default", "render", "{}");

        for (var i = 0; i < 5; i++)
        {
            await runner.RunOnceAsync();
            clock.Advance(TimeSpan.FromMinutes(2));       // past the poll delay
        }

        var final = (await store.GetAsync(id))!;
        Assert.Equal(JobStatus.Succeeded, final.Status);
        Assert.Equal(5, handler.Calls);                   // four polls then the completing run
        Assert.True(final.Attempts <= 2,
            $"polling must not accumulate attempts — MaxAttempts is 2 and the job ran 5 times, saw {final.Attempts}");
    }

    [Fact]
    public async Task A_poll_outcome_still_dead_letters_a_job_whose_lease_was_lost()
    {
        // Poll un-counts the claim's attempt increment, so it must stay FENCED on the worker id exactly like
        // every other terminal transition — otherwise a worker whose lease was reclaimed could drive another
        // worker's job backwards forever, which is the one way an un-counted outcome could become unbounded.
        var handler = new FakeJobHandler("render", _ => Task.FromResult(JobOutcome.Poll(TimeSpan.FromSeconds(1))));
        var (runner, store, queue, _) = Build(o => o.Jobs.DefaultMaxAttempts = 3, handler);
        var id = await queue.EnqueueAsync("default", "render", "{}");

        await runner.RunOnceAsync();
        var afterPoll = (await store.GetAsync(id))!;
        Assert.Equal(JobStatus.Pending, afterPoll.Status);

        // a DIFFERENT worker's poll must not land on this record
        Assert.False(await store.PollAgainAsync(id, "someone-else", DateTimeOffset.UtcNow.AddMinutes(5)));
    }

    [Fact]
    public async Task A_job_past_max_attempts_is_dead_lettered_without_running()
    {
        // simulates a poison pill that CRASHES the worker every run (the handler never returns/throws, so
        // ApplyAsync's bound never fires) — the claim-time bound must dead-letter it without invoking it.
        var handler = new FakeJobHandler("poison", _ => throw new InvalidOperationException("boom"));
        var (runner, store, queue, clock) = Build(o => o.Jobs.DefaultMaxAttempts = 2, handler);
        var id = await queue.EnqueueAsync("default", "poison", "{}");

        // two "crashes": a dead worker claims (attempts++) but never runs the handler; its lease lapses
        for (var i = 0; i < 2; i++)
        {
            await store.ClaimNextAsync("default", "dead", TimeSpan.FromMinutes(1));
            clock.Advance(TimeSpan.FromMinutes(2)); // lease lapses → reclaimable
        }
        Assert.Equal(0, handler.Calls); // never actually ran

        await runner.RunOnceAsync(); // reclaims (attempts → 3 > 2) → dead-letter at the top, no run

        Assert.Equal(JobStatus.Dead, (await store.GetAsync(id))!.Status);
        Assert.Equal(0, handler.Calls); // the handler was NOT invoked
    }

    [Fact]
    public async Task Cancel_request_stops_a_running_job()
    {
        var entered = new TaskCompletionSource();
        var handler = new BlockingHandler(entered);
        // small poll interval so the runner observes the cancel request quickly
        var (runner, store, queue, _) = Build(o => o.Jobs.PollInterval = TimeSpan.FromMilliseconds(20), handler);
        var id = await queue.EnqueueAsync("default", "block", "{}");

        var runTask = runner.RunOnceAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30)); // the job is running + blocked on its ct (throws on timeout)

        Assert.True(await queue.CancelAsync(id));                            // request cancellation of the running job
        await runTask.WaitAsync(TimeSpan.FromSeconds(30));                   // poll cancels it, handler stops, runner finalizes

        Assert.Equal(JobStatus.Cancelled, (await store.GetAsync(id))!.Status);
    }

    /// <summary>A handler that signals it started, then blocks until its cancellation token fires.</summary>
    private sealed class BlockingHandler(TaskCompletionSource entered) : IJobHandler
    {
        public string Type => "block";
        public async Task<JobOutcome> HandleAsync(JobContext ctx, CancellationToken ct)
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false); // honors cancellation
            return JobOutcome.Complete;
        }
    }

    [Fact]
    public async Task Higher_priority_jobs_run_before_lower_within_a_lane()
    {
        var handler = new FakeJobHandler("t", _ => Task.FromResult(JobOutcome.Complete));
        var (runner, store, queue, _) = Build(o => o.Jobs.LaneConcurrency["p"] = 1, handler); // one at a time
        var low = await queue.EnqueueAsync("p", "t", "{}", priority: 1);
        var high = await queue.EnqueueAsync("p", "t", "{}", priority: 9);

        await runner.RunOnceAsync(); // claims exactly one — must be the high-priority job

        Assert.Equal(JobStatus.Succeeded, (await store.GetAsync(high))!.Status);
        Assert.Equal(JobStatus.Pending, (await store.GetAsync(low))!.Status);
    }

    [Fact]
    public async Task A_thrown_handler_is_a_transient_retry()
    {
        var handler = new FakeJobHandler("boom", _ => throw new InvalidOperationException("kaboom"));
        var (runner, store, queue, _) = Build(o => o.Jobs.DefaultMaxAttempts = 2, handler);
        var id = await queue.EnqueueAsync("default", "boom", "{}");

        await runner.RunOnceAsync();

        var job = await store.GetAsync(id);
        Assert.Equal(JobStatus.Pending, job!.Status);                 // requeued, not dead
        Assert.Equal("kaboom", job.LastError);
    }

    [Fact]
    public async Task Lane_concurrency_bounds_the_batch_per_pass()
    {
        var handler = new FakeJobHandler("t", _ => Task.FromResult(JobOutcome.Complete));
        var (runner, _, queue, _) = Build(o => o.Jobs.LaneConcurrency["slow"] = 1, handler);
        await queue.EnqueueAsync("slow", "t", "{}");
        await queue.EnqueueAsync("slow", "t", "{}");

        Assert.Equal(1, await runner.RunOnceAsync()); // limit 1 → one per pass even with two available
        Assert.Equal(1, await runner.RunOnceAsync()); // the second
        Assert.Equal(0, await runner.RunOnceAsync()); // drained
    }

    [Fact]
    public async Task Runs_jobs_from_different_lanes_in_parallel()
    {
        // each handler signals it entered, then blocks; if the two lanes' jobs weren't truly concurrent,
        // the SECOND WaitAsync would hang (the 2nd job wouldn't start until the 1st — which is blocked —
        // finished). Passing proves cross-lane parallelism despite each lane's limit being 1.
        var arrived = new SemaphoreSlim(0);
        var release = new TaskCompletionSource();
        var handler = new FakeJobHandler("t", async _ => { arrived.Release(); await release.Task; return JobOutcome.Complete; });
        var (runner, _, queue, _) = Build(o => { o.Jobs.LaneConcurrency["a"] = 1; o.Jobs.LaneConcurrency["b"] = 1; }, handler);
        await queue.EnqueueAsync("a", "t", "{}");
        await queue.EnqueueAsync("b", "t", "{}");

        // generous backstop: a correct runner enters both handlers near-instantly, but under a saturated
        // thread pool (the whole suite runs in parallel) the 2nd handler task can be slow to SCHEDULE — a
        // tight timeout there is a false negative, not a real failure. Big enough to never flake, small
        // enough to still fail fast if cross-lane concurrency is genuinely broken (then it never releases).
        var runTask = runner.RunOnceAsync();
        Assert.True(await arrived.WaitAsync(TimeSpan.FromSeconds(30)), "lane a's job did not start"); // in flight
        Assert.True(await arrived.WaitAsync(TimeSpan.FromSeconds(30)), "lane b's job did not start concurrently");
        release.SetResult();

        Assert.Equal(2, await runTask);
    }

    [Fact]
    public async Task Global_max_concurrency_caps_the_batch()
    {
        var handler = new FakeJobHandler("t", _ => Task.FromResult(JobOutcome.Complete));
        var (runner, store, queue, _) = Build(o => { o.Jobs.DefaultLaneConcurrency = 10; o.Jobs.MaxConcurrency = 2; }, handler);
        for (var i = 0; i < 5; i++) await queue.EnqueueAsync("x", "t", "{}");

        Assert.Equal(2, await runner.RunOnceAsync());                        // only 2 run this pass (global cap)
        Assert.Equal(3, (await store.ListAsync(JobStatus.Pending)).Count);   // the rest wait for the next pass
    }

    [Fact]
    public async Task Global_cap_is_shared_fairly_across_lanes_no_starvation()
    {
        var handler = new FakeJobHandler("t", _ => Task.FromResult(JobOutcome.Complete));
        var (runner, store, queue, _) = Build(o => { o.Jobs.DefaultLaneConcurrency = 10; o.Jobs.MaxConcurrency = 2; }, handler);
        for (var i = 0; i < 3; i++) { await queue.EnqueueAsync("a", "t", "{}"); await queue.EnqueueAsync("b", "t", "{}"); }

        // cap of 2 → round-robin takes ONE from 'a' and ONE from 'b' — not two from 'a' (which would starve 'b')
        Assert.Equal(2, await runner.RunOnceAsync());
        Assert.Equal(2, (await store.ListAsync(JobStatus.Pending, lane: "a")).Count);
        Assert.Equal(2, (await store.ListAsync(JobStatus.Pending, lane: "b")).Count);
    }

    // ---- the CROSS-PROCESS cap (3.0) -----------------------------------------------------------------
    //
    // MaxConcurrency bounds one runner; GlobalMaxConcurrency bounds every runner sharing the store. The
    // tests below use TWO runners over ONE store, because a single-runner test cannot tell the two apart —
    // which is exactly how a per-process cap could masquerade as a global one.

    private static (JobRunner A, JobRunner B, InMemoryJobStore Store, JobQueue Queue) TwoWorkers(
        Action<LyntaiOptions> tune, params IJobHandler[] handlers)
    {
        var clock = new MutableClock();
        var store = new InMemoryJobStore(clock.Get);   // ONE store — the shared "database"
        var options = new LyntaiOptions();
        options.Jobs.Lease = Lease;
        options.Jobs.RetryBackoff = TimeSpan.FromMinutes(1);
        tune(options);
        var registry = new JobHandlerRegistry(handlers);
        return (new JobRunner(store, registry, options, clock: clock.Get),
                new JobRunner(store, registry, options, clock: clock.Get),
                store, new JobQueue(store, options));
    }

    [Fact]
    public async Task Two_workers_share_ONE_global_cap_rather_than_one_each()
    {
        // THE POINT, and it has to be observed as SIMULTANEITY rather than throughput. A first draft
        // asserted "two passes run 2 jobs, not 4" and failed at 4 — correctly: each pass completes its jobs
        // and hands the slots back, so four jobs across two sequential passes never breaks a cap of 2. The
        // cap bounds how many run AT ONCE, so the handler blocks and the test counts what is in flight.
        var inFlight = 0;
        var peak = 0;
        var gate = new SemaphoreSlim(0);
        var release = new TaskCompletionSource();
        var handler = new FakeJobHandler("t", async _ =>
        {
            lock (release) { inFlight++; peak = Math.Max(peak, inFlight); }
            gate.Release();
            await release.Task;
            lock (release) inFlight--;
            return JobOutcome.Complete;
        });
        var (a, b, _, queue) = TwoWorkers(
            o => { o.Jobs.DefaultLaneConcurrency = 10; o.Jobs.MaxConcurrency = 2; o.Jobs.GlobalMaxConcurrency = 2; },
            handler);
        for (var i = 0; i < 6; i++) await queue.EnqueueAsync("x", "t", "{}");

        // Both workers poll the same store at the same time. Without a shared cap each would take its own
        // MaxConcurrency of 2 and four handlers would be inside the gate at once.
        var runA = a.RunOnceAsync();
        var runB = b.RunOnceAsync();
        Assert.True(await gate.WaitAsync(TimeSpan.FromSeconds(30)), "no job started");
        Assert.True(await gate.WaitAsync(TimeSpan.FromSeconds(30)), "a second job did not start");
        // A third entrant would have to arrive while the first two are blocked; give it a real chance to.
        Assert.False(await gate.WaitAsync(TimeSpan.FromSeconds(2)), "a THIRD job ran past the global cap of 2");
        release.SetResult();
        await Task.WhenAll(runA, runB);

        Assert.Equal(2, peak);
    }

    [Fact]
    public async Task A_released_slot_is_reusable_by_the_OTHER_worker_on_the_next_pass()
    {
        // A cap that never gives slots back is a deadlock wearing a limit's clothes. The first worker's
        // jobs complete, so its slots must be free for the second.
        var handler = new FakeJobHandler("t", _ => Task.FromResult(JobOutcome.Complete));
        var (a, b, store, queue) = TwoWorkers(
            o => { o.Jobs.DefaultLaneConcurrency = 10; o.Jobs.GlobalMaxConcurrency = 2; }, handler);
        for (var i = 0; i < 4; i++) await queue.EnqueueAsync("x", "t", "{}");

        Assert.Equal(2, await a.RunOnceAsync());
        Assert.Equal(2, await b.RunOnceAsync());   // a's slots came back
        Assert.Empty(await store.ListAsync(JobStatus.Pending));
    }

    [Fact]
    public async Task An_empty_lane_gives_its_speculatively_taken_slot_straight_back()
    {
        // The slot is taken BEFORE the claim, so a drained lane must not strand one — otherwise a polling
        // runner with no work would exhaust the deployment's slots and throttle the workers that do.
        var handler = new FakeJobHandler("t", _ => Task.FromResult(JobOutcome.Complete));
        var (a, b, _, queue) = TwoWorkers(
            o => { o.Jobs.DefaultLaneConcurrency = 10; o.Jobs.GlobalMaxConcurrency = 1; }, handler);

        Assert.Equal(0, await a.RunOnceAsync());   // nothing enqueued: acquires a slot, finds no job, releases

        await queue.EnqueueAsync("x", "t", "{}");
        Assert.Equal(1, await b.RunOnceAsync());   // the slot was NOT stranded by a's empty pass
    }

    [Fact]
    public async Task Zero_means_unbounded_and_costs_no_slot_round_trip()
    {
        // The default, and the pre-3.0 behaviour: two workers with no global cap run everything their own
        // per-process caps allow.
        var handler = new FakeJobHandler("t", _ => Task.FromResult(JobOutcome.Complete));
        var (a, b, store, queue) = TwoWorkers(
            o => { o.Jobs.DefaultLaneConcurrency = 10; o.Jobs.MaxConcurrency = 2; }, handler);   // Global = 0
        for (var i = 0; i < 4; i++) await queue.EnqueueAsync("x", "t", "{}");

        Assert.Equal(4, await a.RunOnceAsync() + await b.RunOnceAsync());
        Assert.Empty(await store.ListAsync(JobStatus.Pending));
    }

    [Fact]
    public async Task A_crashed_workers_slot_is_reclaimed_after_the_slot_lease()
    {
        // No release ever arrives from a process that died, so expiry is what stops a crash throttling the
        // deployment. SlotLease can be SHORT because a live worker heartbeats — see the test below.
        var store = new InMemoryJobStore(() => DateTimeOffset.UnixEpoch);
        Assert.Equal(0, await store.TryAcquireSlotAsync(1, "dead-worker", Lease));
        Assert.Null(await store.TryAcquireSlotAsync(1, "live-worker", Lease));   // cap of 1, and it is held

        var later = new InMemoryJobStore(() => DateTimeOffset.UnixEpoch);
        Assert.Equal(0, await later.TryAcquireSlotAsync(1, "dead-worker", TimeSpan.Zero));
        Assert.Equal(0, await later.TryAcquireSlotAsync(1, "live-worker", TimeSpan.Zero)); // expired → retaken
    }

    [Fact]
    public async Task A_heartbeat_keeps_a_LONG_job_holding_its_slot_past_the_lease()
    {
        // THE PROPERTY THE HEARTBEAT BUYS. A single expiry cannot serve both questions: long enough for a
        // job that runs for hours and a crashed worker throttles the fleet for hours; short enough to
        // recover quickly and that long job loses its slot while still running. Renewal separates them —
        // the lease measures only "how long since we last heard from you".
        var clock = new MutableClock();
        var store = new InMemoryJobStore(clock.Get);
        var slotLease = TimeSpan.FromSeconds(30);

        Assert.Equal(0, await store.TryAcquireSlotAsync(1, "long-runner", slotLease));

        // well past the lease, but the worker has been beating throughout
        for (var beat = 0; beat < 10; beat++)
        {
            clock.Advance(TimeSpan.FromSeconds(10));
            await store.HeartbeatSlotsAsync("long-runner");
        }

        Assert.Null(await store.TryAcquireSlotAsync(1, "someone-else", slotLease)); // still held, 100s in

        clock.Advance(slotLease + TimeSpan.FromSeconds(1));                          // beats stop: it died
        Assert.Equal(0, await store.TryAcquireSlotAsync(1, "someone-else", slotLease));
    }

    [Fact]
    public async Task A_heartbeat_does_NOT_revive_a_slot_already_reclaimed_from_this_worker()
    {
        // A stalled worker that wakes up and beats must not steal back a slot its successor now holds —
        // the same fencing the release has, for the same reason.
        var clock = new MutableClock();
        var store = new InMemoryJobStore(clock.Get);
        var slotLease = TimeSpan.FromSeconds(30);
        Assert.Equal(0, await store.TryAcquireSlotAsync(1, "stalled", slotLease));

        clock.Advance(slotLease + TimeSpan.FromSeconds(1));
        Assert.Equal(0, await store.TryAcquireSlotAsync(1, "successor", slotLease));   // taken over

        await store.HeartbeatSlotsAsync("stalled");                                    // too late

        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Null(await store.TryAcquireSlotAsync(1, "third", slotLease));           // successor still holds it
    }

    [Fact]
    public async Task Releasing_is_FENCED_so_an_expired_worker_cannot_free_its_successors_slot()
    {
        var store = new InMemoryJobStore(() => DateTimeOffset.UnixEpoch);
        Assert.Equal(0, await store.TryAcquireSlotAsync(1, "first", TimeSpan.Zero));
        Assert.Equal(0, await store.TryAcquireSlotAsync(1, "second", TimeSpan.Zero));   // took it over

        await store.ReleaseSlotAsync(0, "first");   // the evicted worker finishes and tidies up

        Assert.Null(await store.TryAcquireSlotAsync(1, "third", Lease));   // 'second' still holds it
    }

    [Fact]
    public async Task The_cap_is_CONFIGURATION_so_lowering_it_needs_no_cleanup()
    {
        // Slots are created lazily and selected only below the cap, which is what lets a host retune without
        // a migration or a stranded row.
        var store = new InMemoryJobStore(() => DateTimeOffset.UnixEpoch);
        Assert.Equal(0, await store.TryAcquireSlotAsync(3, "w", Lease));
        Assert.Equal(1, await store.TryAcquireSlotAsync(3, "w", Lease));
        Assert.Equal(2, await store.TryAcquireSlotAsync(3, "w", Lease));

        await store.ReleaseSlotAsync(2, "w");
        Assert.Null(await store.TryAcquireSlotAsync(2, "w", Lease));   // index 2 exists but is above the cap
    }

    [Fact]
    public async Task A_lost_lease_outcome_is_abandoned_not_applied()
    {
        InMemoryJobStore? store = null;
        MutableClock? clock = null;
        // the handler gets its job stolen mid-run (lease lapses, another worker reclaims), then returns Complete
        var handler = new FakeJobHandler("t", async ctx =>
        {
            clock!.Advance(Lease + TimeSpan.FromSeconds(1));
            await store!.ClaimNextAsync("default", "other", Lease); // a different worker reclaims it
            return JobOutcome.Complete;
        });
        var built = Build(null, handler);
        (var runner, store, var queue, clock) = built;
        var id = await queue.EnqueueAsync("default", "t", "{}");

        await runner.RunOnceAsync();

        var job = await store.GetAsync(id);
        Assert.Equal(JobStatus.Running, job!.Status);   // the runner's Complete was fenced out
        Assert.Equal("other", job.ClaimedBy);           // the reclaimer owns it now — not marked Succeeded
    }

    /// <summary>An admission controller that holds a fixed set of lanes.</summary>
    private sealed class HoldLanes(params string[] held) : IJobAdmissionController
    {
        private readonly HashSet<string> _held = [.. held];
        public ValueTask<bool> CanClaimAsync(string lane, CancellationToken ct = default) => new(!_held.Contains(lane));
    }

    private sealed class ThrowingAdmission : IJobAdmissionController
    {
        public ValueTask<bool> CanClaimAsync(string lane, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task A_throwing_admission_controller_holds_the_lane_and_the_pump_survives()
    {
        var handler = new FakeJobHandler("t", _ => Task.FromResult(JobOutcome.Complete));
        var clock = new MutableClock();
        var store = new InMemoryJobStore(clock.Get);
        var options = new LyntaiOptions();
        options.Jobs.Lease = Lease;
        var runner = new JobRunner(store, new JobHandlerRegistry([handler]), options, clock: clock.Get,
            admission: new ThrowingAdmission());
        var queue = new JobQueue(store, options);
        var id = await queue.EnqueueAsync("default", "t", "{}");

        var ran = await runner.RunOnceAsync(); // a flaky controller must NOT crash the pump — it holds the lane

        Assert.Equal(0, ran);
        Assert.Equal(JobStatus.Pending, (await store.GetAsync(id))!.Status); // untouched, retried next pass
    }

    [Fact]
    public async Task Admission_controller_holds_a_lane_out_of_claims()
    {
        var handler = new FakeJobHandler("t", _ => Task.FromResult(JobOutcome.Complete));
        var clock = new MutableClock();
        var store = new InMemoryJobStore(clock.Get);
        var options = new LyntaiOptions();
        options.Jobs.Lease = Lease;
        var runner = new JobRunner(store, new JobHandlerRegistry([handler]), options, clock: clock.Get,
            admission: new HoldLanes("held"));
        var queue = new JobQueue(store, options);

        var heldJob = await queue.EnqueueAsync("held", "t", "{}");
        var openJob = await queue.EnqueueAsync("open", "t", "{}");

        var ran = await runner.RunOnceAsync();

        Assert.Equal(1, ran);                                                   // only the open lane ran
        Assert.Equal(JobStatus.Succeeded, (await store.GetAsync(openJob))!.Status);
        Assert.Equal(JobStatus.Pending, (await store.GetAsync(heldJob))!.Status); // held stays Pending (no state change)
    }

    [Fact]
    public async Task Handler_reported_progress_and_steps_are_persisted_and_readable()
    {
        var handler = new FakeJobHandler("t", async ctx =>
        {
            await ctx.ReportProgressAsync(1, 2, "warming-up");
            await ctx.ReportStepAsync("did the first thing");
            return JobOutcome.Complete;
        });
        var (runner, store, queue, _) = Build(null, handler);
        var id = await queue.EnqueueAsync("default", "t", "{}");

        await runner.RunOnceAsync();

        var job = await store.GetAsync(id);
        Assert.Equal(1, job!.Progress);
        Assert.Equal(2, job.Total);
        Assert.Equal("warming-up", job.Stage);
        Assert.Equal(["did the first thing"], JobStepLog.Parse(job.StepLog).Select(s => s.Message));
    }

    [Fact]
    public async Task A_resumed_job_sees_its_prior_steps_in_context()
    {
        var seenOnResume = new List<string>();
        var handler = new FakeJobHandler("t", async ctx =>
        {
            if (ctx.Checkpoint is null) // first attempt: report a step + checkpoint, then "crash" (retry)
            {
                await ctx.ReportStepAsync("attempt-1 work");
                await ctx.SaveCheckpointAsync("cp1");
                return JobOutcome.Retry();
            }
            seenOnResume.AddRange(ctx.Steps.Select(s => s.Message)); // resume: prior steps are visible
            return JobOutcome.Complete;
        });
        var (runner, store, queue, clock) = Build(null, handler);
        await queue.EnqueueAsync("default", "t", "{}");

        await runner.RunOnceAsync();                          // attempt 1 → Retry
        clock.Advance(TimeSpan.FromMinutes(2));               // let the retry become available
        await runner.RunOnceAsync();                          // attempt 2 → resume, Complete

        Assert.Equal(["attempt-1 work"], seenOnResume);
    }

    [Fact]
    public async Task A_paused_job_is_not_run()
    {
        var handler = new FakeJobHandler("t", _ => Task.FromResult(JobOutcome.Complete));
        var (runner, store, queue, _) = Build(null, handler);
        var id = await queue.EnqueueAsync("default", "t", "{}");
        Assert.True(await queue.PauseAsync(id));

        var ran = await runner.RunOnceAsync();

        Assert.Equal(0, ran);
        Assert.Equal(JobStatus.Paused, (await store.GetAsync(id))!.Status);

        Assert.True(await queue.ResumeAsync(id));
        Assert.Equal(1, await runner.RunOnceAsync());
        Assert.Equal(JobStatus.Succeeded, (await store.GetAsync(id))!.Status);
    }
}
