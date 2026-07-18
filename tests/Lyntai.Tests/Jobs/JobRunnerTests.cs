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
}
