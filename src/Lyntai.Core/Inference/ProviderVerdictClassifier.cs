using System.Net;
using System.Text.RegularExpressions;

namespace Lyntai.Inference;

/// <summary>
/// The ONE place failure text/exceptions are classified into verdicts. The verdict taxonomy drives
/// router-wide behavior (RateLimited cools the host and advances; Refused surfaces with no fallback),
/// so every adapter must share this instead of hand-rolling substring heuristics that drift.
/// </summary>
public static partial class ProviderVerdictClassifier
{
    /// <summary>Classify an error message / stderr tail. Conservative on purpose: "429" alone is NOT
    /// enough (a stack frame like <c>cli.js:429</c> must stay Failed) — it needs rate-limit phrasing,
    /// or an HTTP-ish context word immediately before the number.</summary>
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
    /// PROPAGATES out of <c>ILlmClient.CompleteAsync</c>/<c>StreamAsync</c> and aborts the whole routing
    /// attempt — the remaining candidates included. This deliberately differs from
    /// <see cref="Lyntai.Llm.IRefusalMatcher"/>, whose seam logs a throwing matcher and fails open: this class is static
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
    /// <para>Why not simply require a key up front: an OpenAI-COMPATIBLE endpoint run locally (LM Studio,
    /// vLLM, Ollama) legitimately needs none, so "no key" cannot mean unconfigured on its own — only "no key
    /// AND the server demanded one" does.</para>
    /// <para>The generation domain states the SAME rule over its own vocabulary
    /// (<c>Lyntai.Inference.ProviderVerdictClassifier.FromHttpFailure</c>). The two are deliberately
    /// stated separately rather than shared: the pattern CORPUS ("what does a 429 look like") is single-sourced
    /// here, but this two-term promotion reaches different populations — every LLM backend that authenticates
    /// by login SESSION rather than a supplied key (the CLI dialects) classifies from error text and has no
    /// <paramref name="hasCredentials"/> fact at all. Change one and check the other.</para></summary>
    /// <param name="status">The failed response's status.</param>
    /// <param name="body">The response body, for text-based classification.</param>
    /// <param name="hasCredentials">Whether this call actually carried credentials.</param>
    public static ProviderVerdict FromHttpFailure(HttpStatusCode status, string? body, bool hasCredentials)
    {
        var verdict = FromHttpFailure(status, body);
        return verdict == ProviderVerdict.AuthFailed && !hasCredentials ? ProviderVerdict.NotConfigured : verdict;
    }

    [GeneratedRegex(@"rate[\s_-]?limit|too\s+many\s+requests|quota\s+exceeded|resource[\s_-]?exhausted|(?:http|status(?:\s+code)?|error|code)\s*[:=]?\s*429\b", RegexOptions.IgnoreCase)]
    private static partial Regex RateLimitPattern();

    [GeneratedRegex(@"context[\s_-]?(?:window|length)|maximum\s+context|context_length_exceeded|prompt\s+is\s+too\s+long|input\s+is\s+too\s+long|too\s+many\s+(?:input\s+)?tokens|exceeds\s+the\s+.{0,20}token", RegexOptions.IgnoreCase)]
    private static partial Regex ContextWindowPattern();

    // "unauthorized" alone is NOT enough (e.g. "user is unauthorized to read file X" is a tool/
    // permission message, not a provider-auth failure) — since AuthFailed now cools the host, it needs
    // auth context (a key/token/credential/401 nearby), mirroring how the rate-limit rule guards 429.
    [GeneratedRegex(@"(?:invalid|incorrect|missing|expired)\s+(?:api[\s_-]?key|token|credential)|api[\s_-]?key\s+(?:not\s+)?(?:valid|found|provided)|authentication\s+(?:failed|error|required)|401\s+unauthorized|(?:api[\s_-]?key|token|credential|access|auth\w*)[\s\S]{0,40}unauthorized|unauthorized[\s\S]{0,40}(?:api[\s_-]?key|token|credential|access|client)|(?:http|status(?:\s+code)?|error|code)\s*[:=]?\s*40[13]\b", RegexOptions.IgnoreCase)]
    private static partial Regex AuthPattern();

    [GeneratedRegex(@"content[\s_-]?(?:filter|policy)|policy\s+violation", RegexOptions.IgnoreCase)]
    private static partial Regex RefusalPattern();

    /// <summary>Classify a THROWN exception for a router, which is <see cref="FromException"/> with one
    /// extra rule: never <see cref="ProviderVerdict.Refused"/>.
    ///
    /// <para>A refusal is a judgement the backend made about the CONTENT and returned in a reply; an
    /// exception is the transport failing. Letting a thrown exception classify as Refused would surface it
    /// with no fallback, so a transport fault that happens to carry policy-shaped words would end the whole
    /// run. Both routers applied this rule from their own private copies until <b>D136</b> merged the
    /// taxonomy that had kept them apart.</para></summary>
    internal static ProviderVerdict FromThrown(Exception ex)
    {
        var verdict = FromException(ex);
        return verdict == ProviderVerdict.Refused ? ProviderVerdict.Failed : verdict;
    }
}
