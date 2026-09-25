namespace Lyntai.Inference;

/// <summary>Whether a call wants the model's intermediate reasoning.
///
/// <para><b>Neutral by construction, because the vocabulary is not.</b> Every backend spells this
/// differently — a request field, a sampling parameter, a magic token in the prompt — and several spell it
/// not at all. This enum names the INTENT; each provider maps it to whatever it has, exactly as
/// <c>ICliBackend</c> maps per-CLI flags — Ollama-native to its <c>think</c> field, and the OpenAI-shaped
/// provider, whose schema has none, to the fields <c>HttpModelOptions.SuppressReasoningFields</c> configures.
/// A library-level string like <c>/no_think</c> would be one model family's syntax baked into shared code,
/// which is what <c>model-decoupling.md</c> forbids.</para>
///
/// <para><b>ADVISORY, never a guarantee.</b> A provider that cannot express it ignores it, and a model that
/// reasons anyway is not a defect in this seam. Treat it as "prefer", never as "the reply will not contain
/// reasoning" — a caller that must have clean output still has to parse defensively.</para>
///
/// <para><b>Why it exists:</b> a reasoning model family can spend an order of magnitude longer on a short
/// structured verdict than a non-reasoning one of the same size, which disqualifies it from a seam in the
/// latency path of every recall (measured for the verification judge ladder, <c>docs/DECISIONS.md</c>
/// <b>D59</b>).</para>
/// </summary>
public enum TextReasoning
{
    /// <summary>Whatever the backend and model do by default. No request field is sent.</summary>
    Default = 0,

    /// <summary>Prefer NO intermediate reasoning — answer directly. For latency-sensitive calls whose value
    /// is a short structured verdict rather than an argument.</summary>
    Suppress = 1,
}
