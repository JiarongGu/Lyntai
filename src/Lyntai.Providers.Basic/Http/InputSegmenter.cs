namespace Lyntai.Providers.Http;

/// <summary>Inputs split for sending: every piece in input order, and which pieces belong to which input.</summary>
/// <param name="Inputs">The inputs as the caller gave them.</param>
/// <param name="Pieces">What to send — <paramref name="Inputs"/> itself when none exceeds the budget.</param>
/// <param name="First">Input <c>i</c>'s pieces are <c>Pieces[First[i]..First[i + 1]]</c>.</param>
/// <param name="Budget">The most characters a piece may carry.</param>
internal sealed record Segmentation(IReadOnlyList<string> Inputs, IReadOnlyList<string> Pieces, int[] First, int Budget)
{
    /// <summary>Whether input <paramref name="i"/> was longer than the budget, and so was split.</summary>
    public bool IsSegmented(int i) => Inputs[i].Length > Budget;

    /// <summary>How many inputs were split.</summary>
    public int Segmented { get; } = Inputs.Count(t => t.Length > Budget);
}

/// <summary>Splits an input longer than a character budget into pieces within it, so a small-window backend
/// is sent every part of the text rather than rejecting the call or losing the tail. Deterministic: the same
/// input and budget always give the same pieces.
///
/// <para>Each cut is the LAST boundary in the window's latter half — a blank line, else a line break, else a
/// sentence end, else whitespace — or a hard cut at the budget when there is none, never between the halves
/// of a surrogate pair. The next piece restarts at the earliest sentence end, else whitespace, inside the
/// last 15% of the piece just cut, so consecutive pieces overlap slightly. Pieces are trimmed and an empty
/// one dropped; an input within the budget is one piece, the input itself.</para></summary>
internal static class InputSegmenter
{
    private const double Overlap = 0.15;

    private static readonly Func<string, int, bool>[] CutPreference =
        [IsBlankLine, IsLineBreak, IsSentenceEnd, IsWhitespace];

    /// <summary>Segment every input against one budget.</summary>
    public static Segmentation Segment(IReadOnlyList<string> inputs, int budget)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(budget, 1);
        var first = new int[inputs.Count + 1];
        if (inputs.All(t => t.Length <= budget))
        {
            for (var i = 0; i <= inputs.Count; i++) first[i] = i;
            return new Segmentation(inputs, inputs, first, budget);
        }

        var pieces = new List<string>(inputs.Count);
        for (var i = 0; i < inputs.Count; i++)
        {
            first[i] = pieces.Count;
            pieces.AddRange(Split(inputs[i], budget));
        }
        first[inputs.Count] = pieces.Count;
        return new Segmentation(inputs, pieces, first, budget);
    }

    /// <summary>The pieces of one input, each at most <paramref name="budget"/> characters.</summary>
    public static IReadOnlyList<string> Split(string input, int budget)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(budget, 1);
        if (input.Length <= budget) return [input];

        List<string> pieces = [.. Spans(input, budget)
            .Select(s => input[s.Start..s.End].Trim())
            .Where(p => p.Length > 0)];
        // all whitespace: one piece keeps the input answered, and whitespace loses nothing when cut
        return pieces.Count > 0 ? pieces : [input[..budget]];
    }

    /// <summary>The untrimmed character ranges <see cref="Split"/> takes its pieces from.</summary>
    internal static IReadOnlyList<(int Start, int End)> Spans(string input, int budget)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(budget, 1);
        var spans = new List<(int Start, int End)>();
        var start = 0;
        while (true)
        {
            if (input.Length - start <= budget)
            {
                // empty only when a pair overran a one-character budget and the cut already reached the end
                if (start < input.Length || spans.Count == 0) spans.Add((start, input.Length));
                return spans;
            }
            var cut = Cut(input, start, budget);
            spans.Add((start, cut));
            start = Restart(input, start, cut);   // always > start, so the loop ends
        }
    }

    /// <summary>Where a piece starting at <paramref name="start"/> ends (exclusive).</summary>
    private static int Cut(string s, int start, int budget)
    {
        var lo = start + Math.Max(1, budget / 2);
        var hi = start + budget;
        foreach (var boundary in CutPreference)
            for (var c = hi; c >= lo; c--)
                if (boundary(s, c)) return c;

        var hard = hi;
        if (char.IsHighSurrogate(s[hard - 1]) && char.IsLowSurrogate(s[hard])) hard--;
        // a one-character budget cannot hold a pair: the pair goes whole rather than split
        return hard > start ? hard : start + 2;
    }

    /// <summary>Where the piece after <c>[start, cut)</c> begins: inside its last 15% when a boundary is
    /// there, else at the cut.</summary>
    private static int Restart(string s, int start, int cut)
    {
        var from = Math.Max(start + 1, cut - (int)((cut - start) * Overlap));
        for (var p = from; p < cut; p++)
            if (IsSentenceEnd(s, p)) return p;
        for (var p = from; p < cut; p++)
            if (IsWhitespace(s, p)) return p;
        return cut;
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
