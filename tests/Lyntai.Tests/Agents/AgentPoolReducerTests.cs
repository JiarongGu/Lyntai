using System.Text.Json;
using Lyntai.Agents;

namespace Lyntai.Tests.Agents;

/// <summary>The restriction rules a host applies to a snapshot it stores itself, with no pool, no store and no
/// clock of the library's: each operation returns the next snapshot and an outcome, and a stale, foreign or
/// token-less report changes nothing. <c>AgentAccountPoolTests</c> holds the same rules through the pool.</summary>
public class AgentPoolReducerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly AgentPoolTiming Timing = new(TimeSpan.FromMinutes(10), null);

    private static AgentAccount Acct(string name) => new() { Provider = "claude-cli", Name = name, Home = "/homes/" + name };

    private static readonly string AccountA = AgentPoolReducer.AccountScope("claude-cli", "a");

    private static AgentRestriction Quota(RecoveryKind kind, DateTimeOffset? at = null) => new(
        RestrictionCause.Quota, RestrictionSubject.Account, new RestrictionRecovery(kind, at));

    private static AgentHoldEntry Only(AgentPoolSnapshot snapshot) => Assert.Single(snapshot.Holds.Values);

    // ── a hold's identity ────────────────────────────────────────────────────────────────────────

    [Fact] // a failure restates the hold of its scope and cause one revision up, and drops a release or an attempt
    public void A_failure_restates_its_hold_at_a_higher_revision()
    {
        var first = AgentPoolReducer.Observe(AgentPoolSnapshot.Empty, AccountA, Quota(RecoveryKind.ResetAt, T0 + Hour), T0);
        var cleared = AgentPoolReducer.Clear(first.Snapshot, first.Outcome.Id, 1, T0);
        var again = AgentPoolReducer.Observe(cleared.Snapshot, AccountA, Quota(RecoveryKind.ResetAt, T0 + 2 * Hour), T0);

        Assert.Equal(1, first.Outcome.Revision);
        Assert.True(cleared.Outcome);
        Assert.Equal(2, again.Outcome.Revision);
        Assert.Null(again.Outcome.ReleasedAt);
        Assert.Same(again.Outcome, Only(again.Snapshot));
    }

    [Fact] // a removed id's highest revision is kept, so a clear meant for the old hold matches nothing against the new one
    public void A_hold_recreated_after_its_removal_continues_from_its_revision()
    {
        var held = AgentPoolReducer.Observe(AgentPoolSnapshot.Empty, AccountA, Quota(RecoveryKind.ResetAt, T0 + Hour), T0);
        var restated = AgentPoolReducer.Observe(held.Snapshot, AccountA, Quota(RecoveryKind.ResetAt, T0 + Hour), T0);
        var gone = AgentPoolReducer.Discharge(restated.Snapshot, Evidence(restated.Outcome, T0 + Hour));
        Assert.True(gone.Outcome);
        Assert.Empty(gone.Snapshot.Holds);
        Assert.Equal(2, gone.Snapshot.RetiredRevisions[restated.Outcome.Id]);

        var recreated = AgentPoolReducer.Observe(gone.Snapshot, AccountA, Quota(RecoveryKind.ResetAt, T0 + 3 * Hour), T0 + Hour);
        var stale = AgentPoolReducer.Clear(recreated.Snapshot, recreated.Outcome.Id, 1, T0 + Hour);

        Assert.Equal(3, recreated.Outcome.Revision);
        Assert.False(stale.Outcome);
        Assert.Same(recreated.Snapshot, stale.Snapshot);
    }

    // ── evidence with no attempt ─────────────────────────────────────────────────────────────────

    private static AgentPoolEvidence Evidence(AgentHoldEntry hold, DateTimeOffset measuredAt, string? subject = null,
        long? revision = null, params RecoveryKind[] answers) =>
        new(hold.Id, revision ?? hold.Revision, subject ?? hold.Scope, measuredAt,
            answers.Length > 0 ? answers : [RecoveryKind.ResetAt, RecoveryKind.RetryNotBefore]);

    [Fact] // an observation names the hold exactly or discharges nothing
    public void A_stale_wrong_scope_and_other_subject_observation_discharge_nothing()
    {
        var held = AgentPoolReducer.Observe(AgentPoolSnapshot.Empty, AccountA, Quota(RecoveryKind.ResetAt, T0 + Hour), T0);
        var restated = AgentPoolReducer.Observe(held.Snapshot, AccountA, Quota(RecoveryKind.ResetAt, T0 + Hour), T0 + TimeSpan.FromMinutes(5));
        var hold = restated.Outcome;
        var later = T0 + TimeSpan.FromMinutes(30);

        // a reading taken against the first failure, which the second restated
        Assert.False(AgentPoolReducer.Discharge(restated.Snapshot, Evidence(hold, later, revision: 1)).Outcome);
        // the right account's key but the seat scope of the same account, and another account's key
        Assert.False(AgentPoolReducer.Discharge(restated.Snapshot,
            Evidence(hold, later, AgentPoolReducer.SeatScope("claude-cli", "a", null))).Outcome);
        Assert.False(AgentPoolReducer.Discharge(restated.Snapshot,
            Evidence(hold, later, AgentPoolReducer.AccountScope("claude-cli", "b"))).Outcome);
        // taken before the failure was, a hold that is not held, and a kind the evidence does not answer
        Assert.False(AgentPoolReducer.Discharge(restated.Snapshot, Evidence(hold, T0 + TimeSpan.FromMinutes(5))).Outcome);
        Assert.False(AgentPoolReducer.Discharge(restated.Snapshot, Evidence(hold, later) with { HoldId = "no-such" }).Outcome);
        Assert.False(AgentPoolReducer.Discharge(restated.Snapshot, Evidence(hold, later, answers: RecoveryKind.OwnerAction)).Outcome);

        var ok = AgentPoolReducer.Discharge(restated.Snapshot, Evidence(hold, later));
        Assert.True(ok.Outcome);
        Assert.Empty(ok.Snapshot.Holds);
    }

    [Fact] // which kinds evidence answers is the host's to say: an owner action is freed only by evidence that says it answers one
    public void An_owner_action_is_discharged_only_by_evidence_that_answers_one()
    {
        var held = AgentPoolReducer.Observe(AgentPoolSnapshot.Empty, AccountA,
            new AgentRestriction(RestrictionCause.Credit, RestrictionSubject.Account, RestrictionRecovery.OwnerActionFor(OwnerActionReason.Refill)), T0);

        Assert.False(AgentPoolReducer.Discharge(held.Snapshot, Evidence(held.Outcome, T0 + Hour)).Outcome);
        Assert.True(AgentPoolReducer.Discharge(held.Snapshot, Evidence(held.Outcome, T0 + Hour, answers: RecoveryKind.OwnerAction)).Outcome);
    }

    // ── a lease and the token that owns it ───────────────────────────────────────────────────────

    [Fact] // a released hold permits one attempt, leased to a token; a report by a token that lost the lease is ignored
    public void A_lease_report_by_a_token_that_lost_it_is_ignored()
    {
        var account = Acct("a");
        var covering = AgentPoolReducer.Covering(account, "claude-cli", null, null);
        var held = AgentPoolReducer.Observe(AgentPoolSnapshot.Empty, AccountA, Quota(RecoveryKind.ResetAt, T0 + Hour), T0);

        Assert.False(AgentPoolReducer.TakeSeat(held.Snapshot, covering, "x", T0, Timing).Outcome.Permitted);

        var afterReset = T0 + Hour;
        var first = AgentPoolReducer.TakeSeat(held.Snapshot, covering, "first", afterReset, Timing);
        Assert.True(first.Outcome.IsProbe);
        var lease = Assert.Single(first.Outcome.Leases);
        // the first attempt runs past its lease, and another takes the seat
        var runOut = afterReset + Timing.AttemptLease + TimeSpan.FromSeconds(1);
        var second = AgentPoolReducer.TakeSeat(first.Snapshot, covering, "second", runOut, Timing);
        Assert.True(second.Outcome.IsProbe);

        var lateSuccess = AgentPoolReducer.Succeed(second.Snapshot, [lease], "first");
        Assert.Empty(lateSuccess.Outcome);
        Assert.Equal("second", Only(lateSuccess.Snapshot).LeaseToken);

        // …and an abandon by it releases nothing of the second's
        var lateAbandon = AgentPoolReducer.Abandon(second.Snapshot, "first");
        Assert.Equal(0, lateAbandon.Outcome);
        Assert.Same(second.Snapshot, lateAbandon.Snapshot);

        var success = AgentPoolReducer.Succeed(second.Snapshot, second.Outcome.Leases, "second");
        Assert.Equal([lease.HoldId], success.Outcome);
        Assert.Empty(success.Snapshot.Holds);
    }

    [Fact] // the attempt's success removes only the hold whose revision it saw: a newer failure outlives it
    public void A_newer_failure_outlives_an_attempt_that_succeeds()
    {
        var covering = AgentPoolReducer.Covering(Acct("a"), "claude-cli", null, null);
        var held = AgentPoolReducer.Observe(AgentPoolSnapshot.Empty, AccountA, Quota(RecoveryKind.ResetAt, T0), T0);
        var reserved = AgentPoolReducer.TakeSeat(held.Snapshot, covering, "t", T0, Timing);
        var restated = AgentPoolReducer.Observe(reserved.Snapshot, AccountA, Quota(RecoveryKind.ResetAt, T0 + Hour), T0);

        var success = AgentPoolReducer.Succeed(restated.Snapshot, reserved.Outcome.Leases, "t");

        Assert.Empty(success.Outcome);
        Assert.Equal(2, Only(success.Snapshot).Revision);
    }

    [Fact] // a failed turn is observed as a hold where it restricts something, and as nothing where it does not
    public void A_failure_is_observed_as_a_hold_in_the_scope_that_covers_it()
    {
        var account = Acct("a");
        var limit = new AgentFailure(AgentFailureKind.Limit, "t");
        var fatal = new AgentFailure(AgentFailureKind.Fatal, "t");

        var seen = AgentPoolReducer.Observe(AgentPoolSnapshot.Empty, limit, account, "claude-cli", "opus", null, T0);
        var ignored = AgentPoolReducer.Observe(seen.Snapshot, fatal, account, "claude-cli", "opus", null, T0);

        Assert.Equal(AgentPoolReducer.SeatScope("claude-cli", "a", "opus"), seen.Outcome!.Scope);   // a bare limit holds the exact seat
        Assert.Null(ignored.Outcome);
        Assert.Same(seen.Snapshot, ignored.Snapshot);
    }

    // ── a host's own serialisation ───────────────────────────────────────────────────────────────

    [Fact] // the snapshot is plain data: the holds and the retired revisions round-trip through a host's own JSON
    public void A_snapshot_round_trips_through_a_hosts_own_serialisation()
    {
        var covering = AgentPoolReducer.Covering(Acct("a"), "claude-cli", "opus", "shared");
        var s = AgentPoolReducer.Observe(AgentPoolSnapshot.Empty, AccountA,
            new AgentRestriction(RestrictionCause.Quota, RestrictionSubject.Account,
                RestrictionRecovery.ResetAt(T0 + Hour) with { Stated = new StatedRecoveryParts(StatedTimeShape.TimeOfDay) { Time = new TimeOnly(13, 0), Zone = "UTC" } })
            { Window = "five_hour" }, T0).Snapshot;
        s = AgentPoolReducer.Observe(s, AgentPoolReducer.AllowanceScope("shared"),
            new AgentRestriction(RestrictionCause.Credit, RestrictionSubject.Pool, RestrictionRecovery.OwnerActionFor(OwnerActionReason.Refill)), T0).Snapshot;
        var reserved = AgentPoolReducer.TakeSeat(s, covering, "t", T0 + Hour, Timing);   // the account's hold is released; the allowance's still blocks
        Assert.False(reserved.Outcome.Permitted);
        var gone = AgentPoolReducer.Discharge(s, Evidence(s.Holds[AccountA + "|quota"], T0 + TimeSpan.FromMinutes(1))).Snapshot;

        var holdsJson = JsonSerializer.Serialize(gone.Holds.Values);
        var retiredJson = JsonSerializer.Serialize(gone.RetiredRevisions);
        var back = AgentPoolSnapshot.Create(
            JsonSerializer.Deserialize<List<AgentHoldEntry>>(holdsJson)!,
            JsonSerializer.Deserialize<Dictionary<string, long>>(retiredJson)!);

        Assert.Equal(gone.Holds.Values.OrderBy(h => h.Id), back.Holds.Values.OrderBy(h => h.Id));
        Assert.Equal(gone.RetiredRevisions.OrderBy(r => r.Key), back.RetiredRevisions.OrderBy(r => r.Key));
        Assert.Single(back.RetiredRevisions);

        // the restored state is in force: a hold recreated under the retired id continues from its revision
        var recreated = AgentPoolReducer.Observe(back, AccountA, Quota(RecoveryKind.ResetAt, T0 + Hour), T0 + Hour);
        Assert.Equal(2, recreated.Outcome.Revision);
    }

    [Fact] // a snapshot a host assembles is checked: an id twice or a revision below 1 is refused
    public void A_snapshot_refuses_an_id_listed_twice_or_a_revision_below_one()
    {
        var hold = new AgentHoldEntry("h", 1, AccountA, Quota(RecoveryKind.Unknown), T0, null, null, null);

        Assert.Throws<ArgumentException>(() => AgentPoolSnapshot.Create([hold, hold]));
        Assert.Throws<ArgumentException>(() => AgentPoolSnapshot.Create([hold with { Revision = 0 }]));
        Assert.Throws<ArgumentException>(() => AgentPoolSnapshot.Create([], new Dictionary<string, long> { ["h"] = 0 }));
        Assert.Same(hold, Only(AgentPoolSnapshot.Create([hold])));
    }
}
