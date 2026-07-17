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
    public static LlmVerdict FromErrorText(string? text, LlmVerdict fallback = LlmVerdict.Failed)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        if (RateLimitPattern().IsMatch(text)) return LlmVerdict.RateLimited;
        if (RefusalPattern().IsMatch(text)) return LlmVerdict.Refused;
        return fallback;
    }

    /// <summary>Classify a caught exception: the typed HTTP status wins over message heuristics
    /// (a 429 surfaced as "Too Many Requests" carries no "429" text at all).</summary>
    public static LlmVerdict FromException(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => LlmVerdict.RateLimited,
        OperationCanceledException => LlmVerdict.Timeout,
        _ => FromErrorText(ex.Message),
    };

    [GeneratedRegex(@"rate[\s_-]?limit|too\s+many\s+requests|quota\s+exceeded|resource[\s_-]?exhausted|(?:http|status(?:\s+code)?|error|code)\s*[:=]?\s*429\b", RegexOptions.IgnoreCase)]
    private static partial Regex RateLimitPattern();

    [GeneratedRegex(@"content[\s_-]?(?:filter|policy)|policy\s+violation", RegexOptions.IgnoreCase)]
    private static partial Regex RefusalPattern();
}
