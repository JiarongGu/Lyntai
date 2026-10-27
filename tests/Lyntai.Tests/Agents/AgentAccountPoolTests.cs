using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Agents;

/// <summary>The account pool and its restriction reducer under a clock that only moves when told to, over the
/// in-memory key-value store: the ordered walk, restrictions as a set, a released restriction permitting one bounded
/// attempt, an owner action cleared by id and revision, an unknown recovery, a stale reading, and a restart reading
/// the stored state back. Zero tokens, zero real waiting.</summary>
public class AgentAccountPoolTests
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    private static AgentAccount Acct(string name, string provider = "claude-cli") =>
        new() { Provider = provider, Name = name, Home = "/homes/" + name };

    private static AgentChoice Choice(string? model = null, string provider = "claude-cli", params string[] accounts) =>
        new() { Provider = provider, Model = model, Accounts = [.. accounts.Select(a => Acct(a, provider))] };

    private static AgentPoolOptions Options(Action<AgentPoolOptions>? configure = null, params AgentChoice[] choices)
    {
        var options = new AgentPoolOptions();
        options.Choices.AddRange(choices);
        configure?.Invoke(options);
        return options;
    }

    private static AgentAccountPool Pool(ManualTimeProvider clock, IKeyValueStore? store, AgentPoolOptions options,
        IAgentAccountPolicy? policy = null, IEnumerable<IAgentAllowanceSource>? allowances = null) =>
        new(options, store, clock, allowances, policy);

    private static AgentAccountPool Pool(ManualTimeProvider clock, params AgentChoice[] choices) =>
        Pool(clock, new InMemoryKeyValueStore(), Options(null, choices));

    private static SessionEnded Ok() => new(ProviderVerdict.Ok, false, "success", "s-1", "done", null);

    private static SessionEnded Failed(AgentFailureKind kind, AgentRestriction? restriction = null) =>
        new(ProviderVerdict.Failed, true, null, null, null, "boom")
        {
            Failure = new AgentFailure(kind, "test") { Restriction = restriction },
        };

    private static SessionEnded Limit(RestrictionRecovery recovery, RestrictionCause cause = RestrictionCause.Quota,
        RestrictionSubject subject = RestrictionSubject.Account) =>
        Failed(AgentFailureKind.Limit, new AgentRestriction(cause, subject, recovery));

    private static async Task<AgentPick> Take(AgentAccountPool pool)
    {
        var result = await pool.PickAsync();
        return result.Pick ?? throw new Xunit.Sdk.XunitException("no seat was permitted");
    }

    private static string Name(AgentPick pick) => pick.Seat.Account.Name;

    // ── the ordered walk ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_walk_takes_a_choices_accounts_before_the_next_choice()
    {
        var clock = new ManualTimeProvider();
        var pool = Pool(clock, Choice("opus", "claude-cli", "a", "b"), Choice("sonnet", "claude-cli", "c"));

        var first = await Take(pool);
        Assert.Equal(("a", "opus", false), (Name(first), first.Seat.Choice.Model, first.Seat.IsProbe));

        await pool.ReportAsync(first, Limit(RestrictionRecovery.OwnerAction));
        var second = await Take(pool);
        Assert.Equal(("b", "opus"), (Name(second), second.Seat.Choice.Model));

        await pool.ReportAsync(second, Limit(RestrictionRecovery.OwnerAction));
        var third = await Take(pool);
        Assert.Equal(("c", "sonnet"), (Name(third), third.Seat.Choice.Model));
    }

    [Fact]
    public async Task A_model_restriction_holds_one_model_on_the_account_and_the_walk_moves_to_the_next_choice()
    {
        var clock = new ManualTimeProvider();
        var pool = Pool(clock, Choice("opus", "claude-cli", "a"), Choice("sonnet", "claude-cli", "a"));

        var opus = await Take(pool);
        await pool.ReportAsync(opus, Limit(RestrictionRecovery.OwnerAction, RestrictionCause.Quota, RestrictionSubject.Model));

        var next = await Take(pool);
        Assert.Equal(("a", "sonnet"), (Name(next), next.Seat.Choice.Model));
    }

    [Fact]
    public async Task Rotation_lands_at_a_turns_end_never_inside_one()
    {
        var clock = new ManualTimeProvider();
        var pool = Pool(clock, Choice(null, "claude-cli", "a", "b"));

        var running = await Take(pool);
        var concurrent = await Take(pool);
        Assert.Equal(("a", "a"), (Name(running), Name(concurrent)));

        // a concurrent turn on 'a' meets its limit: the turn still running keeps the seat it was handed
        await pool.ReportAsync(concurrent, Limit(RestrictionRecovery.OwnerAction));
        Assert.Equal("a", running.Seat.Account.Name);
        Assert.Equal("a", running.Apply(new AgentSessionOptions { Prompt = "p" }).Account!.Name);

        // and the rotation lands on the next ask
        Assert.Equal("b", Name(await Take(pool)));
        await pool.ReportAsync(running, Ok());
        Assert.Equal("b", Name(await Take(pool)));
    }

    [Fact]
    public async Task Asking_twice_with_nothing_reported_gives_the_same_seat()
    {
        var pool = Pool(new ManualTimeProvider(), Choice(null, "claude-cli", "a", "b"));
        Assert.Equal(Name(await Take(pool)), Name(await Take(pool)));
    }

    [Fact]
    public async Task Apply_sets_the_account_and_the_choices_model_over_the_hosts_and_keeps_the_options_type()
    {
        var pool = Pool(new ManualTimeProvider(), Choice("opus", "claude-cli", "a"), Choice(null, "claude-cli", "b"));
        var pick = await Take(pool);

        var applied = pick.Apply(new Derived { Prompt = "p", Model = "host-model", Extra = "x" });

        Assert.IsType<Derived>(applied);
        Assert.Equal(("a", "opus", "x"), (applied.Account!.Name, applied.Model, ((Derived)applied).Extra));

        var noModel = new AgentPick(
            new AgentSeat(1, Choice(null, "claude-cli", "b"), Acct("b"), false), "t", []);
        Assert.Equal("host-model", noModel.Apply(new AgentSessionOptions { Prompt = "p", Model = "host-model" }).Model);
    }

    private sealed record Derived : AgentSessionOptions { public string? Extra { get; init; } }

    // ── a reset moment permits one bounded attempt ───────────────────────────────────────────────

    [Fact]
    public async Task A_reset_time_permits_one_attempt_and_no_second_until_the_first_is_reported()
    {
        var clock = new ManualTimeProvider();
        var pool = Pool(clock, Choice(null, "claude-cli", "a", "b"));
        var first = await Take(pool);
        await pool.ReportAsync(first, Limit(RestrictionRecovery.ResetAt(clock.GetUtcNow() + Hour)));

        Assert.Equal("b", Name(await Take(pool)));
        clock.Advance(Hour - TimeSpan.FromSeconds(1));
        Assert.Equal("b", Name(await Take(pool)));

        clock.Advance(TimeSpan.FromSeconds(1));
        var attempt = await Take(pool);
        Assert.Equal(("a", true), (Name(attempt), attempt.Seat.IsProbe));

        // one attempt at a time: a concurrent ask is turned to the next account
        var concurrent = await Take(pool);
        Assert.Equal(("b", false), (Name(concurrent), concurrent.Seat.IsProbe));

        await pool.ReportAsync(attempt, Ok());
        Assert.Empty(await pool.HoldsAsync());
        var after = await Take(pool);
        Assert.Equal(("a", false), (Name(after), after.Seat.IsProbe));
    }

    [Fact]
    public async Task A_failed_attempt_restates_the_hold_at_a_higher_revision_with_the_new_reset()
    {
        var clock = new ManualTimeProvider();
        var pool = Pool(clock, Choice(null, "claude-cli", "a"));
        await pool.ReportAsync(await Take(pool), Limit(RestrictionRecovery.ResetAt(clock.GetUtcNow() + Hour)));
        clock.Advance(Hour);

        var attempt = await Take(pool);
        var again = clock.GetUtcNow() + Hour;
        await pool.ReportAsync(attempt, Limit(RestrictionRecovery.ResetAt(again)));

        var hold = Assert.Single(await pool.HoldsAsync());
        Assert.Equal((2L, AgentHoldState.Blocked, again), (hold.Revision, hold.State, hold.ReadyAt));
        Assert.Null((await pool.PickAsync()).Pick);
    }

    [Fact]
    public async Task A_reset_time_is_not_evidence_of_credit_an_attempt_that_fails_without_a_restriction_leaves_it_released()
    {
        var clock = new ManualTimeProvider();
        var pool = Pool(clock, Choice(null, "claude-cli", "a"));
        await pool.ReportAsync(await Take(pool), Limit(RestrictionRecovery.ResetAt(clock.GetUtcNow() + Hour)));
        clock.Advance(Hour);

        var attempt = await Take(pool);
        await pool.ReportAsync(attempt, Failed(AgentFailureKind.Fatal));

        var hold = Assert.Single(await pool.HoldsAsync());
        Assert.Equal((1L, AgentHoldState.Ready), (hold.Revision, hold.State));
        Assert.True((await Take(pool)).Seat.IsProbe);
    }

    [Fact]
    public async Task A_lost_attempt_bars_the_seat_only_for_the_lease()
    {
        var clock = new ManualTimeProvider();
        var pool = Pool(clock, new InMemoryKeyValueStore(),
            Options(o => o.AttemptLease = TimeSpan.FromMinutes(10), Choice(null, "claude-cli", "a")));
        await pool.ReportAsync(await Take(pool), Limit(RestrictionRecovery.ResetAt(clock.GetUtcNow())));

        var lost = await Take(pool);
        Assert.True(lost.Seat.IsProbe);

        var held = await pool.PickAsync();
        Assert.Null(held.Pick);
        Assert.Equal(clock.GetUtcNow() + TimeSpan.FromMinutes(10), held.ReadyAt);

        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.True((await Take(pool)).Seat.IsProbe);
    }

    [Fact]
    public async Task An_abandoned_pick_gives_its_attempt_back()
    {
        var clock = new ManualTimeProvider();
        var pool = Pool(clock, Choice(null, "claude-cli", "a"));
        await pool.ReportAsync(await Take(pool), Limit(RestrictionRecovery.ResetAt(clock.GetUtcNow())));

        var attempt = await Take(pool);
        Assert.Null((await pool.PickAsync()).Pick);
        await pool.AbandonAsync(attempt);

        Assert.True((await Take(pool)).Seat.IsProbe);
    }

    [Fact]
    public async Task A_pick_is_given_back_once()
    {
        var pool = Pool(new ManualTimeProvider(), Choice(null, "claude-cli", "a"));
        var pick = await Take(pool);
        await pool.ReportAsync(pick, Ok());

        await Assert.ThrowsAsync<InvalidOperationException>(() => pool.ReportAsync(pick, Ok()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => pool.AbandonAsync(pick));
    }

    // ── restrictions are a set ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Independent_restrictions_on_one_account_must_all_permit()
    {
        var clock = new ManualTimeProvider();
        var pool = Pool(clock, Choice(null, "claude-cli", "a"));
        var start = clock.GetUtcNow();

        // two concurrent turns on the account meet two different limits
        var one = await Take(pool);
        var two = await Take(pool);
        await pool.ReportAsync(one, Limit(RestrictionRecovery.RetryNotBefore(start + TimeSpan.FromMinutes(5)), RestrictionCause.Rate));
        await pool.ReportAsync(two, Limit(RestrictionRecovery.ResetAt(start + Hour), RestrictionCause.Quota));
        Assert.Equal(2, (await pool.HoldsAsync()).Count);

        clock.Advance(TimeSpan.FromMinutes(5));
        var early = await pool.PickAsync();
        Assert.Null(early.Pick);                      // the rate limit has passed; the quota has not
        Assert.Equal(start + Hour, early.ReadyAt);
        Assert.Contains(await pool.HoldsAsync(), h => h.Restriction.Cause == RestrictionCause.Rate && h.State == AgentHoldState.Ready);

        clock.Advance(Hour - TimeSpan.FromMinutes(5));
        var probe = await Take(pool);
        Assert.True(probe.Seat.IsProbe);
        await pool.ReportAsync(probe, Ok());
        Assert.Empty(await pool.HoldsAsync());
    }

    [Fact]
    public async Task One_attempt_that_succeeds_removes_every_released_hold_it_was_admitted_under()
    {
        var clock = new ManualTimeProvider();
        var pool = Pool(clock, Choice("opus", "claude-cli", "a"));
        var start = clock.GetUtcNow();
        var pick = await Take(pool);
        await pool.ReportAsync(pick, Limit(RestrictionRecovery.ResetAt(start + Hour), RestrictionCause.Quota, RestrictionSubject.Account));
        clock.Advance(Hour);
        var probe = await Take(pool);
        await pool.ReportAsync(probe, Failed(AgentFailureKind.Transient,
            new AgentRestriction(RestrictionCause.Capacity, RestrictionSubject.Endpoint, RestrictionRecovery.RetryNotBefore(clock.GetUtcNow()))));

        // the failed attempt released its lease on the quota hold, and a different cause is a different hold
        Assert.Equal(2, (await pool.HoldsAsync()).Count);

        var again = await Take(pool);
        Assert.True(again.Seat.IsProbe);
        await pool.ReportAsync(again, Ok());
        Assert.Empty(await pool.HoldsAsync());
    }

    [Fact]
    public async Task An_endpoint_restriction_holds_every_account_of_the_provider_and_not_another_provider()
    {
        var clock = new ManualTimeProvider();
        var pool = Pool(clock, Choice(null, "claude-cli", "a", "b"), Choice(null, "codex-cli", "c"));

        await pool.ReportAsync(await Take(pool), Failed(AgentFailureKind.Transient,
            new AgentRestriction(RestrictionCause.Capacity, RestrictionSubject.Endpoint, RestrictionRecovery.RetryNotBefore(clock.GetUtcNow() + Hour))));

        var next = await Take(pool);
        Assert.Equal(("c", "codex-cli"), (Name(next), next.Seat.Account.Provider));
    }

    [Fact]
    public async Task A_pool_restriction_holds_only_the_account_it_was_seen_on_until_a_door_shows_the_allowance()
    {
        var clock = new ManualTimeProvider();
        var choice = Choice(null, "claude-cli", "a", "b");
        var unknown = Pool(clock, new InMemoryKeyValueStore(), Options(null, choice));
        await unknown.ReportAsync(await Take(unknown), Limit(RestrictionRecovery.OwnerAction, RestrictionCause.Quota, RestrictionSubject.Pool));
        Assert.Equal("b", Name(await Take(unknown)));

        var shared = Pool(clock, new InMemoryKeyValueStore(), Options(null, choice), null, [new SharedAllowance("work-plan", "a", "b")]);
        await shared.ReportAsync(await Take(shared), Limit(RestrictionRecovery.OwnerAction, RestrictionCause.Quota, RestrictionSubject.Pool));
        Assert.Null((await shared.PickAsync()).Pick);
        Assert.Contains("allowance work-plan", Assert.Single(await shared.HoldsAsync()).Scope);
    }

    [Fact]
    public void An_allowance_stated_without_evidence_is_refused()
    {
        var bad = new SharedAllowance("plan", "a") { Evidence = " " };
        Assert.Throws<InvalidOperationException>(() =>
            Pool(new ManualTimeProvider(), null, Options(null, Choice(null, "claude-cli", "a")), null, [bad]));
    }

    private sealed class SharedAllowance(string id, params string[] members) : IAgentAllowanceSource
    {
        public string Evidence { get; init; } = "both read one usage window";

        public AgentAllowance? AllowanceOf(AgentAccount account) =>
            members.Contains(account.Name) ? new AgentAllowance(id, Evidence) : null;
    }

    [Fact]
    public async Task A_limit_that_states_nothing_holds_the_exact_seat_and_an_access_failure_the_account()
    {
        var clock = new ManualTimeProvider();
        var pool = Pool(clock, Choice("opus", "claude-cli", "a"), Choice("sonnet", "claude-cli", "a"), Choice(null, "claude-cli", "b"));

        await pool.ReportAsync(await Take(pool), Failed(AgentFailureKind.Limit));
        var sonnet = await Take(pool);
        Assert.Equal(("a", "sonnet"), (Name(sonnet), sonnet.Seat.Choice.Model));

        await pool.ReportAsync(sonnet, Failed(AgentFailureKind.Access));
        Assert.Equal("b", Name(await Take(pool)));
        Assert.Contains(await pool.HoldsAsync(), h => h.Scope == "account claude-cli/a");
    }

    [Theory]
    [InlineData(AgentFailureKind.Transient)]
    [InlineData(AgentFailureKind.Fatal)]
    public async Task A_transient_or_fatal_failure_without_a_restriction_holds_nothing(AgentFailureKind kind)
    {
        var pool = Pool(new ManualTimeProvider(), Choice(null, "claude-cli", "a"));
        await pool.ReportAsync(await Take(pool), Failed(kind));
        Assert.Empty(await pool.HoldsAsync());
        Assert.Equal("a", Name(await Take(pool)));
    }

    [Fact]
    public async Task A_failed_turn_with_no_failure_holds_nothing()
    {
        var pool = Pool(new ManualTimeProvider(), Choice(null, "claude-cli", "a"));
        await pool.ReportAsync(await Take(pool), new SessionEnded(ProviderVerdict.Failed, true, null, null, null, "boom"));
        Assert.Empty(await pool.HoldsAsync());
    }

    // ── an owner action ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_owner_action_holds_for_good_and_is_cleared_by_id_and_revision()
    {
        var clock = new ManualTimeProvider();
        var pool = Pool(clock, Choice(null, "claude-cli", "a"));
        await pool.ReportAsync(await Take(pool),
            Limit(RestrictionRecovery.OwnerAction, RestrictionCause.Credit));

        clock.Advance(TimeSpan.FromDays(365));
        var blocked = await pool.PickAsync();
        Assert.Null(blocked.Pick);
        Assert.Null(blocked.ReadyAt);
        var hold = Assert.Single(await pool.HoldsAsync());
        Assert.Equal((AgentHoldState.Blocked, 1L), (hold.State, hold.Revision));

        Assert.False(await pool.ClearAsync(hold.Id, 7));
        Assert.False(await pool.ClearAsync("no-such-hold", 1));
        Assert.Null((await pool.PickAsync()).Pick);

        Assert.True(await pool.ClearAsync(hold.Id, 1));
        var attempt = await Take(pool);
        Assert.True(attempt.Seat.IsProbe);
        Assert.Null((await pool.PickAsync()).Pick);   // one attempt at a time, even after a clear

        await pool.ReportAsync(attempt, Ok());
        Assert.Empty(await pool.HoldsAsync());
    }

    [Fact]
    public async Task A_clear_meant_for_an_older_failure_cannot_lift_a_newer_one()
    {
        var clock = new ManualTimeProvider();
        var pool = Pool(clock, Choice(null, "claude-cli", "a"));
        await pool.ReportAsync(await Take(pool), Limit(RestrictionRecovery.OwnerAction, RestrictionCause.Credit));
        var seen = Assert.Single(await pool.HoldsAsync());

        // the host refills the credit and clears revision 1; the next attempt still meets the limit
        Assert.True(await pool.ClearAsync(seen.Id, seen.Revision));
        await pool.ReportAsync(await Take(pool), Limit(RestrictionRecovery.OwnerAction, RestrictionCause.Credit));
        var newer = Assert.Single(await pool.HoldsAsync());
        Assert.Equal((2L, AgentHoldState.Blocked), (newer.Revision, newer.State));

        // a second clear of the old reading, arriving late, does nothing
        Assert.False(await pool.ClearAsync(seen.Id, seen.Revision));
        Assert.Null((await pool.PickAsync()).Pick);
        Assert.True(await pool.ClearAsync(newer.Id, newer.Revision));
    }

    [Fact]
    public async Task A_success_that_started_before_a_newer_failure_does_not_remove_it()
    {
        var clock = new ManualTimeProvider();
        var pool = Pool(clock, Choice(null, "claude-cli", "a"));
        await pool.ReportAsync(await Take(pool), Limit(RestrictionRecovery.ResetAt(clock.GetUtcNow())));

        var slow = await Take(pool);            // the attempt under revision 1
        Assert.True(slow.Seat.IsProbe);
        // a concurrent turn on the same account meets the limit again (restating it) while the attempt runs
        var restated = new AgentPick(slow.Seat, "other", []);
        await pool.ReportAsync(restated, Limit(RestrictionRecovery.ResetAt(clock.GetUtcNow() + Hour)));

        await pool.ReportAsync(slow, Ok());
        var hold = Assert.Single(await pool.HoldsAsync());
        Assert.Equal((2L, AgentHoldState.Blocked), (hold.Revision, hold.State));
    }

    // ── an unknown recovery ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_unknown_recovery_holds_until_the_host_clears_it_and_reports_no_ready_time()
    {
        var clock = new ManualTimeProvider();
        var pool = Pool(clock, Choice(null, "claude-cli", "a"));
        await pool.ReportAsync(await Take(pool), Limit(RestrictionRecovery.Unknown, RestrictionCause.Rate));

        clock.Advance(TimeSpan.FromDays(30));
        var result = await pool.PickAsync();
        Assert.Null(result.Pick);
        Assert.Null(result.ReadyAt);

        var hold = Assert.Single(await pool.HoldsAsync());
        Assert.Null(hold.ReadyAt);
        Assert.True(await pool.ClearAsync(hold.Id, hold.Revision));
        Assert.True((await Take(pool)).Seat.IsProbe);
    }

    [Fact]
    public async Task An_unknown_recovery_admits_a_probe_after_the_hosts_own_cooloff_and_an_owner_action_never_does()
    {
        var clock = new ManualTimeProvider();
        var pool = Pool(clock, new InMemoryKeyValueStore(),
            Options(o => o.UnknownProbeAfter = TimeSpan.FromMinutes(60), Choice(null, "claude-cli", "a", "b")));
        var start = clock.GetUtcNow();
        await pool.ReportAsync(await Take(pool), Limit(RestrictionRecovery.Unknown, RestrictionCause.Rate));
        await pool.ReportAsync(await Take(pool), Limit(RestrictionRecovery.OwnerAction, RestrictionCause.Credit));

        var early = await pool.PickAsync();
        Assert.Null(early.Pick);
        Assert.Equal(start + Hour, early.ReadyAt);

        clock.Advance(Hour);
        var probe = await Take(pool);
        Assert.Equal(("a", true), (Name(probe), probe.Seat.IsProbe));
        Assert.Null((await pool.PickAsync()).Pick);
    }

    // ── a stale reading ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_reading_measured_before_a_failure_never_clears_it_and_a_later_one_showing_room_does()
    {
        var clock = new ManualTimeProvider();
        var pool = Pool(clock, Choice(null, "claude-cli", "a"));
        var taken = clock.GetUtcNow();
        await pool.ReportAsync(await Take(pool), Limit(RestrictionRecovery.ResetAt(taken + Hour)));
        clock.Advance(TimeSpan.FromMinutes(10));

        var account = Acct("a");
        Assert.Equal(0, await pool.ReportReadingAsync(account, taken - TimeSpan.FromMinutes(1), true));
        Assert.Equal(0, await pool.ReportReadingAsync(account, taken, true));
        Assert.Equal(0, await pool.ReportReadingAsync(account, clock.GetUtcNow(), null));
        Assert.Equal(0, await pool.ReportReadingAsync(account, clock.GetUtcNow(), false));
        Assert.Null((await pool.PickAsync()).Pick);

        Assert.Equal(1, await pool.ReportReadingAsync(account, clock.GetUtcNow(), true));
        var seat = await Take(pool);
        Assert.False(seat.Seat.IsProbe);
    }

    [Fact]
    public async Task A_newer_reading_lifts_neither_an_owner_action_nor_an_unknown_recovery_nor_an_account_the_pool_lacks()
    {
        var clock = new ManualTimeProvider();
        var pool = Pool(clock, Choice(null, "claude-cli", "a"));
        var account = Acct("a");
        await pool.ReportAsync(await Take(pool), Limit(RestrictionRecovery.OwnerAction, RestrictionCause.Credit));
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(0, await pool.ReportReadingAsync(account, clock.GetUtcNow(), true));
        Assert.Null((await pool.PickAsync()).Pick);

        // a released credit hold whose attempt then meets a rate limit with no stated end
        var credit = Assert.Single(await pool.HoldsAsync());
        await pool.ClearAsync(credit.Id, credit.Revision);
        await pool.ReportAsync(await Take(pool), Limit(RestrictionRecovery.Unknown, RestrictionCause.Rate));
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(0, await pool.ReportReadingAsync(account, clock.GetUtcNow(), true));
        Assert.Equal(2, (await pool.HoldsAsync()).Count);
        Assert.Equal(0, await pool.ReportReadingAsync(Acct("not-in-the-pool"), clock.GetUtcNow(), true));
    }

    // ── a restart ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_restart_reads_the_stored_holds_back_including_an_attempt_in_flight()
    {
        var clock = new ManualTimeProvider();
        var store = new InMemoryKeyValueStore();
        var options = Options(null, Choice(null, "claude-cli", "a", "b"));
        var before = Pool(clock, store, options);
        await before.ReportAsync(await Take(before), Limit(RestrictionRecovery.ResetAt(clock.GetUtcNow() + Hour)));
        await before.ReportAsync(await Take(before), Limit(RestrictionRecovery.OwnerAction, RestrictionCause.Credit));
        var heldBefore = await before.HoldsAsync();

        var after = Pool(clock, store, Options(null, Choice(null, "claude-cli", "a", "b")));
        Assert.Equal(heldBefore, await after.HoldsAsync());
        Assert.Null((await after.PickAsync()).Pick);

        clock.Advance(Hour);
        var probe = await Take(after);
        Assert.Equal(("a", true), (Name(probe), probe.Seat.IsProbe));

        // the attempt is leased in the store too: a second restart does not offer it again
        var third = Pool(clock, store, Options(null, Choice(null, "claude-cli", "a", "b")));
        Assert.Null((await third.PickAsync()).Pick);
    }

    [Fact]
    public async Task The_store_holds_one_key_per_hold_under_the_pools_name_and_a_removed_hold_leaves_none()
    {
        var clock = new ManualTimeProvider();
        var store = new InMemoryKeyValueStore();
        var pool = Pool(clock, store, Options(o => o.Name = "work", Choice(null, "claude-cli", "a")));
        await pool.ReportAsync(await Take(pool), Limit(RestrictionRecovery.ResetAt(clock.GetUtcNow())));

        var keys = await store.ListKeysAsync("lyntai:agent-pool:");
        Assert.StartsWith("lyntai:agent-pool:work:hold:", Assert.Single(keys));

        await pool.ReportAsync(await Take(pool), Ok());
        Assert.Empty(await store.ListKeysAsync("lyntai:agent-pool:"));
    }

    [Fact]
    public async Task A_stored_entry_that_does_not_read_is_skipped_with_a_warning()
    {
        var store = new InMemoryKeyValueStore();
        await store.SetAsync("lyntai:agent-pool:default:hold:junk", "{ not json");
        await store.SetAsync("lyntai:agent-pool:default:hold:partial", "{\"id\":\"x\"}");
        var log = new CapturingLogger();
        var pool = new AgentAccountPool(Options(null, Choice(null, "claude-cli", "a")), store, new ManualTimeProvider(),
            null, null, log.For<AgentAccountPool>());

        Assert.Empty(await pool.HoldsAsync());
        Assert.Equal(2, log.Warnings.Count);
        Assert.Equal("a", Name(await Take(pool)));
    }

    [Fact]
    public async Task Without_a_store_the_pool_works_in_process()
    {
        var clock = new ManualTimeProvider();
        var pool = Pool(clock, null, Options(null, Choice(null, "claude-cli", "a", "b")));
        await pool.ReportAsync(await Take(pool), Limit(RestrictionRecovery.OwnerAction));
        Assert.Equal("b", Name(await Take(pool)));
    }

    // ── the nothing-permitted answer ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task With_every_seat_held_the_answer_is_the_earliest_moment_time_alone_frees_one()
    {
        var clock = new ManualTimeProvider();
        var pool = Pool(clock, Choice(null, "claude-cli", "a", "b", "c"));
        var start = clock.GetUtcNow();
        await pool.ReportAsync(await Take(pool), Limit(RestrictionRecovery.ResetAt(start + TimeSpan.FromHours(3))));
        await pool.ReportAsync(await Take(pool), Limit(RestrictionRecovery.ResetAt(start + TimeSpan.FromHours(2))));
        await pool.ReportAsync(await Take(pool), Limit(RestrictionRecovery.OwnerAction));

        var result = await pool.PickAsync();
        Assert.Null(result.Pick);
        Assert.Equal(start + TimeSpan.FromHours(2), result.ReadyAt);
    }

    // ── the policy seam ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_policy_picks_among_the_permitted_seats_only()
    {
        var clock = new ManualTimeProvider();
        var seen = new List<IReadOnlyList<AgentSeat>>();
        var last = new LambdaPolicy(seats => { seen.Add(seats); return seats[^1]; });
        var pool = Pool(clock, new InMemoryKeyValueStore(), Options(null, Choice(null, "claude-cli", "a", "b", "c")), last);

        var first = await Take(pool);
        Assert.Equal("c", Name(first));
        Assert.Equal(["a", "b", "c"], seen[0].Select(s => s.Account.Name));

        await pool.ReportAsync(first, Limit(RestrictionRecovery.OwnerAction));
        Assert.Equal("b", Name(await Take(pool)));
        Assert.Equal(["a", "b"], seen[1].Select(s => s.Account.Name));
    }

    [Fact]
    public async Task A_seat_the_policy_was_not_offered_is_refused()
    {
        var stranger = new AgentSeat(0, Choice(null, "claude-cli", "z"), Acct("z"), false);
        var pool = Pool(new ManualTimeProvider(), new InMemoryKeyValueStore(),
            Options(null, Choice(null, "claude-cli", "a")), new LambdaPolicy(_ => stranger));

        await Assert.ThrowsAsync<InvalidOperationException>(() => pool.PickAsync());
    }

    private sealed class LambdaPolicy(Func<IReadOnlyList<AgentSeat>, AgentSeat> select) : IAgentAccountPolicy
    {
        public AgentSeat Select(IReadOnlyList<AgentSeat> admissible) => select(admissible);
    }

    // ── the options and the registration ─────────────────────────────────────────────────────────

    [Fact]
    public void The_options_refuse_a_pool_that_cannot_be_walked()
    {
        var clock = new ManualTimeProvider();
        Assert.Throws<ArgumentException>(() => Pool(clock, null, new AgentPoolOptions()));
        Assert.Throws<ArgumentException>(() => Pool(clock, null, Options(null, new AgentChoice { Provider = "claude-cli", Accounts = [] })));
        Assert.Throws<ArgumentException>(() => Pool(clock, null,
            Options(null, new AgentChoice { Provider = "claude-cli", Accounts = [Acct("a", "codex-cli")] })));
        Assert.Throws<ArgumentException>(() => Pool(clock, null, Options(null,
            Choice(null, "claude-cli", "a"),
            new AgentChoice { Provider = "claude-cli", Accounts = [Acct("a") with { Home = "/elsewhere" }] })));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Pool(clock, null, Options(o => o.AttemptLease = TimeSpan.Zero, Choice(null, "claude-cli", "a"))));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Pool(clock, null, Options(o => o.UnknownProbeAfter = TimeSpan.FromSeconds(-1), Choice(null, "claude-cli", "a"))));
    }

    [Fact]
    public async Task AddAgentAccountPool_registers_a_pool_over_the_registered_store_and_clock()
    {
        var services = new ServiceCollection();
        var store = new InMemoryKeyValueStore();
        var clock = new ManualTimeProvider();
        services.AddSingleton<IKeyValueStore>(store);
        services.AddSingleton<TimeProvider>(clock);
        services.AddLyntai(b => b.UseInMemoryStorage().AddAgentAccountPool(o => o.Choices.Add(Choice(null, "claude-cli", "a", "b"))));
        await using var sp = services.BuildServiceProvider();

        var pool = sp.GetRequiredService<AgentAccountPool>();
        Assert.Same(pool, sp.GetRequiredService<AgentAccountPool>());
        await pool.ReportAsync(await Take(pool), Limit(RestrictionRecovery.OwnerAction));

        Assert.Equal("b", Name(await Take(pool)));
        Assert.Single(await store.ListKeysAsync("lyntai:agent-pool:"));
    }

    [Fact]
    public void Without_AddAgentAccountPool_no_pool_is_registered_and_a_bad_pool_is_refused_at_the_call()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b.UseInMemoryStorage());
        using var sp = services.BuildServiceProvider();
        Assert.Null(sp.GetService<AgentAccountPool>());

        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddLyntai(b => b.AddAgentAccountPool(_ => { })));
    }
}
