namespace Lyntai.Llm;

/// <summary>Whether a call wants the model's intermediate reasoning.
///
/// <para><b>Neutral by construction, because the vocabulary is not.</b> Every backend spells this
/// differently — a request field, a sampling parameter, a magic token in the prompt — and several spell it
/// not at all. This enum names the INTENT; each provider maps it to whatever it has, exactly as
/// <c>ICliProviderDialect</c> maps per-CLI flags. A library-level string like <c>/no_think</c> would be one
/// model family's syntax baked into shared code, which is what <c>model-decoupling.md</c> forbids.</para>
///
/// <para><b>ADVISORY, never a guarantee.</b> A provider that cannot express it ignores it, and a model that
/// reasons anyway is not a defect in this seam. Treat it as "prefer", never as "the reply will not contain
/// reasoning" — a caller that must have clean output still has to parse defensively.</para>
///
/// <para><b>Why it exists, measured.</b> Building the verification judge ladder (<c>docs/DECISIONS.md</c>
/// <b>D59</b>): <c>gemma3:4b</c> judged a 145-query corpus in <b>3.5 minutes</b>, while <c>qwen3:4b</c> —
/// same size class, same task — was abandoned after <b>62 minutes</b>, spending roughly 25 s per judgement
/// on reasoning tokens before emitting its JSON. An abliterated qwen3.5 showed the same profile, so it is
/// the family and not the fine-tune. For a seam that sits in the latency path of every recall, a 15×
/// penalty is disqualifying whatever the accuracy — and before this option there was no way to ask for the
/// cheap behaviour except by avoiding a whole class of models.</para>
/// </summary>
public enum LlmReasoning
{
    /// <summary>Whatever the backend and model do by default. No request field is sent.</summary>
    Default = 0,

    /// <summary>Prefer NO intermediate reasoning — answer directly. For latency-sensitive calls whose value
    /// is a short structured verdict rather than an argument.</summary>
    Suppress = 1,
}
