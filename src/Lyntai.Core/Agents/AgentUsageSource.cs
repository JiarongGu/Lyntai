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

/// <summary>One window of an account's allowance, as the vendor's server stated it. A window the server states in part
/// is kept in part: a missing use, length or reset is null, never made up.</summary>
/// <param name="Name">What the window is: <see cref="FiveHour"/>, <see cref="Weekly"/>, or <c>"{n}-minutes"</c> for a
/// window of another length, named by that length and not guessed.</param>
/// <param name="UsedPercent">How much of the window is used, from 0 to 100, kept as the server wrote it (<c>23.5</c> is
/// 23.5). Null when the server stated none.</param>
/// <param name="Length">The window's length, when the server states it or the name implies it.</param>
/// <param name="ResetsAt">When the window resets, when the server states it (a window not yet opened states none).</param>
public sealed record AgentAllowanceWindow(string Name, double? UsedPercent, TimeSpan? Length, DateTimeOffset? ResetsAt)
{
    /// <summary>A window of five hours.</summary>
    public const string FiveHour = "five-hour";

    /// <summary>A window of seven days.</summary>
    public const string Weekly = "weekly";

    /// <summary>What the window limits when it is not the account's own limit: Claude's suffix after
    /// <c>seven_day_</c> (<c>"opus"</c>), a Codex limit's <c>limitId</c>. Null for the account's own windows, the only
    /// ones <see cref="AgentAllowanceReading.HasRoom"/> reads.</summary>
    public string? Scope { get; init; }

    /// <summary>The least difference the server's written digits show in <see cref="UsedPercent"/>: 1 for <c>23</c>,
    /// 0.1 for <c>23.5</c>. Null when no use was stated or it was written in exponent form.</summary>
    public double? UsedStep { get; init; }
}

/// <summary>One limit row the server stated beside its windows (Claude's <c>rate_limits.limits[]</c>), as it wrote it.
/// The server's severity for the row is its judgement and is not carried.</summary>
/// <param name="Kind">The server's word for the limit (<c>"session"</c>, <c>"weekly_all"</c>); not a value to parse.</param>
/// <param name="Scope">What the row limits when it is not the account's own limit, as a word: a string scope as written,
/// or the id of the model an object scope names. Null for the account's own limit and for a row scoped by a surface alone.</param>
/// <param name="UsedPercent">The use the row states, from 0 to 100, kept as written; null when it stated none.</param>
/// <param name="UsedStep">The least difference the written digits show, as <see cref="AgentAllowanceWindow.UsedStep"/>.</param>
/// <param name="ResetsAt">When the row resets, when the server states it.</param>
public sealed record AgentAllowanceLimit(string Kind, string? Scope, double? UsedPercent, double? UsedStep, DateTimeOffset? ResetsAt)
{
    /// <summary>The display name the server gave the model <see cref="Scope"/> names; null when it gave none or the scope
    /// is not a model.</summary>
    public string? ScopeName { get; init; }

    /// <summary>The surface an object scope names, as written; null when the scope states none.</summary>
    public string? Surface { get; init; }
}

/// <summary>Why a reading is unknown. The values are stable words for a host to branch on or log; none is a format
/// to parse beyond equality.</summary>
public static class AgentAllowanceUnknown
{
    /// <summary>The host has not enabled the ability, or enabled it as off. Nothing was started.</summary>
    public const string Disabled = "disabled";

    /// <summary>The account is not this provider's, has no usable name or home, or is a key (it sets an
    /// <see cref="AgentAccount.Environment"/> entry: a key has no signed-in allowance to read, and the read starts no
    /// child under a key). Nothing was started.</summary>
    public const string AccountRefused = "account-refused";

    /// <summary>The registered <see cref="Lyntai.Processes.IProcessRunner"/> cannot hold a process open for an
    /// exchange. Nothing was started.</summary>
    public const string RunnerUnsupported = "runner-unsupported";

    /// <summary>The binary's version could not be read, so it cannot be matched to a validated one. The server was
    /// not started.</summary>
    public const string VersionUnreadable = "version-unreadable";

    /// <summary>The host's <see cref="AgentUsageReadingOptions.VersionPolicy"/> did not start the server: by default
    /// because the binary's version is not one whose usage response is validated, and also when a policy declines or
    /// throws. The server was not started.</summary>
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
/// of the same provider. <b>What it does not carry</b>: the account id, the credits or balance, and the severity the
/// server gives a limit. A value the server stated that this reader cannot recognise makes the whole reading unknown
/// rather than leaving it out.</para></summary>
public sealed record AgentAllowanceReading
{
    /// <summary>When the library read the answer, on its own clock. Pass it to
    /// <see cref="AgentAccountPool.ReportReadingAsync"/> as <c>measuredAt</c>.</summary>
    public required DateTimeOffset MeasuredAt { get; init; }

    /// <summary>The windows the server stated, the account's own and any scoped one; empty exactly when
    /// <see cref="UnknownReason"/> is set.</summary>
    public IReadOnlyList<AgentAllowanceWindow> Windows { get; init; } = [];

    /// <summary>The limit rows the server stated beside its windows (Claude's <c>limits[]</c>), by kind and scope;
    /// empty when it stated none or the door's server sends none.</summary>
    public IReadOnlyList<AgentAllowanceLimit> Limits { get; init; } = [];

    /// <summary>The name the server gives the account's own limit (Codex's <c>limitId</c>), when it states one.</summary>
    public string? LimitId { get; init; }

    /// <summary>The plan the server named for the account, when it did (<c>"team"</c>, <c>"plus"</c>); the vendor's
    /// word, not a value to match on.</summary>
    public string? Plan { get; init; }

    /// <summary>Why the reading is unknown, one of <see cref="AgentAllowanceUnknown"/>'s; null when it was read.</summary>
    public string? UnknownReason { get; init; }

    /// <summary>The version of the binary this was read on, as the library parses it (<c>"2.1.296"</c>). Null when no
    /// version was read: the policy does not probe, the version could not be read, or the read ended before the probe.</summary>
    public string? Version { get; init; }

    /// <summary>Whether the library has validated <see cref="Version"/>'s usage response; null exactly when
    /// <see cref="Version"/> is. False on a reading a host's <see cref="AgentUsageReadingOptions.VersionPolicy"/> let through,
    /// so a host that keeps readings can tell one from a validated one.</summary>
    public bool? VersionValidated { get; init; }

    /// <summary>True when the server's answer was recognised and carried at least one window.</summary>
    public bool IsKnown => UnknownReason is null && Windows.Count > 0;

    /// <summary>Whether the account has room, for <see cref="AgentAccountPool.ReportReadingAsync"/>'s
    /// <c>hasRoom</c>: false when one of the account's own windows (<see cref="AgentAllowanceWindow.Scope"/> null) is
    /// full (100 percent), true when every one shows use under it, null — unknown — when the reading is unknown,
    /// states no own window, or an own window states no use. A full scoped window is not an account with no room, and
    /// room in the windows the server stated is not room in one it did not; read <see cref="Windows"/> for a scope.</summary>
    public bool? HasRoom
    {
        get
        {
            if (!IsKnown) return null;
            var own = Windows.Where(w => w.Scope is null).ToList();
            if (own.Count == 0) return null;
            if (own.Any(w => w.UsedPercent >= 100)) return false;
            return own.All(w => w.UsedPercent is not null) ? true : null;
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

    /// <summary>The folder the vendor's server runs in, chosen per account: a fully qualified path, or null for the
    /// default, a fresh empty folder under the system's temporary folder that is removed when the read ends. A named
    /// folder is created when absent and <b>never removed or emptied</b>: it is the host's. <b>To the vendor it is a
    /// project</b>: it keys state by that path and loads the project's extensions from it, so name one only where
    /// that is meant. A path that is not fully qualified, holds a control character, or comes from a function that
    /// throws reads <see cref="AgentAllowanceUnknown.AccountRefused"/> before anything is started. Default null.</summary>
    public Func<AgentAccount, string?>? WorkingDirectory { get; set; }

    /// <summary>What to do with a version the library has not validated: <see cref="AgentUsageVersionPolicies.ValidatedOnly"/>
    /// by default, which reads <see cref="AgentAllowanceUnknown.VersionNotValidated"/> and starts nothing;
    /// <see cref="AgentUsageVersionPolicies.AnyVersion"/>, or a host's own <see cref="IAgentUsageVersionPolicy"/>, starts the
    /// server on it. The descriptor's claim is the same under every policy, and so is what counts as a reading.
    /// Null is refused where it is set.</summary>
    public IAgentUsageVersionPolicy VersionPolicy { get; set; } = AgentUsageVersionPolicies.ValidatedOnly;

    /// <summary>The name the server is told its client has, where the protocol carries one (Codex's
    /// <c>initialize</c>) and ignored where it carries none (Claude's control request). A server may act on it.
    /// Non-empty, at most 64 characters and free of control characters; written by a
    /// JSON writer, so a quote or backslash is carried intact. Default <c>"lyntai"</c>.</summary>
    public string ClientName { get; set; } = "lyntai";

    /// <summary>The version the server is told its client is, under the same rules and the same reach as
    /// <see cref="ClientName"/>. Default <c>"1"</c>.</summary>
    public string ClientVersion { get; set; } = "1";

    private const int MaxClientLength = 64;

    internal void Validate()
    {
        if (Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(Timeout), Timeout, "A usage read needs a timeout between 0 and 5 minutes.");
        CheckClient(ClientName, nameof(ClientName));
        CheckClient(ClientVersion, nameof(ClientVersion));
        ArgumentNullException.ThrowIfNull(VersionPolicy);
    }

    // the value is left out of the message: it is a host's text, and may hold the control character it is refused for
    private static void CheckClient(string? value, string name)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxClientLength || value.Any(char.IsControl))
            throw new ArgumentOutOfRangeException(name,
                $"A usage read's {name} is 1 to {MaxClientLength} characters with no control character.");
    }
}
