using System.Net;
using System.Text.RegularExpressions;
using Lyntai.Agents;

namespace Lyntai.Inference;

/// <summary>
/// The ONE place failure text/exceptions are classified into verdicts. The verdict taxonomy drives
/// router-wide behavior (RateLimited cools the host and advances; Refused surfaces with no fallback),
/// so every adapter must share this instead of hand-rolling substring heuristics that drift.
/// </summary>
public static partial class ProviderVerdictClassifier
{
    // consumer-registered matchers, consulted BEFORE the built-in (English) patterns — so an app can teach
    // the classifier a non-English provider's phrasing, or a bespoke error code, without editing Core. A
    // process-wide extension point (set at startup); AddErrorTextMatcher returns an IDisposable so a test
    // can scope its registration.
    private static readonly Lock _matchersLock = new();
    private static readonly List<Func<string, ProviderVerdict?>> _customMatchers = [];
    // copy-on-write snapshot rebuilt on register/unregister (rare) so the classify path — which runs on
    // EVERY failure — reads it lock-free and allocation-free instead of lock+copy per call
    private static volatile Func<string, ProviderVerdict?>[] _matcherSnapshot = [];

    /// <summary>Register a custom error-text matcher (returns a verdict, or null to defer). Consulted before
    /// the built-in patterns, first non-null wins. Dispose the returned handle to unregister (an app
    /// registers once at startup and never disposes; a test disposes to isolate).
    /// <para>A matcher MUST NOT throw. Classification runs inside the router's own <c>catch</c>, so a throw
    /// PROPAGATES out of <c>ITextClient.CompleteAsync</c>/<c>StreamAsync</c> and aborts the whole routing
    /// attempt — the remaining candidates included. This deliberately differs from
    /// <see cref="Lyntai.Inference.IRefusalMatcher"/>, whose seam logs a throwing matcher and fails open: this class is static
    /// and has no logger, so swallowing here would hide the bug rather than report it.</para></summary>
    public static IDisposable AddErrorTextMatcher(Func<string, ProviderVerdict?> matcher)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        lock (_matchersLock)
        {
            _customMatchers.Add(matcher);
            _matcherSnapshot = [.. _customMatchers];
        }
        return new MatcherRegistration(matcher);
    }

    /// <summary>Classify an error message / stderr tail. Conservative on purpose: "429" alone is NOT
    /// enough (a stack frame like <c>cli.js:429</c> must stay Failed) — it needs rate-limit phrasing,
    /// or an HTTP-ish context word immediately before the number.</summary>
    /// <param name="text">The backend's words.</param>
    /// <param name="fallback">What unrecognized text classifies as.</param>
    public static ProviderVerdict FromErrorText(string? text, ProviderVerdict fallback = ProviderVerdict.Failed)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;

        foreach (var matcher in _matcherSnapshot)
            if (matcher(text) is { } verdict) return verdict; // consumer patterns win over the built-ins

        if (RateLimitPattern().IsMatch(text)) return ProviderVerdict.RateLimited;
        if (ContextWindowPattern().IsMatch(text)) return ProviderVerdict.ContextWindowExceeded;
        if (AuthPattern().IsMatch(text)) return ProviderVerdict.AuthFailed;
        if (RefusalPattern().IsMatch(text)) return ProviderVerdict.Refused;
        return fallback;
    }

    /// <summary>Read an agent door's own failure SENTENCE as a failure: the kind, and what it restricts when the
    /// words say. Order is limit, then access, then transient; null when no entry reads it, which a caller takes
    /// as <see cref="AgentFailureKind.Fatal"/>. Call it only with words the DOOR reported (an error field, a
    /// failed turn's message) — an agent's own message or a tool's output is never evidence of a failure.
    /// <para>The verdict is the same table's: <see cref="FromAgentFailure"/> maps the kinds back, and
    /// <see cref="FromErrorText(string, ProviderVerdict)"/> agrees with it on every sentence it reads. A recovery
    /// is stated by the words or it is <see cref="RecoveryKind.Unknown"/>; it is never defaulted.</para></summary>
    /// <param name="text">The door's words.</param>
    /// <param name="time">The clock a time written without a date or a zone is read against, in its local zone;
    /// null reads the system's.</param>
    public static AgentFailure? ReadAgentFailure(string? text, TimeProvider? time = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        time ??= TimeProvider.System;

        foreach (var matcher in _matcherSnapshot)
        {
            if (matcher(text) is not { } verdict) continue;
            return verdict switch
            {
                ProviderVerdict.RateLimited => new AgentFailure(AgentFailureKind.Limit, "sentence:custom-matcher")
                    { Restriction = new(RestrictionCause.Unknown, RestrictionSubject.Unknown, RestrictionRecovery.Unknown) },
                ProviderVerdict.AuthFailed or ProviderVerdict.NotConfigured => new AgentFailure(AgentFailureKind.Access, "sentence:custom-matcher"),
                _ => null,
            };
        }

        if (RateLimitPattern().IsMatch(text)) return ReadLimit(text, time);
        if (ContextWindowPattern().IsMatch(text)) return null;
        if (AuthPattern().IsMatch(text)) return new AgentFailure(AgentFailureKind.Access, "sentence:auth");
        return ReadTransient(text);
    }

    /// <summary>The verdict a failure maps onto — no new member, so the router's actions stand: a limit is
    /// <see cref="ProviderVerdict.RateLimited"/>, an access failure <see cref="ProviderVerdict.AuthFailed"/>, a
    /// transient one <see cref="ProviderVerdict.Failed"/>, and a fatal one what <see cref="FromErrorText(string, ProviderVerdict)"/>
    /// says of <paramref name="text"/> when that is a refusal or an over-long input, else <c>Failed</c>.</summary>
    /// <param name="failure">The failure.</param>
    /// <param name="text">The door's words, which only a fatal failure reads.</param>
    public static ProviderVerdict FromAgentFailure(AgentFailure failure, string? text = null)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return failure.Kind switch
        {
            AgentFailureKind.Limit => ProviderVerdict.RateLimited,
            AgentFailureKind.Access => ProviderVerdict.AuthFailed,
            AgentFailureKind.Transient => ProviderVerdict.Failed,
            _ => FatalVerdict(FromErrorText(text)),
        };

        static ProviderVerdict FatalVerdict(ProviderVerdict read) =>
            read is ProviderVerdict.Refused or ProviderVerdict.ContextWindowExceeded ? read : ProviderVerdict.Failed;
    }

    private static AgentFailure ReadLimit(string text, TimeProvider time)
    {
        var credit = CreditPattern().IsMatch(text);
        var cause = credit ? RestrictionCause.Credit
            : ConcurrencyPattern().IsMatch(text) ? RestrictionCause.Concurrency
            : QuotaPattern().IsMatch(text) ? RestrictionCause.Quota
            : RestrictionCause.Rate;
        var subject = SharedBudgetPattern().IsMatch(text) ? RestrictionSubject.Pool
            : ModelLimitPattern().IsMatch(text) ? RestrictionSubject.Model
            : cause is RestrictionCause.Credit or RestrictionCause.Quota ? RestrictionSubject.Account
            : RestrictionSubject.Unknown;
        var recovery = credit ? RestrictionRecovery.OwnerAction : StatedRecovery.Read(text, time) ?? RestrictionRecovery.Unknown;

        return new AgentFailure(AgentFailureKind.Limit, $"sentence:limit-{cause.ToString().ToLowerInvariant()}")
        {
            Restriction = new AgentRestriction(cause, subject, recovery),
        };
    }

    private static AgentFailure? ReadTransient(string text)
    {
        if (CapacityPattern().IsMatch(text))
            return new AgentFailure(AgentFailureKind.Transient, "sentence:model-capacity")
            {
                Restriction = new(RestrictionCause.Capacity, RestrictionSubject.Model, RestrictionRecovery.Unknown),
            };
        if (OverloadedPattern().IsMatch(text))
            return new AgentFailure(AgentFailureKind.Transient, "sentence:overloaded")
            {
                Restriction = new(RestrictionCause.Capacity, RestrictionSubject.Endpoint, RestrictionRecovery.Unknown),
            };
        return UnavailablePattern().IsMatch(text) ? new AgentFailure(AgentFailureKind.Transient, "sentence:unavailable") : null;
    }

    private sealed class MatcherRegistration(Func<string, ProviderVerdict?> matcher) : IDisposable
    {
        public void Dispose()
        {
            lock (_matchersLock)
            {
                _customMatchers.Remove(matcher);
                _matcherSnapshot = [.. _customMatchers];
            }
        }
    }

    /// <summary>Classify a caught exception: the typed HTTP status wins over message heuristics
    /// (a 429 surfaced as "Too Many Requests" carries no "429" text at all).</summary>
    public static ProviderVerdict FromException(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => ProviderVerdict.RateLimited,
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } => ProviderVerdict.AuthFailed,
        OperationCanceledException => ProviderVerdict.Timeout,
        // scan the FULL inner-exception chain, not just ex.Message — a typed provider exception (e.g. an
        // MEAI "prompt too long") often wraps the real context-window/rate-limit detail in an inner
        // exception, so classifying only the outer message would flatten it to Failed.
        _ => FromErrorText(FullMessage(ex)),
    };

    private static string FullMessage(Exception ex)
    {
        var parts = new List<string>();
        for (Exception? e = ex; e is not null; e = e.InnerException) parts.Add(e.Message);
        return string.Join(" | ", parts);
    }

    /// <summary>Classify a failed HTTP status + response body (typed status wins over body text).</summary>
    public static ProviderVerdict FromHttpFailure(HttpStatusCode status, string? body) => status switch
    {
        HttpStatusCode.TooManyRequests => ProviderVerdict.RateLimited,
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ProviderVerdict.AuthFailed,
        _ => FromErrorText(body),
    };

    /// <summary>Classify a failed HTTP response, distinguishing "no credentials were SUPPLIED" from "the
    /// credentials were REJECTED". An auth failure with nothing to authenticate with is
    /// <see cref="ProviderVerdict.NotConfigured"/>, not <see cref="ProviderVerdict.AuthFailed"/> — and the difference
    /// is not cosmetic, because routing acts on it: NotConfigured skips the candidate blamelessly and lets a
    /// host offer setup, whereas AuthFailed BENCHES the provider for the cooldown window. A backend a
    /// consumer merely listed without configuring would otherwise be penalised on every first attempt.
    /// <para>Why not simply require a key up front: an OpenAI-shaped endpoint run locally (LM Studio,
    /// vLLM, Ollama) legitimately needs none, so "no key" cannot mean unconfigured on its own — only "no key
    /// AND the server demanded one" does.</para></summary>
    /// <param name="status">The failed response's status.</param>
    /// <param name="body">The response body, for text-based classification.</param>
    /// <param name="hasCredentials">Whether this call actually carried credentials.</param>
    public static ProviderVerdict FromHttpFailure(HttpStatusCode status, string? body, bool hasCredentials) =>
        Credentialed(FromHttpFailure(status, body), hasCredentials);

    /// <summary>Classify an error a backend reported IN BAND — under a 2xx, where the status says nothing — with
    /// the same credentials rule as <see cref="FromHttpFailure(HttpStatusCode, string, bool)"/>: an auth failure
    /// on a call that carried no credentials is <see cref="ProviderVerdict.NotConfigured"/>.</summary>
    /// <param name="text">The backend's words.</param>
    /// <param name="hasCredentials">Whether this call actually carried credentials.</param>
    public static ProviderVerdict FromErrorText(string? text, bool hasCredentials) =>
        Credentialed(FromErrorText(text), hasCredentials);

    private static ProviderVerdict Credentialed(ProviderVerdict verdict, bool hasCredentials) =>
        verdict == ProviderVerdict.AuthFailed && !hasCredentials ? ProviderVerdict.NotConfigured : verdict;

    // "out of credits" is an allowance spent until a person refills it (Codex: "Your workspace is out of
    // credits. Ask your workspace owner to refill…"), named with no reset time and no 429 — the same remedy
    // as a quota: bench this account and advance
    // …and so is a usage limit a vendor names in its own words ("You've hit your usage limit", "You've hit your
    // weekly limit · resets Mon 12:00am", a spend cap, a team's shared budget)
    [GeneratedRegex(@"rate[\s_-]?limit|too\s+many\s+requests|quota\s+exceeded|resource[\s_-]?exhausted|out\s+of\s+credits|usage\s+limit|spend\s+cap|hit\s+your\s+(?:[\w'’]+\s+){0,2}(?:limit|budget)|(?:http|status(?:\s+code)?|error|code)\s*[:=]?\s*429\b", RegexOptions.IgnoreCase)]
    private static partial Regex RateLimitPattern();

    // what a limit sentence says ran out. Credits and a spend cap wait for a person; the rest are an allowance
    [GeneratedRegex(@"out\s+of\s+(?:usage\s+)?credits|spend\s+cap", RegexOptions.IgnoreCase)]
    private static partial Regex CreditPattern();

    [GeneratedRegex(@"quota|resource[\s_-]?exhausted|usage\s+limit|hit\s+your\s+(?:[\w'’]+\s+){0,2}(?:limit|budget)", RegexOptions.IgnoreCase)]
    private static partial Regex QuotaPattern();

    [GeneratedRegex(@"concurren", RegexOptions.IgnoreCase)]
    private static partial Regex ConcurrencyPattern();

    [GeneratedRegex(@"shared\s+budget", RegexOptions.IgnoreCase)]
    private static partial Regex SharedBudgetPattern();

    // "limit for gpt-x" (Codex's per-model limit) and "your Opus limit" (Claude Code's): one model's, so another
    // model on the same account is still available
    [GeneratedRegex(@"limit\s+for\s+\S|your\s+(?:opus|sonnet|haiku)\s+limit", RegexOptions.IgnoreCase)]
    private static partial Regex ModelLimitPattern();

    // a MODEL at capacity is the model's, never the account's (Codex: "Selected model is at capacity…")
    [GeneratedRegex(@"model\s+is\s+at\s+capacity", RegexOptions.IgnoreCase)]
    private static partial Regex CapacityPattern();

    // a 529 needs its context word like a 429 does ("processed 529 records" and "cli.js:529:17" stay unread);
    // "overloaded" alone is left out because a method can be overloaded
    [GeneratedRegex(@"overloaded_error|\b529\s+overloaded|api\s+error:?\s*529|(?:http|status(?:\s+code)?|error|code)\s*[:=]?\s*529\b|(?:servers?|service|api)\s+(?:is|are)\s+(?:currently\s+)?overloaded", RegexOptions.IgnoreCase)]
    private static partial Regex OverloadedPattern();

    [GeneratedRegex(@"(?:http|status(?:\s+code)?|error|code)\s*[:=]?\s*50[234]\b|service\s+(?:is\s+)?(?:temporarily\s+)?unavailable|bad\s+gateway|gateway\s+time-?out", RegexOptions.IgnoreCase)]
    private static partial Regex UnavailablePattern();

    // "physical batch size" is llama.cpp refusing an input past its batch, as HTTP 500; "too large to process"
    // alone is left out, because an upload refused for its size says it too
    [GeneratedRegex(@"context[\s_-]?(?:window|length)|max(?:imum)?\s+context|context_length_exceeded|prompt\s+is\s+too\s+long|input\s+is\s+too\s+long|too\s+many\s+(?:input\s+)?tokens|exceeds\s+the\s+.{0,20}token|physical\s+batch\s+size", RegexOptions.IgnoreCase)]
    private static partial Regex ContextWindowPattern();

    // "unauthorized" alone is NOT enough (e.g. "user is unauthorized to read file X" is a tool/
    // permission message, not a provider-auth failure) — since AuthFailed now cools the host, it needs
    // auth context (a key/token/credential/401 nearby), mirroring how the rate-limit rule guards 429.
    [GeneratedRegex(@"(?:invalid|incorrect|missing|expired)\s+(?:api[\s_-]?key|token|credential)|api[\s_-]?key\s+(?:not\s+)?(?:valid|found|provided)|authentication\s+(?:failed|error|required)|401\s+unauthorized|(?:api[\s_-]?key|token|credential|access|auth\w*)[\s\S]{0,40}unauthorized|unauthorized[\s\S]{0,40}(?:api[\s_-]?key|token|credential|access|client)|(?:http|status(?:\s+code)?|error|code)\s*[:=]?\s*40[13]\b", RegexOptions.IgnoreCase)]
    private static partial Regex AuthPattern();

    [GeneratedRegex(@"content[\s_-]?(?:filter|policy)|policy\s+violation", RegexOptions.IgnoreCase)]
    private static partial Regex RefusalPattern();

    /// <summary>Classify a THROWN exception the way every router does, which is <see cref="FromException"/> with
    /// one extra rule: never <see cref="ProviderVerdict.Refused"/>. A refusal is a judgement the backend made
    /// about the CONTENT and returned in a reply; an exception is the transport failing, and classifying one as
    /// Refused would surface it with no fallback — so a proxy page that happens to mention a content policy
    /// would end the whole run. A backend catching its own exceptions classifies them with this.</summary>
    /// <param name="ex">What was thrown.</param>
    public static ProviderVerdict FromThrown(Exception ex)
    {
        var verdict = FromException(ex);
        return verdict == ProviderVerdict.Refused ? ProviderVerdict.Failed : verdict;
    }

    /// <summary>Whether a thrown exception proves the request never left this process — a refused connection, a
    /// name that would not resolve, a TLS handshake that never completed — so nothing was submitted and nothing
    /// charged. Deliberately NOT a catch-all: a timeout, a protocol error mid-response, or anything unrecognised
    /// may have been delivered, and the expensive mistake is assuming it was not. Read only for a SUBMIT, where
    /// asking is what commits the money (<see cref="QueuedOperation.FromThrownSubmit"/>).</summary>
    internal static bool NeverReachedTheBackend(Exception ex) => ex switch
    {
        HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError
                                              or HttpRequestError.NameResolutionError
                                              or HttpRequestError.SecureConnectionError } => true,
        System.Net.Sockets.SocketException => true,
        _ => false,
    };
}
