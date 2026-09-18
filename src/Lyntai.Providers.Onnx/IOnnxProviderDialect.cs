using Lyntai.Text;
using Microsoft.ML.OnnxRuntime;

namespace Lyntai.Providers.Onnx;

/// <summary>The loaded engine, handed to a dialect for one call: a session, its tokenizer, and the
/// sequence limit both truncate at. Carries no decision of its own — everything that INTERPRETS a tensor
/// belongs to the dialect.</summary>
/// <param name="Session">The ONNX graph, already open.</param>
/// <param name="Tokenizer">The model's own WordPiece vocabulary.</param>
/// <param name="MaxTokens">Where input is truncated, including the special tokens.</param>
internal readonly record struct OnnxRun(InferenceSession Session, WordPieceTokenizer Tokenizer, int MaxTokens);

/// <summary>What a <see cref="OnnxProvider"/> DOES with its session — and therefore what that provider
/// produces (<c>docs/DECISIONS.md</c> <b>D157</b>).
///
/// <para><b>The provider is the ENGINE and stays pure.</b> It opens a session, tokenizes, feeds and runs;
/// it never pools, normalizes or reads a logit. Those are the two ENDS of a call, they are the only thing
/// that differs between an embedding model and a reranker on the same runtime, and they live here.</para>
///
/// <para><b>So a kind is never a reason to fork the provider class.</b> What comes out is a
/// <see cref="Produces"/> value the dialect states and the provider declares — which is where
/// <b>D152</b> says a capability belongs.</para></summary>
internal interface IOnnxProviderDialect
{
    /// <summary>The <see cref="Lyntai.Inference.ProviderKinds"/> value a provider running this dialect
    /// serves. The provider copies it into its <c>Capabilities.Produces</c>, so routing selects on it.</summary>
    string Produces { get; }

    /// <summary>Which graph output this dialect reads, resolved ONCE at composition.
    ///
    /// <para><b>Refuse an unreadable graph here, never at call time.</b> A dialect whose consumer is
    /// fail-open — the memory scoring seam is — turns a per-call objection into a recall that is quietly
    /// never verified, so a graph that cannot carry this dialect's answer must fail while a human is
    /// watching the composition.</para></summary>
    /// <param name="session">The open graph.</param>
    /// <exception cref="InvalidOperationException">The graph has no output this dialect can read.</exception>
    string ResolveOutput(InferenceSession session);
}

/// <summary>A dialect that turns texts into VECTORS — the bi-encoder shape.</summary>
internal interface IOnnxVectorDialect : IOnnxProviderDialect
{
    /// <summary>One batched forward pass: encode each text, pad to the longest, run, reduce each row.</summary>
    /// <param name="run">The engine for this call.</param>
    /// <param name="outputName">What <see cref="IOnnxProviderDialect.ResolveOutput"/> chose at composition.</param>
    /// <param name="texts">The batch; never empty (the provider answers an empty request itself).</param>
    float[][] Embed(OnnxRun run, string outputName, IReadOnlyList<string> texts);
}

/// <summary>A dialect that turns a query and documents into SCORES — the cross-encoder shape, which
/// encodes a PAIR per row rather than one text.</summary>
internal interface IOnnxScoreDialect : IOnnxProviderDialect
{
    /// <summary>One batched forward pass over the pairs: encode each, pad to the longest, run, read one
    /// score per row.</summary>
    /// <param name="run">The engine for this call.</param>
    /// <param name="outputName">What <see cref="IOnnxProviderDialect.ResolveOutput"/> chose at composition.</param>
    /// <param name="query">The question every document is scored against.</param>
    /// <param name="documents">The batch; never empty (the provider answers an empty request itself).</param>
    double[] Score(OnnxRun run, string outputName, string query, IReadOnlyList<string> documents);
}
