namespace Lyntai.Agents;

/// <summary>What a failed agent turn was, read from the door's own failure — never from what the agent said.
/// Carried by <see cref="SessionEnded.Failure"/>. It maps onto <see cref="Lyntai.Inference.ProviderVerdict"/>
/// and adds no member to it: <see cref="Limit"/> is <c>RateLimited</c>, <see cref="Access"/> is
/// <c>AuthFailed</c>, <see cref="Transient"/> is <c>Failed</c>, and <see cref="Fatal"/> is whatever
/// <see cref="Lyntai.Inference.ProviderVerdictClassifier"/> says of the words.</summary>
public enum AgentFailureKind
{
    /// <summary>Anything no field or sentence read — the default, so an unread failure is never taken for
    /// something retryable.</summary>
    Fatal = 0,

    /// <summary>An allowance is spent: a quota, a rate, a credit balance.</summary>
    Limit,

    /// <summary>The account is not allowed in: a rejected or missing login, a held account.</summary>
    Access,

    /// <summary>A fault of the service or the model's capacity that a later attempt may not meet.</summary>
    Transient,
}

/// <summary>What ran out or is held back, when a failure says.</summary>
public enum RestrictionCause
{
    /// <summary>The door did not say.</summary>
    Unknown = 0,

    /// <summary>A usage window or an allowance (a five-hour or weekly limit, "quota exceeded").</summary>
    Quota,

    /// <summary>A rate of requests ("too many requests", a 429).</summary>
    Rate,

    /// <summary>A paid balance or a spend cap.</summary>
    Credit,

    /// <summary>Too many calls at once.</summary>
    Concurrency,

    /// <summary>The service or a model has no room right now ("overloaded", "at capacity").</summary>
    Capacity,
}

/// <summary>What a restriction holds back.</summary>
public enum RestrictionSubject
{
    /// <summary>The door did not say. Another local profile name proves nothing, so this stays unknown.</summary>
    Unknown = 0,

    /// <summary>The signed-in account.</summary>
    Account,

    /// <summary>An allowance several accounts draw on.</summary>
    Pool,

    /// <summary>One model; another on the same account is still available.</summary>
    Model,

    /// <summary>The service the door talks to.</summary>
    Endpoint,
}

/// <summary>How a restriction ends.</summary>
public enum RecoveryKind
{
    /// <summary>Nothing was stated. Lyntai never invents a deadline: a default cool-off is the host's option.</summary>
    Unknown = 0,

    /// <summary>The allowance renews at <see cref="RestrictionRecovery.At"/>.</summary>
    ResetAt,

    /// <summary>The service asked for no new attempt before <see cref="RestrictionRecovery.At"/>.</summary>
    RetryNotBefore,

    /// <summary>A person must act (refill, upgrade, sign in); waiting does not help.</summary>
    OwnerAction,
}

/// <summary>A restriction's way out: a stated moment, or the absence of one.</summary>
/// <param name="Kind">How it ends.</param>
/// <param name="At">The moment, for <see cref="RecoveryKind.ResetAt"/> and <see cref="RecoveryKind.RetryNotBefore"/>;
/// null otherwise.</param>
public sealed record RestrictionRecovery(RecoveryKind Kind, DateTimeOffset? At = null)
{
    /// <summary>Nothing was stated.</summary>
    public static RestrictionRecovery Unknown { get; } = new(RecoveryKind.Unknown);

    /// <summary>A person must act.</summary>
    public static RestrictionRecovery OwnerAction { get; } = new(RecoveryKind.OwnerAction);

    /// <summary>The allowance renews at <paramref name="at"/>.</summary>
    public static RestrictionRecovery ResetAt(DateTimeOffset at) => new(RecoveryKind.ResetAt, at);

    /// <summary>No new attempt before <paramref name="at"/>.</summary>
    public static RestrictionRecovery RetryNotBefore(DateTimeOffset at) => new(RecoveryKind.RetryNotBefore, at);
}

/// <summary>What a failure restricts: a fact for a host to hold against an account, a model or an endpoint.</summary>
/// <param name="Cause">What ran out.</param>
/// <param name="Subject">What it holds back.</param>
/// <param name="Recovery">How it ends.</param>
public sealed record AgentRestriction(RestrictionCause Cause, RestrictionSubject Subject, RestrictionRecovery Recovery);

/// <summary>Whether the turn's prompt reached the agent — the fact a retry has to know. It is independent of
/// <see cref="AgentFailureKind"/>: a limit that arrives after the door answered is no proof the prompt was
/// refused before it was consumed.</summary>
public enum AgentDelivery
{
    /// <summary>A tool step had started, so the turn may have acted before it failed — or the door cannot tell.
    /// The default.</summary>
    Uncertain = 0,

    /// <summary>Nothing came back from the door before the failure, so nothing shows the prompt was read.</summary>
    NeverSent,

    /// <summary>The door answered (a session start, text or thinking) and had run no tool step.</summary>
    Taken,
}

/// <summary>One failed turn, as the door's own fields and words read it. <see cref="SessionEnded.Failure"/>
/// carries it on every failed terminal the two CLI doors emit.</summary>
/// <param name="Kind">What kind of failure it was. The order of reading is limit, then access, then
/// transient; anything unread is <see cref="AgentFailureKind.Fatal"/>.</param>
/// <param name="By">The field or sentence entry that decided it, for a log or a test to name.</param>
public sealed record AgentFailure(AgentFailureKind Kind, string By)
{
    /// <summary>What the failure holds back, when it says. Null for a failure that restricts nothing.</summary>
    public AgentRestriction? Restriction { get; init; }

    /// <summary>Whether the prompt was delivered. The door's session loop sets it from what the turn showed
    /// before it failed; a reader that builds a failure of its own leaves the default.</summary>
    public AgentDelivery Delivery { get; init; }
}
