using System.Net;
using Lyntai.Llm;

namespace Lyntai.Generation;

/// <summary>Turns a transport failure or an error message into a <see cref="GenerationVerdict"/>.
///
/// It DELEGATES the pattern matching to Core's <see cref="LlmVerdictClassifier"/> and translates the answer
/// into media's vocabulary. That is the deliberate middle path between the two bad options: media does not
/// adopt LLM-named types (it is a separate domain), and it does not carry a second copy of the "what does a
/// 429 / a content-policy refusal look like" corpus, which would drift out of sync
/// (<c>docs/DECISIONS.md</c> D21). Consumer-registered matchers on the shared classifier therefore teach BOTH
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
    /// <para>Every verdict both domains carry keeps its meaning, and the two BLAMELESS ones matter most.
    /// Flattening <see cref="LlmVerdict.NotConfigured"/> or <see cref="LlmVerdict.Unsupported"/> to
    /// <see cref="GenerationVerdict.Failed"/> converts the policy's
    /// <see cref="Routing.GenerationFallbackAction.Advance"/> into
    /// <see cref="Routing.GenerationFallbackAction.PenalizeAndAdvance"/>, so repeated capability gaps bench a
    /// perfectly healthy backend. Both are REACHABLE: a consumer-registered matcher on the shared classifier
    /// can return either, and so can <see cref="FromException"/>.</para>
    /// <para><b>A translated verdict keeps its MEANING and CHANGES its fallback semantics</b> — the two
    /// domains answer <see cref="LlmVerdict.Unsupported"/> differently on purpose, each policy being right
    /// for its own domain. The change is invisible at the call site, so never read a translated verdict as
    /// "the same thing will happen".</para>
    /// <para><see cref="LlmVerdict.ContextWindowExceeded"/> is the only member with no media counterpart, and
    /// it maps to <see cref="GenerationVerdict.Unsupported"/>: too big for THIS backend is a capability gap,
    /// not ill health, so it must ADVANCE without counting toward the dead-host threshold.</para>
    /// <para><b>A blameless mapping is only safe because the ROUTER keeps a second reporting slot — do not
    /// undo half of it</b> (<c>docs/DECISIONS.md</c> D36; <see cref="Routing.GenerationRouter"/> states it).</para>
    /// <para><b>The discard covers undefined numeric values only, and returns <c>null</c> so a missing arm is
    /// detectable.</b> C# cannot make a switch over an enum exhaustive (<c>(LlmVerdict)99</c> is legal), so
    /// the compiler can never be the growth gate. The gate is a TEST,
    /// <c>GenerationVerdictClassifierTests.Every_llm_verdict_states_its_media_translation</c>, which fails
    /// until a new member has BOTH a row in its table and an arm here — and returning null rather than
    /// <see cref="GenerationVerdict.Failed"/> is what lets it tell a missing arm from a deliberate one.</para></summary>
    private static GenerationVerdict Translate(LlmVerdict verdict) =>
        // an undefined numeric value is still classified conservatively; only the GATE sees the difference
        TryTranslate(verdict) ?? GenerationVerdict.Failed;

    /// <summary>The translation table itself, with <c>null</c> meaning "no arm for this member" — an
    /// implementation detail, not consumer surface, exposed to the tests via <c>InternalsVisibleTo</c> so the
    /// growth gate can demand an ARM rather than merely a value. Without it a new member would pass the gate
    /// on the discard's answer alone, which is exactly how <see cref="LlmVerdict.Unsupported"/> came to be
    /// reported as <see cref="GenerationVerdict.Failed"/> for a whole release.</summary>
    internal static GenerationVerdict? TryTranslate(LlmVerdict verdict) => verdict switch
    {
        LlmVerdict.Ok => GenerationVerdict.Ok,
        LlmVerdict.RateLimited => GenerationVerdict.RateLimited,
        LlmVerdict.AuthFailed => GenerationVerdict.AuthFailed,
        LlmVerdict.Refused => GenerationVerdict.Refused,
        LlmVerdict.Timeout => GenerationVerdict.Timeout,
        LlmVerdict.NotConfigured => GenerationVerdict.NotConfigured,
        LlmVerdict.Unsupported => GenerationVerdict.Unsupported,
        LlmVerdict.Failed => GenerationVerdict.Failed,
        // no media counterpart, so it lands on the nearest media MEANING (a capability gap) rather than on
        // the nearest severity — see the doc above for why that needed the router's blameless slot first
        LlmVerdict.ContextWindowExceeded => GenerationVerdict.Unsupported,
        _ => null,
    };
}
