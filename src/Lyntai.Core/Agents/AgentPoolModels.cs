namespace Lyntai.Agents;

/// <summary>One option the host would run a turn on: a provider, optionally a model and an effort, and the accounts
/// to try for it, in the order to try them. A pool is an ordered list of these (<see cref="AgentPoolOptions.Choices"/>)
/// and walks one choice's accounts before the next choice.</summary>
public sealed record AgentChoice
{
    /// <summary>The id of the registered provider (<see cref="AgentProviderDescriptor.Id"/>).</summary>
    public required string Provider { get; init; }

    /// <summary>The backend's own model id, passed through as <see cref="AgentSessionOptions.Model"/>; null = the
    /// backend's default, and the host's own <see cref="AgentSessionOptions.Model"/> stands.</summary>
    public string? Model { get; init; }

    /// <summary>The host's word for an effort level. Carried to the host on the pick and never interpreted: no
    /// neutral option carries it, because each adapter spells effort in its own options.</summary>
    public string? Effort { get; init; }

    /// <summary>The accounts of this choice, at least one, each of this choice's provider.</summary>
    public required IReadOnlyList<AgentAccount> Accounts { get; init; }
}

/// <summary>One account of one choice, as the pool offers it for a turn.</summary>
/// <param name="ChoiceIndex">The choice's place in <see cref="AgentPoolOptions.Choices"/>.</param>
/// <param name="Choice">The choice.</param>
/// <param name="Account">The account.</param>
/// <param name="IsProbe">True when a restriction on this seat has been released and the seat is permitted for ONE
/// bounded attempt: a reset moment passing, or a host's release, is not evidence the allowance is back.</param>
public sealed record AgentSeat(int ChoiceIndex, AgentChoice Choice, AgentAccount Account, bool IsProbe);

/// <summary>Which seat serves a turn, beyond the ordered walk. The pool offers the seats every applicable restriction
/// permits, in walk order (first choice first, a choice's accounts in their listed order), and the policy picks one.
/// Only the ordered walk ships (<see cref="AgentAccountPool"/>'s default, which takes the first); a spread, a
/// least-recently-used or a most-work rule is a host's own implementation.
/// <para>A policy sees seats and nothing else: it cannot lift a restriction, and a seat it returns that was not
/// offered throws.</para></summary>
public interface IAgentAccountPolicy
{
    /// <param name="admissible">The permitted seats in walk order; never empty.</param>
    /// <returns>One of <paramref name="admissible"/>.</returns>
    AgentSeat Select(IReadOnlyList<AgentSeat> admissible);
}

/// <summary>What the pool says when asked for a seat.</summary>
/// <param name="Pick">The seat to run the turn on, or null when every seat is restricted.</param>
/// <param name="ReadyAt">With no pick: the earliest moment a seat could be permitted by the passage of time alone, or
/// null when only a host action (a clear, a release) can free one. It is a bound, not a promise: it may come sooner
/// (an attempt in flight ends) or the attempt it frees may fail. The host sleeps; the library never does.</param>
public sealed record AgentPickResult(AgentPick? Pick, DateTimeOffset? ReadyAt);

/// <summary>A seat taken for ONE turn. Run the turn with <see cref="Apply"/>, then hand the pick back exactly once to
/// <see cref="AgentAccountPool.ReportAsync"/> (or <see cref="AgentAccountPool.AbandonAsync"/> if the turn never ran).
/// Asking the pool again inside a turn is how a rotation lands mid-turn, and the pool does not do that for you:
/// rotation happens at the NEXT pick.</summary>
public sealed class AgentPick
{
    private int _settled;

    internal AgentPick(AgentSeat seat, string token, IReadOnlyList<(string Id, long Revision)> leases)
    {
        Seat = seat;
        Token = token;
        Leases = leases;
    }

    /// <summary>The seat.</summary>
    public AgentSeat Seat { get; }

    internal string Token { get; }

    internal IReadOnlyList<(string Id, long Revision)> Leases { get; }

    /// <summary>The options for the turn under this seat: <see cref="AgentSessionOptions.Account"/> is the seat's
    /// account, and <see cref="AgentSessionOptions.Model"/> is the choice's when it names one (else the host's own
    /// stands). The runtime type of <paramref name="options"/> is kept.</summary>
    public AgentSessionOptions Apply(AgentSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options with { Account = Seat.Account, Model = Seat.Choice.Model ?? options.Model };
    }

    internal bool TrySettle() => Interlocked.Exchange(ref _settled, 1) == 0;
}

/// <summary>Whether a held restriction is in the way.</summary>
public enum AgentHoldState
{
    /// <summary>It forbids every seat it covers, until its moment passes or the host acts.</summary>
    Blocked,

    /// <summary>Released: its covered seats are permitted for one bounded attempt each time, not freely.</summary>
    Ready,

    /// <summary>An attempt is in flight under it; no other is admitted until that one is reported or its lease runs out.</summary>
    Attempting,
}

/// <summary>One restriction the pool holds, as <see cref="AgentAccountPool.HoldsAsync"/> reports it.</summary>
/// <param name="Id">Names the restriction: what it covers and its cause. A newer failure of the same cause on the
/// same scope is the same hold at a higher revision.</param>
/// <param name="Revision">Starts at 1 and rises each time a newer failure restates the hold. A clear names it, so a
/// clear meant for an older failure cannot lift a newer one.</param>
/// <param name="Scope">What it covers, in words for a log or a screen (not a format to parse).</param>
/// <param name="Restriction">The door's own statement (cause, subject, recovery).</param>
/// <param name="ObservedAt">When the pool took the failure, on its own clock.</param>
/// <param name="State">Where it stands now.</param>
/// <param name="ReadyAt">When time alone releases it, or null when only the host can. For a hold already
/// <see cref="AgentHoldState.Ready"/> it is when it became so.</param>
public sealed record AgentHold(
    string Id, long Revision, string Scope, AgentRestriction Restriction, DateTimeOffset ObservedAt,
    AgentHoldState State, DateTimeOffset? ReadyAt);

/// <summary>An allowance several accounts draw on, as a door states it.</summary>
/// <param name="Id">Names the allowance; two accounts with the same id share it.</param>
/// <param name="Evidence">What the door read that says so, in a line a reader can act on. Required: another local
/// profile name proves nothing.</param>
public sealed record AgentAllowance(string Id, string Evidence);

/// <summary>OPTIONAL, on an <see cref="IAgentProvider"/>: a door that can show which of its accounts share one
/// allowance says so here, with its evidence. The pool finds it by type test over the registered providers
/// (<c>provider is IAgentAllowanceSource</c>). Without it a restriction on "the pool" holds only the account it was
/// seen on, since the pool's extent is unknown and an unknown extent is not guessed. No door ships it today.</summary>
public interface IAgentAllowanceSource
{
    /// <summary>The allowance <paramref name="account"/> draws on, or null when this door cannot show one.</summary>
    AgentAllowance? AllowanceOf(AgentAccount account);
}
