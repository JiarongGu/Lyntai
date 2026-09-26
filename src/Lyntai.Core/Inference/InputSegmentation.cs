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
    /// the cost of one long input. It applies on every provider that segments. Segmenting multiplies a call's
    /// work, and a call that outruns its timeout fails as <see cref="ProviderVerdict.Timeout"/>, so on slow
    /// hardware this cap is what bounds it.
    ///
    /// <para>There is no per-CALL total: a call's pieces are already at most its inputs times this, and fitting
    /// a call to a latency budget is a policy for the deployment that measured it — which is why a reranking
    /// REQUEST may narrow this cap for itself (<see cref="ScoreRequest.MaxPiecesPerInput"/>).</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is under 1.</exception>
    public int? MaxPiecesPerInput
    {
        get => _maxPiecesPerInput;
        set => _maxPiecesPerInput = value is null or >= 1
            ? value
            : throw new ArgumentOutOfRangeException(nameof(MaxPiecesPerInput), value,
                "MaxPiecesPerInput must be at least 1, or null for no cap.");
    }

    /// <summary>The cap a call runs under when its request asks for <paramref name="requested"/> pieces per
    /// input (<see cref="ScoreRequest.MaxPiecesPerInput"/>): the lower of that and <see cref="MaxPiecesPerInput"/>,
    /// so a request narrows this record's cap and never widens it. Null asks nothing and keeps this record's.
    /// A provider honouring a request's cap passes the result to <see cref="Spread{T}"/>.</summary>
    /// <param name="requested">The request's own cap, or null.</param>
    public int? MaxPiecesFor(int? requested) =>
        requested is { } asked && (MaxPiecesPerInput is not { } own || asked < own) ? asked : MaxPiecesPerInput;

    /// <summary>The pieces of one input that <see cref="MaxPiecesPerInput"/> keeps: at most
    /// <paramref name="cap"/> of <paramref name="pieces"/> — the first, the last, and the rest at even steps
    /// between, piece <c>round(i·(n−1)/(cap−1))</c> with halves rounded up — or the first alone for a cap of 1.
    /// Every piece, the same list, when <paramref name="cap"/> is null or the pieces are within it. A provider
    /// honouring this record keeps its pieces by this rule, whatever a piece is (text, a token window).</summary>
    /// <param name="pieces">One input's pieces, in order.</param>
    /// <param name="cap">The cap; null keeps every piece.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cap"/> is under 1.</exception>
    public static IReadOnlyList<T> Spread<T>(IReadOnlyList<T> pieces, int? cap)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        if (cap is not { } most) return pieces;
        ArgumentOutOfRangeException.ThrowIfLessThan(most, 1, nameof(cap));
        if (pieces.Count <= most) return pieces;
        if (most == 1) return [pieces[0]];
        var kept = new T[most];
        for (var i = 0; i < most; i++)
            kept[i] = pieces[(int)((2L * i * (pieces.Count - 1) + (most - 1)) / (2L * (most - 1)))];
        return kept;
    }

    /// <summary>What a reranker pair's DOCUMENT keeps of a window of <paramref name="window"/> units
    /// (<see cref="MinDocumentShare"/>): the share rounded up — in decimal, so 0.8 of 60 is 48 rather than a
    /// binary 48.000…01 that rounds to 49 — and never less than one unit, which a share below decimal's range
    /// would otherwise round to. The query keeps at most the rest.</summary>
    /// <param name="window">The pair window, in whatever unit the provider bounds (characters, tokens).</param>
    /// <param name="minDocumentShare">The share; <see cref="MinDocumentShare"/> of the record in force.</param>
    public static int DocumentShare(int window, double minDocumentShare) =>
        Math.Max(1, (int)Math.Ceiling((decimal)minDocumentShare * window));
}
