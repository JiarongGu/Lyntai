using System.Net;
using Lyntai.Llm;

namespace Lyntai.Generation;

/// <summary>Turns a transport failure or an error message into a <see cref="GenerationVerdict"/>.
///
/// It DELEGATES the pattern matching to Core's <see cref="LlmVerdictClassifier"/> and translates the answer
/// into media's vocabulary. That is the deliberate middle path between the two bad options: media does not
/// adopt LLM-named types (it is a separate domain), and it does not carry a second copy of the "what does a
/// 429 / a content-policy refusal look like" corpus, which would drift out of sync
/// (<c>docs/DECISIONS.md</c> D27). Consumer-registered matchers on the shared classifier therefore teach BOTH
/// domains at once.</summary>
public static class GenerationVerdictClassifier
{
    /// <summary>Classify a failed HTTP response (typed status wins over body text).</summary>
    public static GenerationVerdict FromHttpFailure(HttpStatusCode status, string? body) =>
        Translate(LlmVerdictClassifier.FromHttpFailure(status, body));

    /// <summary>Classify an error message / response body.</summary>
    public static GenerationVerdict FromErrorText(string? text) =>
        Translate(LlmVerdictClassifier.FromErrorText(text));

    /// <summary>Classify a caught exception.</summary>
    public static GenerationVerdict FromException(Exception ex) =>
        Translate(LlmVerdictClassifier.FromException(ex));

    /// <summary>Map the shared taxonomy onto media's. Verdicts with no media meaning
    /// (<see cref="LlmVerdict.ContextWindowExceeded"/>) collapse to <see cref="GenerationVerdict.Failed"/> rather
    /// than being surfaced as something a media caller cannot act on.</summary>
    private static GenerationVerdict Translate(LlmVerdict verdict) => verdict switch
    {
        LlmVerdict.Ok => GenerationVerdict.Ok,
        LlmVerdict.RateLimited => GenerationVerdict.RateLimited,
        LlmVerdict.AuthFailed => GenerationVerdict.AuthFailed,
        LlmVerdict.Refused => GenerationVerdict.Refused,
        LlmVerdict.Timeout => GenerationVerdict.Timeout,
        _ => GenerationVerdict.Failed,
    };
}
