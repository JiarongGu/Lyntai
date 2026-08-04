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

    /// <summary>Classify a failed HTTP response, distinguishing "no credentials were SUPPLIED" from
    /// "the credentials were REJECTED". An auth failure with nothing to authenticate with is
    /// <see cref="GenerationVerdict.NotConfigured"/>, not <see cref="GenerationVerdict.AuthFailed"/> — and the
    /// difference is not cosmetic now that routing acts on it: NotConfigured skips the candidate blamelessly and
    /// tells a host to offer setup, whereas AuthFailed BENCHES the backend for the cooldown window. A backend
    /// nobody has configured yet would otherwise be penalised on every first attempt for a fact the platform
    /// already knew before calling.
    /// <para>Why not simply require a key up front: an OpenAI-COMPATIBLE endpoint run locally (LM Studio, vLLM,
    /// Ollama) legitimately needs none, so "no key" cannot mean unconfigured on its own — only "no key AND the
    /// server demanded one" does.</para>
    /// <para>The LLM domain states the SAME rule over its own vocabulary
    /// (<see cref="LlmVerdictClassifier.FromHttpFailure(System.Net.HttpStatusCode, string, bool)"/>), stated
    /// separately rather than shared because the two reach different populations — see the note there. Change
    /// one and check the other.</para></summary>
    /// <param name="status">The failed response's status.</param>
    /// <param name="body">The response body, for text-based classification.</param>
    /// <param name="hasCredentials">Whether this call actually carried credentials.</param>
    public static GenerationVerdict FromHttpFailure(HttpStatusCode status, string? body, bool hasCredentials)
    {
        var verdict = FromHttpFailure(status, body);
        return verdict == GenerationVerdict.AuthFailed && !hasCredentials
            ? GenerationVerdict.NotConfigured
            : verdict;
    }

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
        // both domains carry this one and it means the same thing; letting it fall to Failed would turn a
        // blameless skip into a penalised failure on the way across the boundary (a consumer-registered
        // matcher on the shared classifier can return it, so this is reachable, not theoretical)
        LlmVerdict.NotConfigured => GenerationVerdict.NotConfigured,
        _ => GenerationVerdict.Failed,
    };
}
