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

    /// <summary>Map the shared taxonomy onto media's — one arm per <see cref="LlmVerdict"/> member, with no
    /// group left to the catch-all.
    /// <para>Every verdict both domains carry keeps its meaning, and the two BLAMELESS ones matter most:
    /// <see cref="LlmVerdict.NotConfigured"/> (never set up) and <see cref="LlmVerdict.Unsupported"/> (this
    /// backend cannot do THIS request). Flattening either to <see cref="GenerationVerdict.Failed"/> converts
    /// the policy's <see cref="Routing.GenerationFallbackAction.Advance"/> into
    /// <see cref="Routing.GenerationFallbackAction.PenalizeAndAdvance"/> on the way across the boundary, so
    /// repeated capability gaps bench a perfectly healthy backend. Both are REACHABLE, not theoretical: a
    /// consumer-registered matcher on the shared classifier can return either, and an exception classified
    /// through <see cref="FromException"/> can land on either.</para>
    /// <para><see cref="LlmVerdict.ContextWindowExceeded"/> is the only member with no media counterpart, and
    /// it collapses to <see cref="GenerationVerdict.Failed"/> DELIBERATELY rather than by omission
    /// (<c>docs/DECISIONS.md</c> D43). <see cref="GenerationVerdict.Unsupported"/> would both describe and
    /// route it better, but <see cref="Routing.GenerationRouter"/> deliberately never REPORTS a blameless
    /// verdict over a real failure — so "your prompt is too long for this backend", the one thing the caller
    /// can actually act on, would be swallowed exactly when it is the only thing that went wrong.</para>
    /// <para><b>The discard covers undefined numeric values only.</b> C# cannot make a switch over an enum
    /// exhaustive (<c>(LlmVerdict)99</c> is a legal value), so a discard is mandatory and the compiler can
    /// never be the growth gate. The gate is a TEST —
    /// <c>GenerationVerdictClassifierTests.Every_llm_verdict_states_its_media_translation</c> fails until a
    /// newly added member is given a translation, the same obligation
    /// <c>LlmVerdictExtensionsTests.Every_verdict_states_whether_it_is_transient</c> places on the call-site
    /// helpers and D38 places on the routing policy.</para></summary>
    private static GenerationVerdict Translate(LlmVerdict verdict) => verdict switch
    {
        LlmVerdict.Ok => GenerationVerdict.Ok,
        LlmVerdict.RateLimited => GenerationVerdict.RateLimited,
        LlmVerdict.AuthFailed => GenerationVerdict.AuthFailed,
        LlmVerdict.Refused => GenerationVerdict.Refused,
        LlmVerdict.Timeout => GenerationVerdict.Timeout,
        LlmVerdict.NotConfigured => GenerationVerdict.NotConfigured,
        LlmVerdict.Unsupported => GenerationVerdict.Unsupported,
        LlmVerdict.Failed => GenerationVerdict.Failed,
        // stated explicitly so the discard holds nothing but undefined values — see the doc above
        LlmVerdict.ContextWindowExceeded => GenerationVerdict.Failed,
        _ => GenerationVerdict.Failed,
    };
}
