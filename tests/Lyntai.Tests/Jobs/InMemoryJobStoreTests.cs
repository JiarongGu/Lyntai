using Lyntai.Storage;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Jobs;

/// <summary>Runs the full <see cref="JobStoreContract"/> against <see cref="InMemoryJobStore"/>.</summary>
public class InMemoryJobStoreTests
{
    private static (IJobStore, MutableClock) New()
    {
        var clock = new MutableClock();
        return (new InMemoryJobStore(clock.Get), clock);
    }

    [Fact] public async Task Claim_flips_running() { var (s, c) = New(); await JobStoreContract.Claim_flips_to_running_and_increments_attempts(s, c); }
    [Fact] public async Task Empty_lane_null() { var (s, c) = New(); await JobStoreContract.Empty_lane_claims_null(s, c); }
    [Fact] public async Task Two_claims_distinct() { var (s, c) = New(); await JobStoreContract.Two_claims_never_return_the_same_job(s, c); }
    [Fact] public async Task Complete_terminal() { var (s, c) = New(); await JobStoreContract.Complete_is_terminal(s, c); }
    [Fact] public async Task Fail_retry_requeues() { var (s, c) = New(); await JobStoreContract.Fail_with_retry_requeues_available_later(s, c); }
    [Fact] public async Task Fail_terminal() { var (s, c) = New(); await JobStoreContract.Fail_without_retry_is_terminal(s, c); }
    [Fact] public async Task Checkpoint_renews_lease() { var (s, c) = New(); await JobStoreContract.Checkpoint_round_trips_and_renews_the_lease(s, c); }
    [Fact] public async Task Stale_reclaim() { var (s, c) = New(); await JobStoreContract.Stale_lease_is_reclaimed_with_the_checkpoint(s, c); }
    [Fact] public async Task Fenced_by_worker() { var (s, c) = New(); await JobStoreContract.Writes_are_fenced_by_worker_id(s, c); }
    [Fact] public async Task Cancel_pending_only() { var (s, c) = New(); await JobStoreContract.Cancel_only_affects_pending(s, c); }
    [Fact] public async Task Active_lanes_and_count() { var (s, c) = New(); await JobStoreContract.Active_lanes_and_running_count(s, c); }
    [Fact] public async Task Priority_first() { var (s, c) = New(); await JobStoreContract.Higher_priority_is_claimed_first(s, c); }
    [Fact] public async Task Dead_letter() { var (s, c) = New(); await JobStoreContract.Dead_letter_is_terminal_inspectable_and_fenced(s, c); }
    [Fact] public async Task Replay_dead() { var (s, c) = New(); await JobStoreContract.Replay_requeues_a_dead_job(s, c); }
}
