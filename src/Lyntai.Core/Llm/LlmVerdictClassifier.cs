using System.Net;
using System.Text.RegularExpressions;

namespace Lyntai.Llm;

/// <summary>
/// The ONE place failure text/exceptions are classified into verdicts. The verdict taxonomy drives
/// router-wide behavior (RateLimited cools the host and advances; Refused surfaces with no fallback),
/// so every adapter must share this instead of hand-rolling substring heuristics that drift.
/// </summary>
public static partial class LlmVerdictClassifier
{
    /// <summary>Classify an error message / stderr tail. Conservative on purpose: "429" alone is NOT
    /// enough (a stack frame like <c>cli.js:429</c> must stay Failed) — it needs rate-limit phrasing,
    /// or an HTTP-ish context word immediately before the number.</summary>
    // consumer-registered matchers, consulted BEFORE the built-in (English) patterns — so an app can teach
    // the classifier a non-English provider's phrasing, or a bespoke error code, without editing Core. A
    // process-wide extension point (set at startup); AddErrorTextMatcher returns an IDisposable so a test
    // can scope its registration.
    private static readonly Lock _matchersLock = new();
    private static readonly List<Func<string, LlmVerdict?>> _customMatchers = [];

    /// <summary>Register a custom error-text matcher (returns a verdict, or null to defer). Consulted before
    /// the built-in patterns, first non-null wins. Dispose the returned handle to unregister (an app
    /// registers once at startup and never disposes; a test disposes to isolate).</summary>
    public static IDisposable AddErrorTextMatcher(Func<string, LlmVerdict?> matcher)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        lock (_matchersLock) _customMatchers.Add(matcher);
        return new MatcherRegistration(matcher);
    }

    public static LlmVerdict FromErrorText(string? text, LlmVerdict fallback = LlmVerdict.Failed)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;

        Func<string, LlmVerdict?>[] custom;
        lock (_matchersLock) custom = [.. _customMatchers];
        foreach (var matcher in custom)
            if (matcher(text) is { } verdict) return verdict; // consumer patterns win over the built-ins

        if (RateLimitPattern().IsMatch(text)) return LlmVerdict.RateLimited;
        if (ContextWindowPattern().IsMatch(text)) return LlmVerdict.ContextWindowExceeded;
        if (AuthPattern().IsMatch(text)) return LlmVerdict.AuthFailed;
        if (RefusalPattern().IsMatch(text)) return LlmVerdict.Refused;
        return fallback;
    }

    private sealed class MatcherRegistration(Func<string, LlmVerdict?> matcher) : IDisposable
    {
        public void Dispose() { lock (_matchersLock) _customMatchers.Remove(matcher); }
    }

    /// <summary>Classify a caught exception: the typed HTTP status wins over message heuristics
    /// (a 429 surfaced as "Too Many Requests" carries no "429" text at all).</summary>
    public static LlmVerdict FromException(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => LlmVerdict.RateLimited,
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } => LlmVerdict.AuthFailed,
        OperationCanceledException => LlmVerdict.Timeout,
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
    public static LlmVerdict FromHttpFailure(HttpStatusCode status, string? body) => status switch
    {
        HttpStatusCode.TooManyRequests => LlmVerdict.RateLimited,
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => LlmVerdict.AuthFailed,
        _ => FromErrorText(body),
    };

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
}
