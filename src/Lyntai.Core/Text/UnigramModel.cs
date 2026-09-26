namespace Lyntai.Text;

/// <summary>SentencePiece's Unigram segmentation of one pre-tokenized word — the best-scoring sequence of
/// pieces, computed as HF <c>tokenizers</c> computes it.
///
/// <para>Candidates at each position are visited shortest first and replace the best only when STRICTLY
/// better, so a tie keeps the path that reached the position first. A position no single-code-point piece
/// covers gets an unknown node scored <c>min − 10</c>, the minimum taken over EVERY piece; consecutive unknown
/// nodes fuse into one, which is the unknown id unless the fused text is itself a piece.</para></summary>
internal sealed class UnigramModel
{
    private const double UnknownPenalty = 10.0;

    private readonly Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> _pieces;
    private readonly double[] _scores;
    private readonly int _longest;
    private readonly int _unknownId;
    private readonly double _unknownScore;

    /// <param name="pieces">Each id's piece and score, in id order.</param>
    /// <param name="unknownId">What an uncovered code point, or a fused run of them, becomes.</param>
    /// <param name="unmatchable">Ids never matched from text — the special tokens.</param>
    public UnigramModel(
        IReadOnlyList<(string Piece, double Score)> pieces, int unknownId, IReadOnlySet<int> unmatchable)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        ArgumentNullException.ThrowIfNull(unmatchable);

        var ids = new Dictionary<string, int>(pieces.Count, StringComparer.Ordinal);
        _scores = new double[pieces.Count];
        var min = double.PositiveInfinity;
        for (var id = 0; id < pieces.Count; id++)
        {
            var (piece, score) = pieces[id];
            _scores[id] = score;
            min = Math.Min(min, score);
            if (unmatchable.Contains(id) || string.IsNullOrEmpty(piece)) continue;
            ids[piece] = id;                                       // a repeated piece keeps its LAST id
            _longest = Math.Max(_longest, piece.Length);
        }

        _pieces = ids.GetAlternateLookup<ReadOnlySpan<char>>();
        _unknownId = unknownId;
        _unknownScore = min - UnknownPenalty;
    }

    /// <summary>Append <paramref name="word"/>'s pieces to <paramref name="ids"/>. O(length × longest piece).</summary>
    public void Segment(string word, List<int> ids)
    {
        ArgumentNullException.ThrowIfNull(word);
        ArgumentNullException.ThrowIfNull(ids);
        var n = word.Length;
        if (n == 0) return;

        // the best path ENDING at each position: its score, where its last piece began, and that piece
        var score = new double[n + 1];
        var from = new int[n + 1];
        var chosen = new int[n + 1];
        var reached = new bool[n + 1];
        reached[0] = true;

        for (var at = 0; at < n;)
        {
            var width = char.IsSurrogatePair(word, at) ? 2 : 1;
            var covered = false;
            for (var length = 1; length <= Math.Min(_longest, n - at); length++)
            {
                var end = at + length;
                if (end < n && char.IsLowSurrogate(word[end]) && char.IsHighSurrogate(word[end - 1])) continue;
                if (!_pieces.TryGetValue(word.AsSpan(at, length), out var id)) continue;
                Offer(end, score[at] + _scores[id], at, id);
                if (length == width) covered = true;
            }
            if (!covered) Offer(at + width, score[at] + _unknownScore, at, _unknownId);
            at += width;
        }

        var start = ids.Count;
        for (var end = n; end > 0;)
        {
            var begin = from[end];
            if (chosen[end] == _unknownId)
            {
                while (begin > 0 && chosen[begin] == _unknownId) begin = from[begin];
                ids.Add(_pieces.TryGetValue(word.AsSpan(begin, end - begin), out var fused) ? fused : _unknownId);
            }
            else
            {
                ids.Add(chosen[end]);
            }
            end = begin;
        }
        ids.Reverse(start, ids.Count - start);

        void Offer(int end, double candidate, int begin, int id)
        {
            if (reached[end] && candidate <= score[end]) return;
            (reached[end], score[end], from[end], chosen[end]) = (true, candidate, begin, id);
        }
    }
}
