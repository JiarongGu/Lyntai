namespace Lyntai.Inference;

/// <summary>What a provider does with an input longer than its window.</summary>
public enum InputOverflow
{
    /// <summary>Split it into windows, answer every window, and combine the answers into one per input: a
    /// score is the best window's, a vector the windows' unit vectors averaged by length and
    /// re-normalised.</summary>
    Segment = 0,

    /// <summary>Cut it and answer the part kept — at the window, or where the first piece would end; each
    /// provider's <c>Segmentation</c> option says which.</summary>
    Truncate = 1,
}

/// <summary>How a provider with a window treats an input longer than that window (<c>docs/DECISIONS.md</c>
/// <b>D177</b>) — one record every such provider takes, beside its own window setting
/// (<c>HttpModelOptions.MaxInputChars</c>, <c>OllamaOptions.MaxInputChars</c>,
/// <c>OnnxProviderOptions.MaxTokens</c>).
///
/// <para><b>Segmenting extends what a small-window model can take; it is not a rule about how input must
/// be processed</b>, so it is configured rather than imposed. A provider given no record keeps its own
/// default, which each provider's <c>Segmentation</c> option states. A new record segments.</para>
///
/// <para>Each setter validates its value, so an out-of-range record fails where it is configured.</para></summary>
public sealed class InputSegmentation
{
    private InputOverflow _overflow = InputOverflow.Segment;
    private double _overlap = 0.15;
    private double _minDocumentShare = 0.5;
    private int? _maxPiecesPerInput;

    /// <summary>Whether an over-long input is segmented (the default) or truncated. Where a truncating
    /// provider cuts is stated by its <c>Segmentation</c> option.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="InputOverflow"/>,
    /// which providers would otherwise read differently.</exception>
    public InputOverflow Overflow
    {
        get => _overflow;
        set => _overflow = Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(Overflow), value, "Overflow must be Segment or Truncate.");
    }

    /// <summary>How far each window after the first reaches back into the one before, as a share of that
    /// window: the next starts at a boundary inside its last <c>Overlap</c>, else where it ended. Default
    /// 0.15; 0 turns overlap off.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 0.5 — past half a
    /// window, every window re-sends more of the last than it adds.</exception>
    public double Overlap
    {
        get => _overlap;
        set => _overlap = value is >= 0 and <= 0.5
            ? value
            : throw new ArgumentOutOfRangeException(nameof(Overlap), value, "Overlap must be between 0 and 0.5.");
    }

    /// <summary>For a reranker PAIR, the share of the window its DOCUMENT keeps however long the query is:
    /// the query keeps at most the rest, and one longer is cut ONCE per call, the same for every document —
    /// even one that would fit beside the whole query — because scores against different question text are
    /// not comparable. Default 0.5. The query itself is never segmented.
    ///
    /// <para>It applies wherever one window holds the pair: the ONNX cross-encoder, and an HTTP reranker, whose
    /// <c>MaxInputChars</c> is that pair window. An embedder takes no query.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not strictly between 0 and 1: a document
    /// needs some of the window, and so does the query.</exception>
    public double MinDocumentShare
    {
        get => _minDocumentShare;
        set => _minDocumentShare = value is > 0 and < 1
            ? value
            : throw new ArgumentOutOfRangeException(nameof(MinDocumentShare), value,
                "MinDocumentShare must be greater than 0 and less than 1.");
    }

    /// <summary>The most pieces one input is segmented into; null — the default — sets no cap. An input that
    /// would take more keeps this many, spread evenly: the first piece, the LAST, and the rest at even steps
    /// between (piece <c>round(i·(n−1)/(cap−1))</c> of <c>n</c>); a cap of 1 keeps the first. <b>Coverage then
    /// has gaps</b> — text in a dropped piece is neither scored nor embedded — which is the trade for bounding
    /// the cost of one long input. It applies on every provider that segments.
    ///
    /// <para>There is no per-CALL cap: a call's pieces are already at most its inputs times this, and fitting
    /// a call to a latency budget is a policy for the deployment that measured it.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is under 1.</exception>
    public int? MaxPiecesPerInput
    {
        get => _maxPiecesPerInput;
        set => _maxPiecesPerInput = value is null or >= 1
            ? value
            : throw new ArgumentOutOfRangeException(nameof(MaxPiecesPerInput), value,
                "MaxPiecesPerInput must be at least 1, or null for no cap.");
    }
}
