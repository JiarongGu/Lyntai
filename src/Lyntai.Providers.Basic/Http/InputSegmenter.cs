using System.Globalization;
using System.Text;
using Lyntai.Inference;

namespace Lyntai.Providers.Http;

/// <summary>Inputs split for sending: every piece in input order, and which pieces belong to which input.</summary>
/// <param name="Inputs">The inputs as the caller gave them.</param>
/// <param name="Pieces">What to send — <paramref name="Inputs"/> itself when none exceeds the budget.</param>
/// <param name="First">Input <c>i</c>'s pieces are <c>Pieces[First[i]..First[i + 1]]</c>.</param>
internal sealed record Segmentation(IReadOnlyList<string> Inputs, IReadOnlyList<string> Pieces, int[] First)
{
    /// <summary>Whether input <paramref name="i"/> went as several pieces, whose answers are combined.</summary>
    public bool IsSegmented(int i) => First[i + 1] - First[i] > 1;

    /// <summary>How many inputs went as several pieces.</summary>
    public int Segmented { get; } = Enumerable.Range(0, Inputs.Count).Count(i => First[i + 1] - First[i] > 1);
}

/// <summary>Splits an input longer than a character budget into pieces within it, so a small-window backend
/// is sent every part of the text rather than rejecting the call or losing the tail — or, truncating, keeps
/// only the first of those pieces. Deterministic: the same input, budget and overlap always give the same
/// pieces.
///
/// <para><b>A budget counts characters after NFKC normalisation</b> — summed one text element at a time to
/// decide whether an input fits (<see cref="Measure"/>), one code point at a time to place a cut, each an
/// upper bound — because a tokenizer normalises before it counts and a compatibility character can expand
/// several-fold. Pieces are still cut from, and sent as, the original text. A cut falls between text
/// elements while one fits the budget, and otherwise inside the element at a code point, never inside a
/// surrogate pair: only a single code point that alone outcounts the budget can make a piece exceed it.</para>
///
/// <para>Each cut is the LAST boundary in the window's latter half — a blank line, else a line break, else a
/// sentence end, else whitespace — or a hard cut at the budget when there is none. The next piece restarts at
/// the earliest sentence end, else whitespace, inside the last <see cref="InputSegmentation.Overlap"/> of the
/// piece just cut, so consecutive pieces overlap slightly. Pieces are trimmed and an empty one dropped; an
/// input within the budget is one piece, the input itself.</para></summary>
internal static class InputSegmenter
{
    private static readonly InputSegmentation Defaults = new();

    private static readonly Func<string, int, bool>[] CutPreference =
        [IsBlankLine, IsLineBreak, IsSentenceEnd, IsWhitespace];

    /// <summary>Throws when a <c>MaxInputChars</c> bound cannot hold a piece: it is not positive, or, for an
    /// embedder, a role prefix every piece carries leaves no room for text. Null bounds nothing.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The bound cannot hold a piece.</exception>
    public static void ValidateBound(int? maxInputChars, bool embeds, string? documentPrefix, string? queryPrefix)
    {
        const string name = nameof(HttpModelOptions.MaxInputChars);
        if (maxInputChars is not { } max) return;
        if (max <= 0) throw new ArgumentOutOfRangeException(name, max, $"{name} must be positive.");
        if (!embeds) return;
        (string Name, string? Value)[] prefixes =
        [
            (nameof(HttpModelOptions.DocumentPrefix), documentPrefix),
            (nameof(HttpModelOptions.QueryPrefix), queryPrefix),
        ];
        foreach (var (prefixName, prefix) in prefixes)
            if (prefix is not null && Measure(prefix) is var length && length >= max)
                throw new ArgumentOutOfRangeException(name, max,
                    $"{name} ({max}) leaves no room for text after {prefixName} ({length} characters): every "
                    + "piece carries the prefix.");
    }

    /// <summary>The length a bound counts: characters after NFKC normalisation, summed one text element at a
    /// time. That is never below the NFKC length of the whole text — composing across elements only shortens
    /// it — and it takes one pass. Text already in NFKC is its own length.</summary>
    public static int Measure(string text)
    {
        if (IsNormal(text)) return text.Length;
        var total = 0;
        for (var i = 0; i < text.Length;)
        {
            var length = StringInfo.GetNextTextElementLength(text.AsSpan(i));
            total += Weigh(text, i, length);
            i += length;
        }
        return total;
    }

    /// <summary>The query a reranker sends when one window of <paramref name="window"/> characters holds it
    /// and a document: whole while it keeps within its share — the window less the document's
    /// <paramref name="minDocumentShare"/> of it — else cut to that share where its first piece would end, at a
    /// word boundary when one falls in the share's latter half.</summary>
    public static string QueryWithin(string query, int window, double minDocumentShare)
    {
        var most = window - DocumentShare(window, minDocumentShare);
        if (Measure(query) <= most) return query;
        if (most < 1) return string.Empty;
        var kept = Truncate(query, most);
        // only a first code point that alone outcounts the share can overrun it, and then nothing of it fits
        return Measure(kept) <= most ? kept : string.Empty;
    }

    /// <summary>What a document keeps of a pair window: its share, rounded up — in decimal, so 0.8 of 60 is
    /// 48 rather than a binary 48.000…01 that rounds to 49 — and never less than one character, which a share
    /// below decimal's range would otherwise round to.</summary>
    public static int DocumentShare(int window, double minDocumentShare) =>
        Math.Max(1, (int)Math.Ceiling((decimal)minDocumentShare * window));

    /// <summary>Segment every input against one budget, keeping at most <paramref name="maxPieces"/> pieces of
    /// each (<see cref="Spread{T}"/>); a null overlap is the default one.</summary>
    public static Segmentation Segment(
        IReadOnlyList<string> inputs, int budget, double? overlap = null, int? maxPieces = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(budget, 1);
        var first = new int[inputs.Count + 1];
        if (inputs.All(t => Measure(t) <= budget))
        {
            for (var i = 0; i <= inputs.Count; i++) first[i] = i;
            return new Segmentation(inputs, inputs, first);
        }

        var pieces = new List<string>(inputs.Count);
        for (var i = 0; i < inputs.Count; i++)
        {
            first[i] = pieces.Count;
            pieces.AddRange(Spread(Split(inputs[i], budget, overlap), maxPieces));
        }
        first[inputs.Count] = pieces.Count;
        return new Segmentation(inputs, pieces, first);
    }

    /// <summary>The pieces of one input, each counting at most <paramref name="budget"/>.</summary>
    public static IReadOnlyList<string> Split(string input, int budget, double? overlap = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(budget, 1);
        if (Measure(input) <= budget) return [input];

        var spans = Spans(input, budget, overlap);
        List<string> pieces = [.. spans
            .Select(s => input[s.Start..s.End].Trim())
            .Where(p => p.Length > 0)];
        // all whitespace: one piece keeps the input answered, and whitespace loses nothing when cut
        return pieces.Count > 0 ? pieces : [input[spans[0].Start..spans[0].End]];
    }

    /// <summary>An input within the budget as given; a longer one cut where its first piece would end, its
    /// trailing whitespace trimmed.</summary>
    public static string Truncate(string input, int budget)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(budget, 1);
        if (Measure(input) <= budget) return input;
        var head = input[..Cut(input, Count.Of(input), 0, budget)];
        var trimmed = head.TrimEnd();
        return trimmed.Length > 0 ? trimmed : head;
    }

    /// <summary>At most <paramref name="cap"/> of <paramref name="pieces"/>: the first, the last, and the rest
    /// at even steps between — piece <c>round(i·(n−1)/(cap−1))</c>, halves rounded up — or the first alone for
    /// a cap of 1. Every piece, as given, when there is no cap or the pieces are within it.</summary>
    public static IReadOnlyList<T> Spread<T>(IReadOnlyList<T> pieces, int? cap)
    {
        if (cap is not { } most || pieces.Count <= most) return pieces;
        if (most == 1) return [pieces[0]];
        var kept = new T[most];
        for (var i = 0; i < most; i++)
            kept[i] = pieces[(int)((2L * i * (pieces.Count - 1) + (most - 1)) / (2L * (most - 1)))];
        return kept;
    }

    /// <summary>The untrimmed character ranges <see cref="Split"/> takes its pieces from.</summary>
    internal static IReadOnlyList<(int Start, int End)> Spans(string input, int budget, double? overlap = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(budget, 1);
        var reach = overlap ?? Defaults.Overlap;
        var count = Count.Of(input);
        var spans = new List<(int Start, int End)>();
        var start = 0;
        while (true)
        {
            if (count.Between(start, input.Length) <= budget)
            {
                // empty only when a code point overran the budget and the cut already reached the end
                if (start < input.Length || spans.Count == 0) spans.Add((start, input.Length));
                return spans;
            }
            var cut = Cut(input, count, start, budget);
            spans.Add((start, cut));
            start = Restart(input, count, start, cut, reach);   // always > start, so the loop ends
        }
    }

    /// <summary>Where a piece starting at <paramref name="start"/> ends (exclusive): at a text-element
    /// boundary while one fits, else inside the element at a code point, never inside a surrogate pair.</summary>
    private static int Cut(string s, Count count, int start, int budget)
    {
        var hi = Furthest(s, count, start, budget, count.IsElement);
        if (hi == start)
        {
            // one element outcounts the whole budget: cut it at a code point rather than send it whole
            hi = Furthest(s, count, start, budget, count.IsCodePoint);
            // and a single code point that alone outcounts the budget is the one piece that can exceed it
            return hi > start ? hi : count.NextCodePoint(start);
        }

        var half = Math.Max(1, budget / 2);
        foreach (var boundary in CutPreference)
            for (var c = hi; c > start; c--)
            {
                if (!count.IsElement(c)) continue;
                if (count.Between(start, c) < half) break;
                if (boundary(s, c)) return c;
            }
        return hi;
    }

    /// <summary>The furthest position after <paramref name="start"/> that <paramref name="allowed"/> admits
    /// and whose piece counts within the budget; <paramref name="start"/> itself when none does.</summary>
    private static int Furthest(string s, Count count, int start, int budget, Func<int, bool> allowed)
    {
        var hi = start;
        for (var c = start + 1; c <= s.Length; c++)
        {
            if (!allowed(c)) continue;
            if (count.Between(start, c) > budget) break;
            hi = c;
        }
        return hi;
    }

    /// <summary>Where the piece after <c>[start, cut)</c> begins: inside its last <paramref name="overlap"/>
    /// when a boundary is there, else at the cut.</summary>
    private static int Restart(string s, Count count, int start, int cut, double overlap)
    {
        var reach = (int)(count.Between(start, cut) * overlap);
        var from = cut;
        for (var p = cut - 1; p > start; p--)
        {
            if (!count.IsElement(p)) continue;
            if (count.Between(p, cut) > reach) break;
            from = p;
        }
        for (var p = from; p < cut; p++)
            if (count.IsElement(p) && IsSentenceEnd(s, p)) return p;
        for (var p = from; p < cut; p++)
            if (count.IsElement(p) && IsWhitespace(s, p)) return p;
        return cut;
    }

    /// <summary>The NFKC length of <c>text[start..start + length]</c>, one text element or one code point.
    /// Ill-formed UTF-16 has no normal form, so it counts as it stands.</summary>
    private static int Weigh(string text, int start, int length)
    {
        if (length == 1 && text[start] < 0x80) return 1;
        try
        {
            return text.Substring(start, length).Normalize(NormalizationForm.FormKC).Length;
        }
        catch (ArgumentException)
        {
            return length;
        }
    }

    private static bool IsNormal(string text)
    {
        try
        {
            return text.IsNormalized(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>One text counted CODE POINT by code point, each at its own NFKC length — never less than the
    /// NFKC length of any piece, whose normal form never outgrows its parts — with its text elements marked, so
    /// a cut prefers them. <c>at[i]</c> is the count of <c>text[..i]</c> where a code point starts at <c>i</c>,
    /// and at the end; −1 inside a surrogate pair.</summary>
    private sealed class Count(int[] at, bool[] element)
    {
        public bool IsCodePoint(int i) => at[i] >= 0;

        public bool IsElement(int i) => element[i];

        public int Between(int from, int to) => at[to] - at[from];

        public int NextCodePoint(int i)
        {
            do i++;
            while (at[i] < 0);
            return i;
        }

        public static Count Of(string text)
        {
            var at = new int[text.Length + 1];
            var element = new bool[text.Length + 1];
            Array.Fill(at, -1);
            var normal = IsNormal(text);
            var total = 0;
            for (var i = 0; i < text.Length;)
            {
                element[i] = true;
                var end = i + StringInfo.GetNextTextElementLength(text.AsSpan(i));
                while (i < end)
                {
                    at[i] = total;
                    var width = char.IsSurrogatePair(text, i) ? 2 : 1;
                    // text already in NFKC is its own length, code point by code point
                    total += normal ? width : Weigh(text, i, width);
                    i += width;
                }
            }
            at[text.Length] = total;
            element[text.Length] = true;
            return new Count(at, element);
        }
    }

    // Each predicate asks whether a boundary falls just before index c, i.e. after s[c - 1].

    private static bool IsBlankLine(string s, int c)
    {
        if (s[c - 1] != '\n') return false;
        for (var j = c - 2; j >= 0; j--)
        {
            if (s[j] == '\n') return true;
            if (s[j] is not (' ' or '\t' or '\r')) return false;
        }
        return false;
    }

    private static bool IsLineBreak(string s, int c) => s[c - 1] == '\n';

    private static bool IsSentenceEnd(string s, int c) => s[c - 1] switch
    {
        '。' or '！' or '？' or '；' => true,
        '.' or '!' or '?' or ';' => c < s.Length && char.IsWhiteSpace(s[c]),
        _ => false,
    };

    private static bool IsWhitespace(string s, int c) => char.IsWhiteSpace(s[c - 1]);
}
