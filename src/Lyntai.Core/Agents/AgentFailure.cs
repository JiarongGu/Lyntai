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

/// <summary>Which action a person must take when a restriction ends only by one
/// (<see cref="RecoveryKind.OwnerAction"/>). A reader sets it only from a field the door defines, never from words.</summary>
public enum OwnerActionReason
{
    /// <summary>No field said which action. The default.</summary>
    Unknown = 0,

    /// <summary>Add credit or raise a spend cap (a billing failure).</summary>
    Refill,

    /// <summary>Sign in again.</summary>
    SignIn,

    /// <summary>Grant a permission the agent or account lacks.</summary>
    Grant,

    /// <summary>Change a configuration, such as a model the agent refused.</summary>
    Configure,
}

/// <summary>How a restriction's end was written.</summary>
public enum StatedTimeShape
{
    /// <summary>An instant, as a field states one (a frame's Unix seconds, a <c>Retry-After</c> date).</summary>
    Moment = 1,

    /// <summary>A date and a time (<c>Oct 3rd, 2026 4:05 PM</c>).</summary>
    DateAndTime,

    /// <summary>A weekday and a time (<c>Mon 12:00am</c>).</summary>
    WeekdayAndTime,

    /// <summary>A time of day alone (<c>3:45pm</c>), whose day the sentence leaves to the reader.</summary>
    TimeOfDay,

    /// <summary>A wait (<c>retry after 60s</c>, a <c>Retry-After</c> in seconds).</summary>
    Wait,
}

/// <summary>What a failure stated of when its restriction ends, apart from the library's resolution of it
/// (<see cref="RestrictionRecovery.At"/>). A host with its own policy for a time that has passed, or its own naming of a
/// window, applies it to these parts. Only the parts the <see cref="Shape"/> writes are set.</summary>
/// <param name="Shape">How it was written.</param>
public sealed record StatedRecoveryParts(StatedTimeShape Shape)
{
    /// <summary>The instant, for <see cref="StatedTimeShape.Moment"/>.</summary>
    public DateTimeOffset? Moment { get; init; }

    /// <summary>The date, for <see cref="StatedTimeShape.DateAndTime"/>.</summary>
    public DateOnly? Date { get; init; }

    /// <summary>The weekday, for <see cref="StatedTimeShape.WeekdayAndTime"/>.</summary>
    public DayOfWeek? Weekday { get; init; }

    /// <summary>The time of day, for every shape that writes one.</summary>
    public TimeOnly? Time { get; init; }

    /// <summary>The zone of that time: the name the sentence gave, else the clock's local zone, which
    /// <see cref="ZoneNamed"/> tells apart. Null for a moment and a wait.</summary>
    public string? Zone { get; init; }

    /// <summary>True when the sentence named <see cref="Zone"/>; false when the clock's local zone stood in for none.</summary>
    public bool ZoneNamed { get; init; }

    /// <summary>The wait, for <see cref="StatedTimeShape.Wait"/>.</summary>
    public TimeSpan? Wait { get; init; }
}

/// <summary>A restriction's way out: a stated moment, or the absence of one.</summary>
/// <param name="Kind">How it ends.</param>
/// <param name="At">The moment, for <see cref="RecoveryKind.ResetAt"/> and <see cref="RecoveryKind.RetryNotBefore"/>;
/// null otherwise.</param>
public sealed record RestrictionRecovery(RecoveryKind Kind, DateTimeOffset? At = null)
{
    /// <summary>What the failure stated, beside <see cref="At"/>, which is the library's resolution of it under its one
    /// rule (the next occurrence, a minute of skew allowed). A stated zone the machine lacks leaves the kind
    /// <see cref="RecoveryKind.Unknown"/> with these parts kept. Null where nothing was stated.</summary>
    public StatedRecoveryParts? Stated { get; init; }

    /// <summary>Which action the person must take, for <see cref="RecoveryKind.OwnerAction"/>;
    /// <see cref="OwnerActionReason.Unknown"/> for every other kind and where no field said.</summary>
    public OwnerActionReason Reason { get; init; }

    /// <summary>Nothing was stated.</summary>
    public static RestrictionRecovery Unknown { get; } = new(RecoveryKind.Unknown);

    /// <summary>A person must act, and which action is not said.</summary>
    public static RestrictionRecovery OwnerAction { get; } = new(RecoveryKind.OwnerAction);

    /// <summary>A person must take the action <paramref name="reason"/> names.</summary>
    public static RestrictionRecovery OwnerActionFor(OwnerActionReason reason) => new(RecoveryKind.OwnerAction) { Reason = reason };

    /// <summary>The allowance renews at <paramref name="at"/>.</summary>
    public static RestrictionRecovery ResetAt(DateTimeOffset at) => new(RecoveryKind.ResetAt, at);

    /// <summary>No new attempt before <paramref name="at"/>.</summary>
    public static RestrictionRecovery RetryNotBefore(DateTimeOffset at) => new(RecoveryKind.RetryNotBefore, at);
}

/// <summary>What a failure restricts: a fact for a host to hold against an account, a model or an endpoint.</summary>
/// <param name="Cause">What ran out.</param>
/// <param name="Subject">What it holds back.</param>
/// <param name="Recovery">How it ends.</param>
public sealed record AgentRestriction(RestrictionCause Cause, RestrictionSubject Subject, RestrictionRecovery Recovery)
{
    /// <summary>The window the door named, in its own words (<c>five_hour</c>, <c>seven_day</c>); null where it named none.</summary>
    public string? Window { get; init; }

    /// <summary>The scope the door named, in its own words (a <c>limitScope</c>); null where it named none.</summary>
    public string? Scope { get; init; }
}

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

    /// <summary>The door answered the prompt (text, thinking, usage or the agent's own reply to it, and not a session
    /// start the door announced before it wrote the prompt) and had run no tool step.</summary>
    Taken,
}

/// <summary>What an access failure asks of the person.</summary>
public enum AccessReason
{
    /// <summary>The account must sign in again.</summary>
    SignInAgain = 1,

    /// <summary>The service refused the credential presented.</summary>
    CredentialRefused,
}

/// <summary>A state of the world a door states beside a failure, read from a field the door defines and never from words. It rides
/// on <see cref="AgentFailure.Conflict"/> beside the kind, which stays what the fields and words decided, so a host that reads only
/// <see cref="AgentFailure.Kind"/> sees what it saw before.</summary>
public enum AgentConflict
{
    /// <summary>No conflict was stated. The default.</summary>
    None = 0,

    /// <summary>Another process holds the conversation and the agent will not open it for this one, so the open was refused
    /// before the prompt. A later attempt may find it free and nothing in the door says when; the library never retries it, since
    /// resuming over a live writer is the host's decision.</summary>
    ConversationHeld,
}

/// <summary>The two readings of an access failure, kept apart: there is no winner and no rule choosing between
/// them, since a field and a sentence disagree in practice and either may be the right one for a host. Each is
/// null when its source said nothing about which.</summary>
/// <param name="ByField">What a field the door defines stated: an HTTP 401 is a refused credential, the protocol's
/// own authentication-required code a sign-in again.</param>
/// <param name="ByWords">What the door's words implied.</param>
public sealed record AgentAccessReadings(AccessReason? ByField, AccessReason? ByWords);

/// <summary>One failed turn, as the door's own fields and words read it. <see cref="SessionEnded.Failure"/>
/// carries it on every failed terminal the two CLI doors emit.</summary>
/// <param name="Kind">What kind of failure it was. The order of reading is limit, then access, then
/// transient; anything unread is <see cref="AgentFailureKind.Fatal"/>.</param>
/// <param name="By">The field or sentence entry that decided it, for a log or a test to name.</param>
public sealed record AgentFailure(AgentFailureKind Kind, string By)
{
    /// <summary>What the failure holds back, when it says. Null for a failure that restricts nothing. A
    /// <see cref="AgentFailureKind.Fatal"/> one carries a restriction only for a model the agent refused
    /// (<see cref="RestrictionSubject.Model"/>, <see cref="RecoveryKind.OwnerAction"/>): a pool holds that account and
    /// model and nothing wider until the host clears it.</summary>
    public AgentRestriction? Restriction { get; init; }

    /// <summary>For an <see cref="AgentFailureKind.Access"/> failure, what a field and the words each said of it;
    /// null for every other kind. <see cref="Kind"/> stays what the fields decided.</summary>
    public AgentAccessReadings? AccessReadings { get; init; }

    /// <summary>A state of the world the door stated beside the failure; <see cref="AgentConflict.None"/> where it stated none.
    /// <see cref="Kind"/> is unchanged by it (a held conversation is <see cref="AgentFailureKind.Fatal"/> to the retry decorator and
    /// holds nothing in the pool), so a host that resumes a conversation reads this member and not the kind.</summary>
    public AgentConflict Conflict { get; init; }

    /// <summary>Whether the prompt was delivered. The door's session loop sets it from what the turn showed
    /// before it failed; a reader that builds a failure of its own leaves the default.</summary>
    public AgentDelivery Delivery { get; init; }
}
