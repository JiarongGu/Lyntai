using Lyntai.Storage;

namespace Lyntai.Memory;

/// <summary>A query read against the entries a recall considered: how much of it each entry covers
/// (<see cref="MemoryItem.Score"/>) and which passage of its content matched (<see cref="MemoryItem.Span"/>).
/// <para>Built once per recall over the candidate set, because a word's weight is how rare it is THERE. A word no
/// candidate holds weighs what a word every candidate holds does — almost nothing — since it cannot tell one
/// candidate from another and is as likely filler as a distinctive word nobody wrote.</para></summary>
internal sealed class MemoryQueryMatcher
{
    /// <summary>Occurrences read per word when looking for a passage; a word repeated past this adds no information a
    /// bounded window could use.</summary>
    private const int MaxOccurrences = 64;

    private readonly Term[] _terms;
    private readonly double _total;

    private MemoryQueryMatcher(Term[] terms)
    {
        _terms = terms;
        _total = terms.Sum(t => t.Weight);
    }

    private readonly record struct Term(string Text, bool Whole, double Weight);

    /// <summary>The matcher for <paramref name="query"/> over <paramref name="corpus"/> (each entry's headline and
    /// content), or null when the query has no word a search could use.</summary>
    internal static MemoryQueryMatcher? Create(string? query, IReadOnlyList<(string Headline, string Content)> corpus)
    {
        var words = new List<(string Text, bool Whole)>();
        foreach (var id in MemoryIdentifiers.Extract(query, int.MaxValue)) words.Add((id, true));
        foreach (var term in SearchTerms.SubstringTerms(query))
            if (!words.Exists(w => string.Equals(w.Text, term, StringComparison.OrdinalIgnoreCase)))
                words.Add((term, false));
        if (words.Count == 0) return null;

        var terms = new Term[words.Count];
        for (var i = 0; i < terms.Length; i++)
        {
            var (text, whole) = words[i];
            var held = 0;
            foreach (var (headline, content) in corpus)
                if (Holds(headline, text, whole) || Holds(content, text, whole)) held++;
            terms[i] = new Term(text, whole, Idf(held == 0 ? corpus.Count : held, corpus.Count));
        }
        return new MemoryQueryMatcher(terms);
    }

    /// <summary>The share of the query's weight this entry holds, 0..1.</summary>
    internal double Score(string headline, string content)
    {
        var held = 0.0;
        foreach (var t in _terms)
            if (Holds(headline, t.Text, t.Whole) || Holds(content, t.Text, t.Whole)) held += t.Weight;
        return _total <= 0 ? 0 : Math.Clamp(held / _total, 0, 1);
    }

    /// <summary>The window of <paramref name="content"/>, at most <paramref name="chars"/> long, holding the most weight
    /// of the query's words; the earliest on a tie. Null when the content holds none.</summary>
    internal MemorySpan? Span(string content, int chars)
    {
        var hits = new List<(int At, int End, int Term)>();
        for (var i = 0; i < _terms.Length; i++)
        {
            var seen = 0;
            foreach (var at in Positions(content, _terms[i].Text, _terms[i].Whole))
            {
                hits.Add((at, at + _terms[i].Text.Length, i));
                if (++seen >= MaxOccurrences) break;
            }
        }
        if (hits.Count == 0) return null;
        hits.Sort((a, b) => a.At.CompareTo(b.At));

        var inside = new int[_terms.Length];
        var weight = 0.0;
        var (bestWeight, bestFrom, bestTo) = (-1.0, 0, 0);
        var to = 0;
        for (var from = 0; from < hits.Count; from++)
        {
            while (to < hits.Count && hits[to].End - hits[from].At <= chars)
            {
                if (inside[hits[to].Term]++ == 0) weight += _terms[hits[to].Term].Weight;
                to++;
            }
            if (to == from)
            {
                // the word alone is longer than the window; it is still the match
                to = from + 1;
                inside[hits[from].Term]++;
                weight += _terms[hits[from].Term].Weight;
            }
            if (weight > bestWeight) (bestWeight, bestFrom, bestTo) = (weight, from, to);

            if (--inside[hits[from].Term] == 0) weight -= _terms[hits[from].Term].Weight;
        }

        var matchStart = hits[bestFrom].At;
        var matchEnd = hits.Skip(bestFrom).Take(bestTo - bestFrom).Max(h => h.End);
        return Cut(content, matchStart, matchEnd, chars);
    }

    /// <summary>Places a window of <paramref name="chars"/> around the match with a third of the spare room before it, then
    /// moves each edge to a word boundary where one is near and the match stays inside.</summary>
    private static MemorySpan Cut(string content, int matchStart, int matchEnd, int chars)
    {
        var spare = Math.Max(0, chars - (matchEnd - matchStart));
        var start = Math.Max(0, matchStart - spare / 3);
        var end = Math.Min(content.Length, Math.Max(matchEnd, start + chars));
        if (end - start > chars) end = start + chars;
        if (end - start < chars) start = Math.Max(0, end - chars);
        if (end < matchEnd) end = matchEnd;
        if (start > matchStart) start = matchStart;

        // a line start is the best place to begin, a space the next best; the end gives way to a space only
        if (start > 0)
        {
            var line = content.LastIndexOf('\n', matchStart > 0 ? matchStart - 1 : 0, Math.Max(1, matchStart - start));
            if (line >= start) start = line + 1;
            else
            {
                var space = content.IndexOf(' ', start, Math.Max(0, Math.Min(32, matchStart - start)));
                if (space >= 0) start = space + 1;
            }
        }
        if (end < content.Length)
        {
            var floor = Math.Max(matchEnd, end - 32);
            var space = floor < end ? content.LastIndexOf(' ', end - 1, end - floor) : -1;
            if (space >= floor) end = space;
        }

        if (start > 0 && char.IsLowSurrogate(content[start])) start--;
        if (end < content.Length && char.IsLowSurrogate(content[end])) end--;
        return new MemorySpan(content[start..end], start, content.Length);
    }

    private static bool Holds(string text, string word, bool whole) =>
        whole ? MemoryIdentifiers.Find(text, word).Any() : text.Contains(word, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<int> Positions(string text, string word, bool whole)
    {
        if (whole)
        {
            foreach (var at in MemoryIdentifiers.Find(text, word)) yield return at;
            yield break;
        }

        var from = 0;
        while (from < text.Length)
        {
            var at = text.IndexOf(word, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0) yield break;
            yield return at;
            from = at + 1;
        }
    }

    /// <summary>Robertson and Spärck Jones' inverse document frequency, kept positive: a word in every document is nearly
    /// nothing, never negative.</summary>
    private static double Idf(int held, int total) => Math.Log(1 + (total - held + 0.5) / (held + 0.5));
}
