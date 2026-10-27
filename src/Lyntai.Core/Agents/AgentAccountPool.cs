using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lyntai.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Agents;

/// <summary>The default <see cref="IAgentAccountPolicy"/>: the ordered walk, which takes the first permitted seat.</summary>
internal sealed class OrderedAgentAccountPolicy : IAgentAccountPolicy
{
    public AgentSeat Select(IReadOnlyList<AgentSeat> admissible) => admissible[0];
}

/// <summary>An account pool for agent turns: which account of which choice the next turn runs on, with the ones a
/// failure restricted held back. <b>Ask once per turn</b> (<see cref="PickAsync"/>), run the turn on
/// <see cref="AgentPick.Apply"/>, give the pick back (<see cref="ReportAsync"/>): a rotation lands on the next pick,
/// never inside a turn.
/// <list type="bullet">
/// <item><description><b>The walk:</b> choices in listed order, a choice's accounts before the next choice; the
/// first seat every covering restriction permits, or the one an <see cref="IAgentAccountPolicy"/> picks.</description></item>
/// <item><description><b>Restrictions are a set,</b> taken from a failed turn's own restriction and held on what it
/// covers (account, model on an account, endpoint, or a shared allowance a door showed). A seat runs only if every
/// covering hold permits it.</description></item>
/// <item><description><b>Released is not recovered:</b> a stated moment passing, or <see cref="ClearAsync"/> by id and
/// revision, permits ONE attempt at a time (<see cref="AgentPoolOptions.AttemptLease"/>). Success removes the hold;
/// failure restates it. An owner action only a clear releases. Older evidence never undoes a newer
/// failure.</description></item>
/// </list>
/// <para>Holds are kept in process and written through the <see cref="IKeyValueStore"/>, so a restart reads them back.
/// <b>Two processes sharing one pool are out of scope</b>: the store has no compare-and-set and the last writer would
/// win. The pool reports when a seat could be free (<see cref="AgentPickResult.ReadyAt"/>); the host sleeps.</para></summary>
public sealed class AgentAccountPool
{
    private readonly record struct Place(AgentSeat Seat, string? Allowance);

    private readonly List<Place> _walk = [];
    private readonly PoolRules _rules;
    private readonly string _prefix;
    private readonly IKeyValueStore? _store;
    private readonly TimeProvider _time;
    private readonly IAgentAccountPolicy _policy;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ImmutableDictionary<string, HoldRow> _holds = ImmutableDictionary<string, HoldRow>.Empty.WithComparers(StringComparer.Ordinal);
    private bool _loaded;

    /// <param name="options">The choices and bounds, read once.</param>
    /// <param name="store">Where the holds are kept; null = in process only.</param>
    /// <param name="time">The clock; null = the system clock.</param>
    /// <param name="allowances">The doors that can show accounts sharing an allowance; null = none.</param>
    /// <param name="policy">Which permitted seat serves; null = the first.</param>
    /// <param name="logger">Where a stored hold that does not read is reported; null = silent.</param>
    /// <exception cref="ArgumentException">No choice, a choice with no account or an account of another provider, or
    /// one account listed with two homes.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A bound is not positive (or <c>UnknownProbeAfter</c> is
    /// negative).</exception>
    /// <exception cref="InvalidOperationException">A source states an allowance with a blank id or no evidence.</exception>
    public AgentAccountPool(
        AgentPoolOptions options, IKeyValueStore? store = null, TimeProvider? time = null,
        IEnumerable<IAgentAllowanceSource>? allowances = null, IAgentAccountPolicy? policy = null,
        ILogger<AgentAccountPool>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _rules = new PoolRules(options.AttemptLease, options.UnknownProbeAfter);
        _prefix = $"lyntai:agent-pool:{Uri.EscapeDataString(options.Name)}:hold:";
        _store = store;
        _time = time ?? TimeProvider.System;
        _policy = policy ?? new OrderedAgentAccountPolicy();
        _logger = logger ?? NullLogger<AgentAccountPool>.Instance;

        var sources = allowances?.ToList() ?? [];
        for (var i = 0; i < options.Choices.Count; i++)
            foreach (var account in options.Choices[i].Accounts)
                _walk.Add(new Place(new AgentSeat(i, options.Choices[i], account, false), AllowanceOf(sources, account)));
    }

    private static string? AllowanceOf(List<IAgentAllowanceSource> sources, AgentAccount account)
    {
        foreach (var source in sources)
        {
            if (source.AllowanceOf(account) is not { } allowance) continue;
            if (string.IsNullOrWhiteSpace(allowance.Id) || string.IsNullOrWhiteSpace(allowance.Evidence))
                throw new InvalidOperationException(
                    $"An allowance of account '{account.Name}' ('{account.Provider}') was stated without an id or without evidence. " +
                    "A shared allowance exists only where a door shows it.");
            return allowance.Id;
        }
        return null;
    }

    /// <summary>The seat for the next turn: the permitted seat the policy picks, taken for that turn. When the seat is
    /// held only for one bounded attempt (<see cref="AgentSeat.IsProbe"/>), the attempt is leased to this pick and
    /// no other pick is offered the seat until it is reported, abandoned, or its lease runs out. With none permitted,
    /// <see cref="AgentPickResult.Pick"/> is null and <see cref="AgentPickResult.ReadyAt"/> says when one could be.</summary>
    /// <exception cref="InvalidOperationException">The policy returned a seat it was not offered.</exception>
    public async Task<AgentPickResult> PickAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await LoadAsync(ct).ConfigureAwait(false);
            var now = _time.GetUtcNow();
            var offered = new List<(AgentSeat Seat, string? Allowance, SeatAdmission Admission)>();
            DateTimeOffset? soonest = null;
            var hostOnly = true;
            foreach (var (seat, allowance) in _walk)
            {
                var admission = AgentPoolReducer.Admit(
                    _holds, AgentPoolReducer.Covering(seat.Account, seat.Choice.Model, allowance), now, _rules);
                if (admission.Permitted)
                    offered.Add((seat with { IsProbe = admission.Probing.Count > 0 }, allowance, admission));
                else if (admission.ReadyAt is { } at)
                {
                    hostOnly = false;
                    soonest = soonest is { } s && s <= at ? s : at;
                }
            }

            if (offered.Count == 0) return new AgentPickResult(null, hostOnly ? null : soonest);

            var chosen = _policy.Select([.. offered.Select(o => o.Seat)]);
            var index = offered.FindIndex(o => o.Seat.Equals(chosen));
            if (index < 0)
                throw new InvalidOperationException(
                    $"{_policy.GetType().Name} returned a seat it was not offered: account '{chosen.Account.Name}'.");

            var token = Guid.NewGuid().ToString("N");
            var probing = offered[index].Admission.Probing;
            await CommitAsync(AgentPoolReducer.Lease(_holds, probing, token, now, _rules), ct).ConfigureAwait(false);
            return new AgentPickResult(
                new AgentPick(offered[index].Seat, token, [.. probing.Select(h => (h.Id, h.Revision))]), null);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Give back a pick with the turn's terminal event. A success removes the holds the attempt was admitted
    /// under (only those no newer failure has restated); a failure ends the attempt and, when it restricts something,
    /// holds it (<see cref="AgentFailure.Restriction"/>; a limit or an access failure restricts even when it says no
    /// more; a transient or fatal failure only when it carries one). A failure with no <see cref="SessionEnded.Failure"/>
    /// restricts nothing.</summary>
    /// <exception cref="InvalidOperationException">The pick was already reported or abandoned.</exception>
    public async Task ReportAsync(AgentPick pick, SessionEnded ended, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pick);
        ArgumentNullException.ThrowIfNull(ended);
        Settle(pick);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await LoadAsync(ct).ConfigureAwait(false);
            var holds = _holds;
            if (!ended.IsError)
                holds = AgentPoolReducer.Succeed(holds, pick.Leases, pick.Token);
            else
            {
                holds = AgentPoolReducer.Unlease(holds, pick.Token);
                var seat = pick.Seat;
                var allowance = _walk.First(p => p.Seat.Account == seat.Account).Allowance;
                if (ended.Failure is { } failure
                    && AgentPoolReducer.FailureHold(failure, seat.Account, seat.Choice.Model, allowance) is { } held)
                    holds = AgentPoolReducer.Hold(holds, held.Scope, held.Restriction, _time.GetUtcNow());
            }
            await CommitAsync(holds, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Give back a pick whose turn never ran, so the attempt it leased can be taken by another.</summary>
    /// <exception cref="InvalidOperationException">The pick was already reported or abandoned.</exception>
    public async Task AbandonAsync(AgentPick pick, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pick);
        Settle(pick);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await LoadAsync(ct).ConfigureAwait(false);
            await CommitAsync(AgentPoolReducer.Unlease(_holds, pick.Token), ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Clear a hold the host has dealt with (a credit refilled, a login restored, a probe it chose to
    /// allow), naming the hold's <see cref="AgentHold.Id"/> and <see cref="AgentHold.Revision"/>. The hold becomes
    /// released — one attempt at a time — and is removed only when an attempt succeeds. Returns false and changes
    /// nothing when the hold is gone or its revision is not the current one, because a newer failure has restated it
    /// since the host looked.</summary>
    public async Task<bool> ClearAsync(string id, long revision, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await LoadAsync(ct).ConfigureAwait(false);
            var (holds, cleared) = AgentPoolReducer.Clear(_holds, id, revision, _time.GetUtcNow());
            await CommitAsync(holds, ct).ConfigureAwait(false);
            return cleared;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Tell the pool what a reading of <paramref name="account"/>'s allowance showed, measured at
    /// <paramref name="measuredAt"/>. A reading that shows room (<paramref name="hasRoom"/> true) lifts the holds on
    /// that account, and on the allowance it draws on, whose recovery was a stated moment — if it was measured
    /// AFTER the hold was taken. An older reading, one that shows no room, and one that shows nothing (null: absent is
    /// unknown, never zero) change nothing; an owner action or an unknown recovery is never lifted by a reading.
    /// Returns how many holds were lifted; an account the pool does not list lifts none.</summary>
    public async Task<int> ReportReadingAsync(
        AgentAccount account, DateTimeOffset measuredAt, bool? hasRoom, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (_walk.FirstOrDefault(p => p.Seat.Account.Provider == account.Provider && p.Seat.Account.Name == account.Name)
            is not { Seat: not null } place)
            return 0;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await LoadAsync(ct).ConfigureAwait(false);
            var scopes = new List<string> { AgentPoolReducer.AccountScope(account.Provider, account.Name) };
            if (place.Allowance is not null) scopes.Add(AgentPoolReducer.AllowanceScope(place.Allowance));
            var (holds, lifted) = AgentPoolReducer.Lift(_holds, scopes, measuredAt, hasRoom);
            await CommitAsync(holds, ct).ConfigureAwait(false);
            return lifted;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Every hold, in id order, as it stands now.</summary>
    public async Task<IReadOnlyList<AgentHold>> HoldsAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await LoadAsync(ct).ConfigureAwait(false);
            var now = _time.GetUtcNow();
            return [.. _holds.Values.OrderBy(h => h.Id, StringComparer.Ordinal).Select(h => new AgentHold(
                h.Id, h.Revision, AgentPoolReducer.Label(h.Scope), h.Restriction, h.ObservedAt,
                AgentPoolReducer.Phase(h, now, _rules), AgentPoolReducer.ReleaseTime(h, _rules)))];
        }
        finally { _gate.Release(); }
    }

    private static void Settle(AgentPick pick)
    {
        if (!pick.TrySettle())
            throw new InvalidOperationException("This pick was already reported or abandoned; a pick is given back once.");
    }

    // ── state in the key-value store ─────────────────────────────────────────────────────────────

    private async Task LoadAsync(CancellationToken ct)
    {
        if (_loaded) return;
        var holds = _holds;
        if (_store is not null)
            foreach (var key in await _store.ListKeysAsync(_prefix, ct).ConfigureAwait(false))
                if (Parse(key, await _store.GetAsync(key, ct).ConfigureAwait(false)) is { } hold)
                    holds = holds.SetItem(hold.Id, hold);
        _holds = holds;
        _loaded = true;
    }

    // Memory is the truth in process; the write follows it. A write that throws leaves the new state in force here
    // and unwritten there, and the exception says so to the caller.
    private async Task CommitAsync(ImmutableDictionary<string, HoldRow> next, CancellationToken ct)
    {
        var previous = _holds;
        _holds = next;
        if (_store is null) return;
        foreach (var (id, hold) in next)
            if (!previous.TryGetValue(id, out var was) || !ReferenceEquals(was, hold))
                await _store.SetAsync(_prefix + id, Write(hold), ct).ConfigureAwait(false);
        foreach (var id in previous.Keys)
            if (!next.ContainsKey(id))
                await _store.DeleteAsync(_prefix + id, ct).ConfigureAwait(false);
    }

    private static string Write(HoldRow hold)
    {
        var o = new JsonObject
        {
            ["id"] = hold.Id,
            ["revision"] = hold.Revision,
            ["scope"] = hold.Scope,
            ["cause"] = hold.Restriction.Cause.ToString(),
            ["subject"] = hold.Restriction.Subject.ToString(),
            ["recovery"] = hold.Restriction.Recovery.Kind.ToString(),
            ["observedAt"] = Stamp(hold.ObservedAt),
        };
        if (hold.Restriction.Recovery.At is { } at) o["at"] = Stamp(at);
        if (hold.ReleasedAt is { } released) o["releasedAt"] = Stamp(released);
        if (hold.LeaseUntil is { } until) o["leaseUntil"] = Stamp(until);
        if (hold.LeaseToken is { } token) o["leaseToken"] = token;
        return o.ToJsonString();
    }

    private HoldRow? Parse(string key, string? raw)
    {
        if (raw is null) return null;
        try
        {
            var o = JsonNode.Parse(raw)!.AsObject();
            var recovery = Enum.Parse<RecoveryKind>(Text(o, "recovery"));
            var stated = o["at"] is { } at ? Time(at) : (DateTimeOffset?)null;
            if (recovery is RecoveryKind.ResetAt or RecoveryKind.RetryNotBefore && stated is null)
                throw new FormatException("a stated recovery with no moment");
            return new HoldRow(
                Text(o, "id"), o["revision"]!.GetValue<long>(), Text(o, "scope"),
                new AgentRestriction(
                    Enum.Parse<RestrictionCause>(Text(o, "cause")), Enum.Parse<RestrictionSubject>(Text(o, "subject")),
                    new RestrictionRecovery(recovery, stated)),
                Time(o["observedAt"]!),
                o["releasedAt"] is { } released ? Time(released) : null,
                o["leaseUntil"] is { } until ? Time(until) : null,
                o["leaseToken"]?.GetValue<string>());
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException
                                      or ArgumentException or NullReferenceException or OverflowException)
        {
            _logger.LogWarning(e, "agent pool: skipping '{Key}' — it does not read as a hold", key);
            return null;
        }
    }

    private static string Stamp(DateTimeOffset at) => at.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Time(JsonNode node) =>
        DateTimeOffset.ParseExact(node.GetValue<string>(), "O", CultureInfo.InvariantCulture);

    private static string Text(JsonObject o, string name) =>
        o[name]?.GetValue<string>() ?? throw new FormatException($"no '{name}'");
}
