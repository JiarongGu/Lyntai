using Lyntai.Cortex;

namespace Lyntai.Inference.Tracing;

/// <summary>What <c>AddTextCallTracing</c> records for each front-door call, and which scorers it runs.</summary>
public sealed class TextCallTracingOptions
{
    /// <summary>The <see cref="RunTrace.Mode"/> of the trace a call begins when it is not inside
    /// <see cref="TextCallTracing.Into"/>. Default <c>"text-call"</c>.</summary>
    public string Mode { get; set; } = "text-call";

    /// <summary>Which registered scorers run over each call. Default: the deterministic ones only
    /// (<c>!IsLlm</c>). Admitting an LLM scorer adds a model call to EVERY traced call, and the reply waits for
    /// it; that call is itself never traced or scored.</summary>
    public Func<IScorer, bool> Scorers { get; set; } = scorer => !scorer.IsLlm;

    /// <summary>Whether the reply's text is stored in the step's <see cref="TraceStep.Detail"/>, cut to
    /// <see cref="MaxRecordedChars"/>. Default false: no reply text is stored. The prompt never is.</summary>
    public bool RecordText { get; set; }

    /// <summary>The most reply characters <see cref="RecordText"/> stores. Default 2000; zero or more.</summary>
    public int MaxRecordedChars { get; set; } = 2000;

    /// <summary>Which calls are traced; null traces every call. A call it excludes passes straight through.</summary>
    public Func<TextRequest, bool>? Include { get; set; }
}
