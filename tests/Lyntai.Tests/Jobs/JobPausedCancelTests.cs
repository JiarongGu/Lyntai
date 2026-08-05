using Lyntai;
using Lyntai.Jobs;
using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Jobs;

/// <summary>
/// The FRONT-DOOR half of "a held job can be cancelled": <see cref="JobStoreContract"/> pins the store
/// transition on every backend it runs, this pins what a consumer calling <see cref="IJobQueue.CancelAsync"/>
/// observes — plus the two invariants that keep the widening from being over-applied (the Running half stays
/// a cooperative REQUEST, and the third backend cannot drift because the statement is shared), and the one
/// home the default attempt budget now has.
/// </summary>
public class JobPausedCancelTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);

    private static (IJobQueue Queue, IJobStore Store, MutableClock Clock) New()
    {
        var clock = new MutableClock();
        var store = new InMemoryJobStore(clock.Get);
        return (new JobQueue(store, new LyntaiOptions()), store, clock);
    }

    [Fact]
    public async Task Queue_cancel_takes_a_paused_job_in_one_call()
    {
        var (queue, store, _) = New();
        var id = await queue.EnqueueAsync("default", "t", "{}");
        Assert.True(await queue.PauseAsync(id));

        // before: BOTH halves missed a Paused job (Pending-only || Running-only) and this returned false
        Assert.True(await queue.CancelAsync(id));
        Assert.Equal(JobStatus.Cancelled, (await store.GetAsync(id))!.Status);
    }

    [Fact]
    public async Task The_resume_first_workaround_makes_the_job_claimable_in_the_gap()
    {
        // WHY the widening is a fix rather than an ergonomic shortcut: the only route to cancelling a held
        // job used to be Resume-then-Cancel, and a resumed job is Pending — i.e. back in the claimable set.
        // A runner polling that lane between the two calls takes it, and the cancel that follows degrades
        // from "this job never runs" to "please stop, if the handler honours its token".
        var (queue, store, _) = New();
        var id = await queue.EnqueueAsync("default", "t", "{}");
        await queue.PauseAsync(id);

        Assert.True(await store.ResumeAsync(id));
        Assert.Equal(id, (await store.ClaimNextAsync("default", "w1", Lease))!.Id); // the gap a runner wins

        Assert.True(await queue.CancelAsync(id)); // still "true" — but only the cooperative half fired
        var job = await store.GetAsync(id);
        Assert.Equal(JobStatus.Running, job!.Status);
        Assert.True(job.CancelRequested);
    }

    [Fact]
    public async Task Cancelling_a_paused_job_does_not_flag_it_for_a_worker_that_does_not_exist()
    {
        // the Running half stays narrow on purpose: cancel_requested is a message to the worker holding the
        // claim, and a held job has none. Widening BOTH halves would leave a Paused job flagged and still held.
        var (queue, store, _) = New();
        var id = await queue.EnqueueAsync("default", "t", "{}");
        await queue.PauseAsync(id);

        Assert.False(await store.RequestCancelAsync(id));
        Assert.True(await queue.CancelAsync(id));
        var job = await store.GetAsync(id);
        Assert.Equal(JobStatus.Cancelled, job!.Status);
        Assert.False(job.CancelRequested);
    }

    [Fact]
    public void The_shared_cancel_statement_covers_both_not_yet_started_states()
    {
        // SQLite and Postgres both route CancelAsync through this ONE statement, and the Postgres contract leg
        // only runs against a live container — so this is what keeps the third backend from diverging on a
        // machine with no container. RequestCancel is asserted too: it must NOT have been widened alongside.
        Assert.Contains("status IN ('Pending','Paused')", JobStoreSql.CancelPending, StringComparison.Ordinal);
        Assert.Contains("status='Running'", JobStoreSql.RequestCancel, StringComparison.Ordinal);
        Assert.DoesNotContain("Paused", JobStoreSql.RequestCancel, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_attempt_budget_has_one_home()
    {
        // JobOptions.DefaultMaxAttempts is the CONFIGURABLE queue-level default; JobSpec.DefaultMaxAttempts is
        // the constant it starts from and the fallback a store applies to a spec that reached it directly.
        // They were three hand-copied `3`s plus this property's initializer, free to drift apart in silence.
        Assert.Equal(JobSpec.DefaultMaxAttempts, new JobOptions().DefaultMaxAttempts);
    }

    [Fact]
    public async Task The_queue_still_fills_in_the_configured_budget_rather_than_the_constant()
    {
        // the const is the store's fallback, not an override: a caller who configured the option must still
        // get the option's value through the front door.
        var clock = new MutableClock();
        var store = new InMemoryJobStore(clock.Get);
        var options = new LyntaiOptions();
        options.Jobs.DefaultMaxAttempts = JobSpec.DefaultMaxAttempts + 4;

        var id = await new JobQueue(store, options).EnqueueAsync("default", "t", "{}");
        Assert.Equal(JobSpec.DefaultMaxAttempts + 4, (await store.GetAsync(id))!.MaxAttempts);
    }
}
