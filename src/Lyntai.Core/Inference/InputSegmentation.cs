namespace Lyntai.Inference;

/// <summary>What a provider does with an input longer than its window.</summary>
public enum InputOverflow
{
    /// <summary>Split it into windows, answer every window, and combine the answers into one per input: a
    /// score is the best window's, a vector the windows' unit vectors averaged by length and
    /// re-normalised.</summary>
    Segment = 0,

    /// <summary>Cut it at the window and answer the part that fits.</summary>
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
    private double _overlap = 0.15;
    private double _minDocumentShare = 0.5;

    /// <summary>Whether an over-long input is segmented (the default) or truncated. Where a truncating
    /// provider cuts is stated by its <c>Segmentation</c> option.</summary>
    public InputOverflow Overflow { get; set; } = InputOverflow.Segment;

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
    /// a query too long to leave the document that much is cut to the rest. Default 0.5. The query itself is
    /// never segmented.
    ///
    /// <para>It applies only where a provider measures query and document against one window — the ONNX
    /// cross-encoder. An HTTP bound is per document and never counts the query, so there it does not
    /// apply.</para></summary>
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
}
