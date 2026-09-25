using System.Reflection;
using Lyntai.Jobs;
using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Jobs;

/// <summary>The EMPTY-LANE release: a slot is taken before the claim, so a lane with nothing claimable hands
/// it straight back — and that release must survive a transient store fault and a shutdown landing on it,
/// because <see cref="IJobStore.HeartbeatSlotsAsync"/> renews every slot a worker holds and a stranded one
/// would shrink the global cap for the life of the process.</summary>
public sealed class JobRunnerSlotReleaseTests
{
    private static readonly TimeSpan SlotLease = TimeSpan.FromHours(1); // expiry must not be what frees it

    private static (JobRunner Runner, SlotFaultStore Faults, InMemoryJobStore Inner, JobQueue Queue, MutableClock Clock) Build()
    {
        var clock = new MutableClock();
        var inner = new InMemoryJobStore(clock.Get);
        var faults = SlotFaultStore.Over(inner);
        var options = new LyntaiOptions();
        options.Jobs.DefaultLaneConcurrency = 10;
        options.Jobs.GlobalMaxConcurrency = 1;
        options.Jobs.SlotLease = SlotLease;
        var handler = new FakeJobHandler("t", _ => Task.FromResult(JobOutcome.Complete));
        var runner = new JobRunner((IJobStore)faults, new JobHandlerRegistry([handler]), options, clock: clock.Get);
        return (runner, faults, inner, new JobQueue(inner, options), clock);
    }

    [Fact]
    public async Task A_failed_empty_lane_release_is_retried_rather_than_stranding_the_slot()
    {
        var (runner, faults, _, queue, clock) = Build();
        // an ACTIVE lane with nothing claimable yet: the pass takes the slot, finds no job, releases
        await queue.EnqueueAsync(new JobSpec("x", "t", "{}", AvailableAt: clock.Now.AddSeconds(1)));
        faults.ReleaseFaults = 1;

        Assert.Equal(0, await runner.RunOnceAsync());

        clock.Advance(TimeSpan.FromSeconds(2));
        // With a global cap of 1, this pass can claim only if the failed release was retried.
        Assert.Equal(1, await runner.RunOnceAsync());
    }

    [Fact]
    public async Task A_shutdown_landing_on_the_empty_lane_release_still_releases()
    {
        var (runner, faults, inner, queue, clock) = Build();
        await queue.EnqueueAsync(new JobSpec("x", "t", "{}", AvailableAt: clock.Now.AddSeconds(1)));
        using var shutdown = new CancellationTokenSource();
        faults.AfterEmptyClaim = shutdown.Cancel; // the cancel arrives between the empty claim and the release

        try { await runner.RunOnceAsync(shutdown.Token); }
        catch (OperationCanceledException) { /* the pass may end on the cancel; the slot is what matters */ }

        Assert.Equal(0, await inner.TryAcquireSlotAsync(1, "another-worker", SlotLease));
    }
}

/// <summary>Forwards every <see cref="IJobStore"/> call to an inner store, and can fault a slot release — a
/// transient error, or a cancelled token honoured the way a SQL store's driver honours it.</summary>
public class SlotFaultStore : DispatchProxy
{
    private IJobStore _inner = null!;

    /// <summary>How many further <see cref="IJobStore.ReleaseSlotAsync"/> calls throw.</summary>
    public int ReleaseFaults { get; set; }

    /// <summary>Run after a claim that found nothing.</summary>
    public Action? AfterEmptyClaim { get; set; }

    public static SlotFaultStore Over(IJobStore inner)
    {
        var proxy = (SlotFaultStore)(object)Create<IJobStore, SlotFaultStore>();
        proxy._inner = inner;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        switch (method!.Name)
        {
            case nameof(IJobStore.ReleaseSlotAsync) when ReleaseFaults > 0:
                ReleaseFaults--;
                return Task.FromException(new InvalidOperationException("the store blipped releasing a slot"));
            case nameof(IJobStore.ReleaseSlotAsync) when args![2] is CancellationToken { IsCancellationRequested: true } ct:
                return Task.FromCanceled(ct);
            case nameof(IJobStore.ClaimNextAsync) when AfterEmptyClaim is { } after:
                return ClaimThenAsync((Task<JobRecord?>)method.Invoke(_inner, args)!, after);
            default:
                return method.Invoke(_inner, args);
        }
    }

    private static async Task<JobRecord?> ClaimThenAsync(Task<JobRecord?> claim, Action after)
    {
        var job = await claim.ConfigureAwait(false);
        if (job is null) after();
        return job;
    }
}
