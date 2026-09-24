using Lyntai.Text;

namespace Lyntai.Providers.Onnx;

/// <summary>A batch encoded for the graph: one row per window, and which rows belong to which input.</summary>
/// <param name="Rows">What to feed the graph, every input's windows in input order.</param>
/// <param name="First">Input <c>i</c>'s rows are <c>Rows[First[i]..First[i + 1]]</c>.</param>
/// <param name="Weights">Each row's own content tokens — the tokens of its window, not the specials or a
/// query.</param>
internal sealed record WindowedBatch(WordPieceEncoding[] Rows, int[] First, int[] Weights)
{
    /// <summary>Whether input <paramref name="i"/> ran past the window, and so took several rows.</summary>
    public bool IsSegmented(int i) => First[i + 1] - First[i] > 1;
}

/// <summary>The model's tokenizer, bounded by its window: encodes a text, or a query and its documents, as the
/// rows a transformer takes, SEGMENTING whatever runs past <see cref="MaxTokens"/> by tokens rather than
/// cutting it (<c>docs/DECISIONS.md</c> <b>D177</b>).
///
/// <para>An input within the window becomes exactly the one row <see cref="WordPieceTokenizer.Encode(string,int)"/>
/// or <see cref="WordPieceTokenizer.Encode(string,string,int)"/> would give it. A longer one becomes a row per
/// <see cref="TokenSegmenter"/> window, each bracketed by the special tokens and, for a pair, carrying the whole
/// query: the query is never segmented.</para></summary>
/// <param name="tokenizer">The model's own vocabulary and rules.</param>
/// <param name="boundaries">Which of its rows continue a word or end a sentence.</param>
/// <param name="maxTokens">The sequence length a row may take, INCLUDING the special tokens.</param>
internal sealed class WindowedTokenizer(WordPieceTokenizer tokenizer, TokenBoundaries boundaries, int maxTokens)
{
    /// <summary>The sequence length a row may take, including the special tokens.</summary>
    public int MaxTokens => maxTokens;

    /// <summary>One text per input: <c>[CLS] window [SEP]</c> per window.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="MaxTokens"/> is under 3.</exception>
    public WindowedBatch EncodeTexts(IReadOnlyList<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        // the tokenizer's own special ids, and its own guard on the length, read off an empty encoding
        var shell = tokenizer.Encode(string.Empty, maxTokens).Ids;
        var batch = new Builder(texts.Count);
        for (var i = 0; i < texts.Count; i++)
        {
            batch.Begin(i);
            var ids = tokenizer.EncodeToIds(texts[i] ?? string.Empty);
            foreach (var (start, end) in TokenSegmenter.Windows(ids, maxTokens - 2, boundaries))
                batch.Add(Row(shell, null, ids, start, end), end - start);
        }
        return batch.Build();
    }

    /// <summary>One query and document per row: <c>[CLS] query [SEP] window [SEP]</c> per window of each
    /// document, the query in segment 0 and the window in segment 1.
    ///
    /// <para>A query too long to leave the document any room falls back to the tokenizer's own pair rule,
    /// which shortens the query and keeps no document token — there is no window to segment into.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="MaxTokens"/> is under 4.</exception>
    public WindowedBatch EncodePairs(string query, IReadOnlyList<string> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var shell = tokenizer.Encode(string.Empty, string.Empty, maxTokens).Ids;
        var queryIds = tokenizer.EncodeToIds(query ?? string.Empty);
        var budget = maxTokens - 3 - queryIds.Count;
        var batch = new Builder(documents.Count);
        for (var i = 0; i < documents.Count; i++)
        {
            batch.Begin(i);
            if (budget < 1)
            {
                batch.Add(tokenizer.Encode(query ?? string.Empty, documents[i] ?? string.Empty, maxTokens), 0);
                continue;
            }
            var ids = tokenizer.EncodeToIds(documents[i] ?? string.Empty);
            foreach (var (start, end) in TokenSegmenter.Windows(ids, budget, boundaries))
                batch.Add(Row(shell, queryIds, ids, start, end), end - start);
        }
        return batch.Build();
    }

    /// <summary><c>[CLS] query [SEP] window [SEP]</c> with the window in segment 1 — the layout
    /// <see cref="WordPieceTokenizer.Encode(string,string,int)"/> builds — or <c>[CLS] window [SEP]</c>, all
    /// segment 0, when there is no query.</summary>
    private static WordPieceEncoding Row(
        int[] shell, IReadOnlyList<int>? query, IReadOnlyList<int> ids, int start, int end)
    {
        var (cls, sep) = (shell[0], shell[1]);
        var row = new List<int>((query?.Count ?? 0) + end - start + 3) { cls };
        if (query is not null)
        {
            row.AddRange(query);
            row.Add(sep);
        }
        var windowStarts = row.Count;
        for (var t = start; t < end; t++) row.Add(ids[t]);
        row.Add(sep);

        var types = new int[row.Count];
        if (query is not null) Array.Fill(types, 1, windowStarts, types.Length - windowStarts);
        var mask = new int[row.Count];
        Array.Fill(mask, 1);
        return new WordPieceEncoding([.. row], mask, types);
    }

    private sealed class Builder(int inputs)
    {
        private readonly List<WordPieceEncoding> _rows = new(inputs);
        private readonly List<int> _weights = new(inputs);
        private readonly int[] _first = new int[inputs + 1];

        public void Begin(int input) => _first[input] = _rows.Count;

        public void Add(WordPieceEncoding row, int weight)
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
