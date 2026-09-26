using Lyntai.Inference;
using Lyntai.Text;

namespace Lyntai.Providers.Onnx;

/// <summary>A batch encoded for the graph: one row per window, and which rows belong to which input.</summary>
/// <param name="Rows">What to feed the graph, every input's windows in input order.</param>
/// <param name="First">Input <c>i</c>'s rows are <c>Rows[First[i]..First[i + 1]]</c>.</param>
/// <param name="Weights">Each row's own content tokens — the tokens of its window, not the specials or a
/// query — and 0 where an input took a single row, which is never pooled.</param>
internal sealed record WindowedBatch(TokenEncoding[] Rows, int[] First, int[] Weights)
{
    /// <summary>The most rows a forward pass holds when a call has fewer inputs than this.</summary>
    public const int MinPassRows = 8;

    /// <summary>Whether input <paramref name="i"/> ran past the window, and so took several rows.</summary>
    public bool IsSegmented(int i) => First[i + 1] - First[i] > 1;

    /// <summary>Every row through <paramref name="forward"/>, in passes of at most
    /// <c>max(<see cref="MinPassRows"/>, inputs)</c> rows, the answers concatenated in row order. A call with
    /// no segmented input is therefore ONE pass, the one it would be unsegmented, and segmenting never makes a
    /// pass larger than that bound.</summary>
    /// <param name="forward">The graph: one answer per row it is fed, in order.</param>
    public T[] Forward<T>(Func<TokenEncoding[], T[]> forward)
    {
        var size = Math.Max(MinPassRows, First.Length - 1);
        if (Rows.Length <= size) return forward(Rows);
        var answers = new List<T>(Rows.Length);
        for (var at = 0; at < Rows.Length; at += size)
            answers.AddRange(forward(Rows[at..Math.Min(Rows.Length, at + size)]));
        return [.. answers];
    }
}

/// <summary>The model's tokenizer, bounded by its window: encodes a text, or a query and its documents, as the
/// rows a transformer takes (<c>docs/DECISIONS.md</c> <b>D177</b>).
///
/// <para>With no <see cref="InputSegmentation"/> — the provider's default — every input is the one row the
/// tokenizer itself gives it, cut at the window: its own <c>Encode</c>, exactly. With a record, a text within the
/// window is still that row, and a longer one is a row per <see cref="TokenSegmenter"/> window, each FRAMED in
/// the model's own special tokens — or, when the record truncates, one row cut at the window. A pair's query is
/// never segmented, and keeps at most (1 − <see cref="InputSegmentation.MinDocumentShare"/>) of the window: one
/// longer is cut ONCE for the whole call, so every document is scored against the same question, and a pair
/// that would fit beside the whole query is not the tokenizer's row.</para></summary>
/// <param name="tokenizer">The model's own vocabulary, rules and layout of special tokens.</param>
/// <param name="boundaries">Which of its rows continue a word or end a sentence.</param>
/// <param name="maxTokens">The sequence length a row may take, INCLUDING the special tokens.</param>
/// <param name="segmentation">What to do past the window; null truncates, as the tokenizer does.</param>
internal sealed class WindowedTokenizer(
    ITransformerTokenizer tokenizer, TokenBoundaries boundaries, int maxTokens, InputSegmentation? segmentation)
{
    /// <summary>The sequence length a row may take, including the special tokens.</summary>
    public int MaxTokens => maxTokens;

    /// <summary>One text per input — <c>[CLS] text [SEP]</c> in BERT's layout — per window when segmenting.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="MaxTokens"/> leaves no room for content.</exception>
    public WindowedBatch EncodeTexts(IReadOnlyList<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        // truncating a text is exactly the tokenizer's own cut, record or none
        if (segmentation is not { Overflow: InputOverflow.Segment } segment)
            return OneRowEach(texts, text => tokenizer.Encode(text ?? string.Empty, maxTokens));

        // an empty encoding is exactly the specials, and making it runs the tokenizer's own guard on the length
        var budget = maxTokens - tokenizer.Encode(string.Empty, maxTokens).Ids.Length;
        var batch = new Builder(texts.Count);
        for (var i = 0; i < texts.Count; i++)
        {
            batch.Begin(i);
            var ids = tokenizer.EncodeToIds(texts[i] ?? string.Empty);
            var windows = InputSegmentation.Spread(
                TokenSegmenter.Windows(ids, budget, boundaries, segment.Overlap), segment.MaxPiecesPerInput);
            foreach (var (start, end) in windows)
                batch.Add(tokenizer.Frame(Slice(ids, start, end)), windows.Count > 1 ? end - start : 0);
        }
        return batch.Build();
    }

    /// <summary>One query and document per row — <c>[CLS] query [SEP] document [SEP]</c> in BERT's layout,
    /// <c>&lt;s&gt; query &lt;/s&gt;&lt;/s&gt; document &lt;/s&gt;</c> in XLM-R's — with the document, or one
    /// window of it, in whatever segment the model's layout puts it. <paramref name="maxPiecesPerInput"/> is the
    /// request's own cap, which narrows the record's (<see cref="InputSegmentation.MaxPiecesFor"/>).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="MaxTokens"/> leaves no room for content.</exception>
    public WindowedBatch EncodePairs(string query, IReadOnlyList<string> documents, int? maxPiecesPerInput = null)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (segmentation is null)
            return OneRowEach(documents,
                document => tokenizer.Encode(query ?? string.Empty, document ?? string.Empty, maxTokens));

        // content tokens across both sides: what the pair's specials leave
        var budget = maxTokens - tokenizer.Encode(string.Empty, string.Empty, maxTokens).Ids.Length;
        var documentShare = InputSegmentation.DocumentShare(budget, segmentation.MinDocumentShare);
        var queryIds = tokenizer.EncodeToIds(query ?? string.Empty);
        List<int> kept = [.. queryIds.Take(budget - documentShare)];
        var documentBudget = segmentation.MaxDocumentPiece is { } piece
            ? Math.Min(piece, budget - kept.Count)
            : budget - kept.Count;
        var batch = new Builder(documents.Count);
        for (var i = 0; i < documents.Count; i++)
        {
            batch.Begin(i);
            var ids = tokenizer.EncodeToIds(documents[i] ?? string.Empty);
            IReadOnlyList<(int Start, int End)> windows = segmentation.Overflow == InputOverflow.Segment
                ? InputSegmentation.Spread(TokenSegmenter.Windows(ids, documentBudget, boundaries, segmentation.Overlap),
                    segmentation.MaxPiecesFor(maxPiecesPerInput))
                : [(0, Math.Min(ids.Count, documentBudget))];
            foreach (var (start, end) in windows)
                batch.Add(tokenizer.Frame(kept, Slice(ids, start, end)), windows.Count > 1 ? end - start : 0);
        }
        return batch.Build();
    }

    private static WindowedBatch OneRowEach(IReadOnlyList<string> inputs, Func<string, TokenEncoding> encode)
    {
        var batch = new Builder(inputs.Count);
        for (var i = 0; i < inputs.Count; i++)
        {
            batch.Begin(i);
            batch.Add(encode(inputs[i]), 0);
        }
        return batch.Build();
    }

    private static int[] Slice(IReadOnlyList<int> ids, int start, int end)
    {
        var slice = new int[end - start];
        for (var t = start; t < end; t++) slice[t - start] = ids[t];
        return slice;
    }

    private sealed class Builder(int inputs)
    {
        private readonly List<TokenEncoding> _rows = new(inputs);
        private readonly List<int> _weights = new(inputs);
        private readonly int[] _first = new int[inputs + 1];

        public void Begin(int input) => _first[input] = _rows.Count;

        public void Add(TokenEncoding row, int weight)
        {
            _rows.Add(row);
            _weights.Add(weight);
        }

        public WindowedBatch Build()
        {
            _first[^1] = _rows.Count;
            return new WindowedBatch([.. _rows], _first, [.. _weights]);
        }
    }
}
