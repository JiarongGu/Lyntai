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
    public async Task Retry_requeues_then_fails_after_max_attempts()
    {
        var handler = new FakeJobHandler("flaky", _ => Task.FromResult(JobOutcome.Retry()));
        var (runner, store, queue, clock) = Build(o => o.Jobs.DefaultMaxAttempts = 2, handler);
        var id = await queue.EnqueueAsync("default", "flaky", "{}");

        await runner.RunOnceAsync();                                  // attempt 1 → Retry → requeued
        Assert.Equal(JobStatus.Pending, (await store.GetAsync(id))!.Status);

        clock.Advance(TimeSpan.FromMinutes(2));                       // past the backoff
        await runner.RunOnceAsync();                                  // attempt 2 → Retry but at max → Failed

        Assert.Equal(JobStatus.Failed, (await store.GetAsync(id))!.Status);
        Assert.Equal(2, handler.Calls);
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

        var runTask = runner.RunOnceAsync();
        Assert.True(await arrived.WaitAsync(TimeSpan.FromSeconds(5))); // lane a's job is in flight
        Assert.True(await arrived.WaitAsync(TimeSpan.FromSeconds(5))); // lane b's job in flight AT THE SAME TIME
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
