using Lyntai.Storage;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Jobs;

/// <summary>Every <see cref="JobStoreContract"/> method as a [Fact] — derive with a store+clock factory
/// and the whole contract runs on that backend automatically (T11: no silent skips). Postgres
/// deliberately does NOT derive: it runs a lane-namespaced subset on the shared container (the
/// table-wide contract methods would see other tests' rows there — see <c>PostgresStorageTests</c>).</summary>
public abstract class JobStoreContractFacts
{
    protected abstract (IJobStore Store, MutableClock Clock) New();

    private async Task Run(Func<IJobStore, MutableClock, Task> scenario)
    {
        var (store, clock) = New();
        await scenario(store, clock);
    }

    [Fact] public Task Claim_flips_running() => Run(JobStoreContract.Claim_flips_to_running_and_increments_attempts);
    [Fact] public Task Empty_lane_null() => Run(JobStoreContract.Empty_lane_claims_null);
    [Fact] public Task Two_claims_distinct() => Run(JobStoreContract.Two_claims_never_return_the_same_job);
    [Fact] public Task Complete_terminal() => Run(JobStoreContract.Complete_is_terminal);
    [Fact] public Task Fail_retry_requeues() => Run((s, c) => JobStoreContract.Fail_with_retry_requeues_available_later(s, c));
    [Fact] public Task Fail_terminal() => Run(JobStoreContract.Fail_without_retry_is_terminal);
    [Fact] public Task Checkpoint_renews_lease() => Run(JobStoreContract.Checkpoint_round_trips_and_renews_the_lease);
    [Fact] public Task Stale_reclaim() => Run(JobStoreContract.Stale_lease_is_reclaimed_with_the_checkpoint);
    [Fact] public Task Fenced_by_worker() => Run(JobStoreContract.Writes_are_fenced_by_worker_id);
    [Fact] public Task Poll_does_not_spend_an_attempt() => Run((s, c) => JobStoreContract.Poll_requeues_without_spending_an_attempt_and_is_fenced(s, c));
    [Fact] public Task Cancel_pending_not_running() => Run(JobStoreContract.Cancel_takes_a_pending_job_but_not_a_running_one);
    [Fact] public Task Enqueue_default_max_attempts() => Run(JobStoreContract.Enqueue_without_max_attempts_uses_the_shared_default);
    [Fact] public Task Active_lanes_and_count() => Run(JobStoreContract.Active_lanes_and_running_count);
    [Fact] public Task Priority_first() => Run(JobStoreContract.Higher_priority_is_claimed_first);
    [Fact] public Task Dead_letter() => Run(JobStoreContract.Dead_letter_is_terminal_inspectable_and_fenced);
    [Fact] public Task Replay_dead() => Run(JobStoreContract.Replay_requeues_a_dead_job);
    [Fact] public Task Request_cancel() => Run(JobStoreContract.Request_cancel_flags_a_running_job_then_cancel_running_finalizes);
    [Fact] public Task Tiebreak_by_id() => Run(JobStoreContract.Same_tick_same_priority_claims_in_id_order);
    [Fact] public Task Pause_resume() => Run(JobStoreContract.Pause_holds_a_pending_job_out_of_claims_then_resume_restores_it);
    [Fact] public Task Pause_pending_only() => Run(JobStoreContract.Pause_only_affects_a_pending_job);
    [Fact] public Task Cancel_reaches_paused() => Run(JobStoreContract.Cancel_reaches_a_paused_job_without_resuming_it);
    [Fact] public Task Progress_and_steps() => Run(JobStoreContract.Progress_and_steps_are_readable_while_running_and_fenced);
    [Fact] public Task Concurrent_steps() => Run(JobStoreContract.Concurrent_step_reports_all_land);
    [Fact] public Task Partition_serial_fifo() => Run((s, c) => JobStoreContract.Same_partition_serializes_and_is_fifo(s, c));
    [Fact] public Task Partitions_parallel() => Run((s, c) => JobStoreContract.Different_partitions_run_in_parallel(s, c));
    [Fact] public Task Partition_priority_ignored_within() => Run((s, c) => JobStoreContract.Priority_is_ignored_within_a_partition_but_honored_across(s, c));
    [Fact] public Task Partition_stale_reclaim_keeps_position() => Run((s, c) => JobStoreContract.Stale_partition_running_is_reclaimed_before_later_pending(s, c));
}
