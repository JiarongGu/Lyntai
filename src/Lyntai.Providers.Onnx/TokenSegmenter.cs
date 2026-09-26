using System.Buffers;
using System.Text;
using Lyntai.Inference;

namespace Lyntai.Providers.Onnx;

/// <summary>Which vocabulary rows continue a word and which end a sentence — what
/// <see cref="TokenSegmenter"/> chooses a window's edges by.</summary>
internal sealed class TokenBoundaries
{
    private const byte Continuation = 1;
    private const byte SentenceEnd = 2;
    private const char MetaSpace = '▁';

    // At token level the whitespace is gone, so the `.` in "3.14" ends a sentence too; that costs only where a
    // window happens to end.
    private static readonly HashSet<string> SentenceEnds =
        new(StringComparer.Ordinal) { ".", "!", "?", ";", "。", "！", "？", "；" };

    private readonly byte[] _kinds;

    private TokenBoundaries(byte[] kinds) => _kinds = kinds;

    /// <summary>Read a <c>vocab.txt</c>'s rows, id = line number: a <c>##</c> row continues a word, and a
    /// sentence-ending punctuation row ends a sentence.</summary>
    public static TokenBoundaries FromVocabulary(IReadOnlyList<string> vocabulary)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);
        var kinds = new byte[vocabulary.Count];
        for (var id = 0; id < kinds.Length; id++)
            kinds[id] = vocabulary[id].StartsWith("##", StringComparison.Ordinal) ? Continuation
                : SentenceEnds.Contains(vocabulary[id]) ? SentenceEnd
                : (byte)0;
        return new TokenBoundaries(kinds);
    }

    /// <summary>Read a SentencePiece vocabulary, id = position: a piece WITHOUT a leading <c>▁</c> continues a
    /// word, and a sentence-ending punctuation piece — <c>▁</c> or not — ends a sentence, which wins. <b>A Han or
    /// Kana piece starts a word wherever it falls</b>: unspaced text carries no <c>▁</c> after its first piece,
    /// and read as continuations its windows could never end after <c>。</c> nor overlap.</summary>
    public static TokenBoundaries FromPieces(IReadOnlyList<string> pieces)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        var kinds = new byte[pieces.Count];
        for (var id = 0; id < kinds.Length; id++)
        {
            var bare = pieces[id].TrimStart(MetaSpace);
            kinds[id] = SentenceEnds.Contains(bare) ? SentenceEnd
                : pieces[id].StartsWith(MetaSpace) || StartsUnspacedWord(bare) ? (byte)0
                : Continuation;
        }
        return new TokenBoundaries(kinds);
    }

    /// <summary>Whether a piece opens with Han or Kana — scripts written without spaces, where every character
    /// boundary is a word boundary. Hangul is spaced, so it is not here.</summary>
    private static bool StartsUnspacedWord(string piece)
    {
        if (piece.Length == 0 || Rune.DecodeFromUtf16(piece, out var first, out _) != OperationStatus.Done) return false;
        return first.Value is (>= 0x4E00 and <= 0x9FFF) or (>= 0x3400 and <= 0x4DBF) or (>= 0xF900 and <= 0xFAFF)
            or (>= 0x20000 and <= 0x2FA1F)                                   // Han, the supplementary planes
            or (>= 0x3040 and <= 0x30FF) or (>= 0x31F0 and <= 0x31FF)        // Hiragana, Katakana
            or (>= 0xFF66 and <= 0xFF9F);                                    // halfwidth Katakana
    }

    /// <summary>Whether <paramref name="id"/> continues a word, never its first piece.</summary>
    public bool IsContinuation(int id) => Kind(id) == Continuation;

    /// <summary>Whether <paramref name="id"/> is a sentence-ending punctuation token.</summary>
    public bool EndsSentence(int id) => Kind(id) == SentenceEnd;

    private byte Kind(int id) => (uint)id < (uint)_kinds.Length ? _kinds[id] : (byte)0;
}

/// <summary>Splits a token sequence longer than a budget into windows within it, so a transformer sees every
/// token of an input rather than the first window's worth (<c>docs/DECISIONS.md</c> <b>D177</b>). Deterministic:
/// the same ids, budget and overlap always give the same windows.
///
/// <para>A token CAN START a window when it neither continues a word nor ends a sentence. A window ends after
/// the LAST sentence-ending token in its latter half that a starter follows, else before the last starter
/// there, else hard at the budget. The next window restarts inside the last
/// <see cref="InputSegmentation.Overlap"/> of the one just cut — after a sentence end, else at the earliest
/// starter — else at the cut. So consecutive windows overlap slightly, and a window starts on a
/// <c>##</c> continuation or a sentence end only after a hard cut.</para></summary>
internal static class TokenSegmenter
{
    private static readonly double DefaultOverlap = new InputSegmentation().Overlap;

    /// <summary>The windows of <paramref name="ids"/> as <c>[Start, End)</c> ranges, in order: one window, the
    /// whole sequence, when it fits the budget. A null overlap is the default one.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="budget"/> is under 1.</exception>
    public static IReadOnlyList<(int Start, int End)> Windows(
        IReadOnlyList<int> ids, int budget, TokenBoundaries boundaries, double? overlap = null)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(boundaries);
        ArgumentOutOfRangeException.ThrowIfLessThan(budget, 1);
        var reach = overlap ?? DefaultOverlap;

        var windows = new List<(int Start, int End)>();
        var start = 0;
        while (ids.Count - start > budget)
        {
            var cut = Cut(ids, start, budget, boundaries);
            windows.Add((start, cut));
            start = Restart(ids, start, cut, reach, boundaries);   // always > start, so the loop ends
        }
        windows.Add((start, ids.Count));
        return windows;
    }

    /// <summary>Where a window starting at <paramref name="start"/> ends (exclusive). The sequence runs past
    /// <c>start + budget</c>, so every index read here exists.</summary>
    private static int Cut(IReadOnlyList<int> ids, int start, int budget, TokenBoundaries boundaries)
    {
        var lo = start + Math.Max(1, budget / 2);
        var hi = start + budget;
        for (var c = hi; c >= lo; c--)
            if (AfterSentenceEnd(ids, c, boundaries)) return c;
        for (var c = hi; c >= lo; c--)
            if (CanStart(ids[c], boundaries)) return c;
        return hi;
    }

    /// <summary>Where the window after <c>[start, cut)</c> begins.</summary>
    private static int Restart(
        IReadOnlyList<int> ids, int start, int cut, double overlap, TokenBoundaries boundaries)
    {
        var from = Math.Max(start + 1, cut - (int)((cut - start) * overlap));
        for (var p = from; p < cut; p++)
            if (AfterSentenceEnd(ids, p, boundaries)) return p;
        for (var p = from; p < cut; p++)
            if (CanStart(ids[p], boundaries)) return p;
        return cut;
    }

    // inside a run like ". . ." only the position after its LAST mark qualifies
    private static bool AfterSentenceEnd(IReadOnlyList<int> ids, int p, TokenBoundaries boundaries) =>
        boundaries.EndsSentence(ids[p - 1]) && CanStart(ids[p], boundaries);

    // a sentence end belongs to the window before it, so it starts none
    private static bool CanStart(int id, TokenBoundaries boundaries) =>
        !boundaries.IsContinuation(id) && !boundaries.EndsSentence(id);
}
