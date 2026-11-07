using System.Reflection;
using System.Runtime.ExceptionServices;
using Lyntai.Jobs;
using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Jobs;

/// <summary>What the runner does when its STORE misbehaves while a job runs: a read that fails once must not end
/// the cancel poll, a store's own timeout (an <see cref="OperationCanceledException"/> whose token is not the
/// caller's) must not end the poll or the slot heartbeat, and a handler's outcome must be written even though the
/// shutdown token is already cancelled.</summary>
public sealed class JobRunnerStoreFaultTests
{
    private static (JobRunner Runner, InMemoryJobStore Inner, Faulting.FaultSet Faults, JobQueue Queue) Build(
        Action<LyntaiOptions> tune, IJobHandler handler, Func<IJobStore, IJobStore>? wrap = null)
    {
        var clock = new MutableClock();
        var inner = new InMemoryJobStore(clock.Get);
        var store = Faulting.Over<IJobStore>(inner, out var faults);
        var options = new LyntaiOptions();
        tune(options);
        var runner = new JobRunner(wrap?.Invoke(store) ?? store, new JobHandlerRegistry([handler]), options, clock: clock.Get);
        return (runner, inner, faults, new JobQueue(inner, options));
    }

    private sealed class BlockUntilCancelled(TaskCompletionSource entered) : IJobHandler
    {
        public string Type => "block";

        public async Task<JobOutcome> HandleAsync(JobContext ctx, CancellationToken ct)
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return JobOutcome.Complete;
        }
    }

    [Fact]
    public async Task One_failed_cancel_poll_does_not_end_cancellation_for_the_rest_of_the_job()
    {
        var entered = new TaskCompletionSource();
        var (runner, inner, faults, queue) = Build(o => o.Jobs.PollInterval = TimeSpan.FromMilliseconds(20), new BlockUntilCancelled(entered));
        var id = await queue.EnqueueAsync("default", "block", "{}");

        faults.Add(nameof(IJobStore.GetAsync));
        var run = runner.RunOnceAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await WaitUntilAsync(() => faults.Calls(nameof(IJobStore.GetAsync)) >= 2); // the poll has failed, more than once
        faults.Clear();

        Assert.True(await queue.CancelAsync(id));
        await run.WaitAsync(TimeSpan.FromSeconds(30)); // hangs while the poll is dead

        Assert.Equal(JobStatus.Cancelled, (await inner.GetAsync(id))!.Status);
    }

    [Fact]
    public async Task A_store_timeout_does_not_end_the_cancel_poll()
    {
        var entered = new TaskCompletionSource();
        var (runner, inner, faults, queue) = Build(o => o.Jobs.PollInterval = TimeSpan.FromMilliseconds(20), new BlockUntilCancelled(entered));
        var id = await queue.EnqueueAsync("default", "block", "{}");

        faults.Add(nameof(IJobStore.GetAsync), () => new TaskCanceledException("the store's own timeout"));
        var run = runner.RunOnceAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await WaitUntilAsync(() => faults.Calls(nameof(IJobStore.GetAsync)) >= 2);
        faults.Clear();

        Assert.True(await queue.CancelAsync(id));
        await run.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(JobStatus.Cancelled, (await inner.GetAsync(id))!.Status);
    }

    [Fact]
    public async Task A_store_timeout_does_not_end_the_slot_heartbeat()
    {
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var handler = new FakeJobHandler("hold", async _ =>
        {
            entered.TrySetResult();
            await release.Task;
            return JobOutcome.Complete;
        });
        var (runner, _, faults, queue) = Build(o =>
        {
            o.Jobs.GlobalMaxConcurrency = 1;
            o.Jobs.SlotLease = TimeSpan.FromMilliseconds(300); // a beat every 100 ms
        }, handler);
        await queue.EnqueueAsync("default", "hold", "{}");

        faults.Add(nameof(IJobStore.HeartbeatSlotsAsync), () => new TaskCanceledException("the store's own timeout"));
        var run = runner.RunOnceAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await WaitUntilAsync(() => faults.Calls(nameof(IJobStore.HeartbeatSlotsAsync)) >= 3); // one beat, then none, without the fix
        release.SetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task A_handler_that_finished_during_a_shutdown_has_its_outcome_written()
    {
        using var shutdown = new CancellationTokenSource();
        var handler = new FakeJobHandler("t", _ =>
        {
            shutdown.Cancel(); // the cooperative stop: the handler checkpoints and returns while the pass is stopping
            return Task.FromResult(JobOutcome.Complete);
        });
        var (runner, inner, _, queue) = Build(_ => { }, handler, store => CancellationAware.Over(store));
        var id = await queue.EnqueueAsync("default", "t", "{}");

        await runner.RunOnceAsync(shutdown.Token);

        Assert.Equal(JobStatus.Succeeded, (await inner.GetAsync(id))!.Status); // Running, redelivered after its lease, today
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("the condition never held");
            await Task.Delay(10);
        }
    }

    /// <summary>A store that honours a cancelled token as a real database client does: a call handed a cancelled
    /// <see cref="CancellationToken"/> throws before it writes.</summary>
    private class CancellationAware : DispatchProxy
    {
        private object _inner = null!;

        public static T Over<T>(T inner) where T : class
        {
            var proxy = Create<T, CancellationAware>();
            ((CancellationAware)(object)proxy)._inner = inner;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            if (args?.OfType<CancellationToken>().Any(t => t.IsCancellationRequested) == true)
                throw new OperationCanceledException();
            try { return targetMethod.Invoke(_inner, args); }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                ExceptionDispatchInfo.Throw(ex.InnerException);
                throw; // unreachable
            }
        }
    }
}
