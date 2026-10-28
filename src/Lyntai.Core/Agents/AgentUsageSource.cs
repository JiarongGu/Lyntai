namespace Lyntai.Agents;

/// <summary>OPTIONAL, on an <see cref="IAgentProvider"/>: a door that can ask the agent's own server one question
/// about an account's allowance. A host finds it by type test (<c>provider is IAgentUsageSource</c>), as the pool
/// finds <see cref="IAgentAllowanceSource"/>, and the descriptor's <see cref="AgentAbilities.UsageReading"/> claim
/// says which versions it is validated on and what a start may do.
/// <para><b>It is off unless the host enables it</b> (<c>AddAgentUsageReading</c>) <b>and it runs only when the
/// host calls <see cref="ReadAllowanceAsync"/></b>: nothing in the library — a turn, the pool, a retry — ever
/// invokes it, because the read starts the vendor's own process under the account's home.</para></summary>
public interface IAgentUsageSource
{
    /// <summary>Ask for <paramref name="account"/>'s allowance. Any failure, and any answer the door does not
    /// recognise, is an <see cref="AgentAllowanceReading"/> that is unknown with its reason — never a guess, and
    /// never a throw. The one exception is cancellation, which propagates after the process is ended.</summary>
    /// <param name="account">The host's account. Its <see cref="AgentAccount.Provider"/> must be this provider's.</param>
    /// <param name="ct">Cancels the read and ends the process it started.</param>
    Task<AgentAllowanceReading> ReadAllowanceAsync(AgentAccount account, CancellationToken ct = default);
}

/// <summary>One window of an account's allowance, as the vendor's server stated it.</summary>
/// <param name="Name">What the window is: <see cref="FiveHour"/>, <see cref="Weekly"/>, or <c>"{n}-minutes"</c> for a
/// window of another length, named by that length and not guessed.</param>
/// <param name="UsedPercent">How much of the window is used, a whole percent from 0 to 100 as the vendors state it.
/// Null when the server stated none.</param>
/// <param name="Length">The window's length, when the server states it or the name implies it.</param>
/// <param name="ResetsAt">When the window resets, when the server states it (a window not yet opened states none).</param>
public sealed record AgentAllowanceWindow(string Name, int? UsedPercent, TimeSpan? Length, DateTimeOffset? ResetsAt)
{
    /// <summary>A window of five hours.</summary>
    public const string FiveHour = "five-hour";

    /// <summary>A window of seven days.</summary>
    public const string Weekly = "weekly";
}

/// <summary>Why a reading is unknown. The values are stable words for a host to branch on or log; none is a format
/// to parse beyond equality.</summary>
public static class AgentAllowanceUnknown
{
    /// <summary>The host has not enabled the ability, or enabled it as off. Nothing was started.</summary>
    public const string Disabled = "disabled";

    /// <summary>The account is not this provider's, or has no usable name or home. Nothing was started.</summary>
    public const string AccountRefused = "account-refused";

    /// <summary>The registered <see cref="Lyntai.Processes.IProcessRunner"/> cannot hold a process open for an
    /// exchange. Nothing was started.</summary>
    public const string RunnerUnsupported = "runner-unsupported";

    /// <summary>The binary's version could not be read, so it cannot be matched to a validated one. The server was
    /// not started.</summary>
    public const string VersionUnreadable = "version-unreadable";

    /// <summary>The binary's version is not one whose usage response is validated. The server was not started.</summary>
    public const string VersionNotValidated = "version-not-validated";

    /// <summary>The process could not be started.</summary>
    public const string SpawnFailed = "spawn-failed";

    /// <summary>The server did not answer in the time allowed.</summary>
    public const string TimedOut = "timed-out";

    /// <summary>The server ended without answering.</summary>
    public const string NoAnswer = "no-answer";

    /// <summary>The server answered that the account is not signed in.</summary>
    public const string NotSignedIn = "not-signed-in";

    /// <summary>The server answered and reported no allowance for the account.</summary>
    public const string NotReported = "allowance-not-reported";

    /// <summary>The server answered with something this validated version's response does not look like.</summary>
    public const string Unrecognised = "unrecognised-answer";
}

/// <summary>What an account's allowance read as: windows with their use, or unknown with the reason. Absent is
/// unknown and never zero or full.
/// <para>Windows are the vendor's own and are not normalised across vendors beyond their names; compare readings
/// of the same provider. The reading carries no account identifier and no credential.</para></summary>
public sealed record AgentAllowanceReading
{
    /// <summary>When the library read the answer, on its own clock. Pass it to
    /// <see cref="AgentAccountPool.ReportReadingAsync"/> as <c>measuredAt</c>.</summary>
    public required DateTimeOffset MeasuredAt { get; init; }

    /// <summary>The windows the server stated; empty exactly when <see cref="UnknownReason"/> is set.</summary>
    public IReadOnlyList<AgentAllowanceWindow> Windows { get; init; } = [];

    /// <summary>The plan the server named for the account, when it did (<c>"team"</c>, <c>"plus"</c>); the vendor's
    /// word, not a value to match on.</summary>
    public string? Plan { get; init; }

    /// <summary>Why the reading is unknown, one of <see cref="AgentAllowanceUnknown"/>'s; null when it was read.</summary>
    public string? UnknownReason { get; init; }

    /// <summary>True when the server's answer was recognised and carried at least one window.</summary>
    public bool IsKnown => UnknownReason is null && Windows.Count > 0;

    /// <summary>Whether the account has room, for <see cref="AgentAccountPool.ReportReadingAsync"/>'s
    /// <c>hasRoom</c>: false when a stated window is full (100 percent), true when every stated window shows use
    /// under it, null — unknown — when the reading is unknown or a window states no use. Room in the windows the
    /// server stated is not room in one it did not.</summary>
    public bool? HasRoom
    {
        get
        {
            if (!IsKnown) return null;
            if (Windows.Any(w => w.UsedPercent >= 100)) return false;
            return Windows.All(w => w.UsedPercent is not null) ? true : null;
        }
    }

    /// <summary>An unknown reading.</summary>
    /// <param name="reason">One of <see cref="AgentAllowanceUnknown"/>'s.</param>
    /// <param name="measuredAt">When the library gave up or declined.</param>
    public static AgentAllowanceReading Unknown(string reason, DateTimeOffset measuredAt) =>
        new() { MeasuredAt = measuredAt, UnknownReason = reason };
}

/// <summary>The bound on <see cref="IAgentUsageSource"/> (<c>AddAgentUsageReading</c>).</summary>
public sealed class AgentUsageReadingOptions
{
    /// <summary>Whether the ability runs at all. <c>AddAgentUsageReading</c> sets it; a host that sets it back to
    /// false keeps the registration and the read answers <see cref="AgentAllowanceUnknown.Disabled"/> without a spawn.</summary>
    public bool Enabled { get; set; }

    /// <summary>The wall clock of one read, spawn to answer, from the start of the server; the process is ended when
    /// it passes. The version probe has its own, shorter bound. Default 30 seconds, since a vendor's first start
    /// under a fresh home has taken most of ten.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(Timeout), Timeout, "A usage read needs a timeout between 0 and 5 minutes.");
    }
}
