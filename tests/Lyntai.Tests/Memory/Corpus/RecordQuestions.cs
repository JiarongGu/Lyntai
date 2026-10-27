using System.Text.RegularExpressions;

namespace Lyntai.Tests.Memory.Corpus;

/// <summary>One indexed piece of a record: consecutive sections packed up to a size, never beyond the record.
/// The record's title, a fix's Symptom and an amendment's headline are NOT in <see cref="Text"/> — they are what
/// the questions are made of, so indexing them would let a question find itself.</summary>
public sealed record CorpusChunk(string RecordKey, int FirstSection, int LastSection, IReadOnlyList<string> Labels, string Text);

/// <summary>Records → chunks, in the order the memory should learn them: oldest first, so decay by interference
/// acts on the archive the way it would on a log written over time.</summary>
public static class RecordChunker
{
    /// <summary>The section kinds held out of the index because a question quotes them.</summary>
    public static readonly IReadOnlySet<string> HeldLabels = new HashSet<string> { "symptom" };

    /// <summary>Chunks every record. A section longer than <paramref name="maxChars"/> is cut at the last
    /// whitespace before the limit; a section shorter than <paramref name="minChars"/> joins the one after it.</summary>
    public static IReadOnlyList<CorpusChunk> Chunk(IEnumerable<CorpusRecord> records, int maxChars = 1000, int minChars = 250)
    {
        var chunks = new List<CorpusChunk>();
        foreach (var record in records.OrderBy(r => r.Date ?? DateOnly.MinValue))
        {
            var pending = new List<(int Index, string Label, string Text)>();
            void Emit()
            {
                if (pending.Count == 0) return;
                chunks.Add(new CorpusChunk(record.Key, pending[0].Index, pending[^1].Index,
                    pending.Select(p => p.Label).Distinct().ToList(), string.Join("\n\n", pending.Select(p => p.Text))));
                pending.Clear();
            }

            for (var i = 0; i < record.Sections.Count; i++)
            {
                var section = record.Sections[i];
                if (HeldLabels.Contains(section.Label)) continue;
                foreach (var piece in Split(Render(section), maxChars))
                {
                    var size = pending.Sum(p => p.Text.Length);
                    if (pending.Count > 0 && size + piece.Length > maxChars) Emit();
                    pending.Add((i, section.Label, piece));
                    if (pending.Sum(p => p.Text.Length) >= minChars) Emit();
                }
            }

            Emit();
        }

        return chunks;
    }

    /// <summary>A section as indexed: its tag, then its body. An amendment's headline is dropped.</summary>
    public static string Render(RecordSection section) =>
        section.Tag.Length == 0 ? section.Body : $"{section.Tag}. {section.Body}".Trim();

    private static IEnumerable<string> Split(string text, int max)
    {
        text = text.Trim();
        while (text.Length > max)
        {
            var cut = text.LastIndexOfAny([' ', '\n'], max);
            if (cut < max / 2) cut = max;
            yield return text[..cut].Trim();
            text = text[cut..].Trim();
        }

        if (text.Length > 0) yield return text;
    }
}

public enum QuestionKind
{
    /// <summary>A fix's symptom; the answer is the entry whose root cause, fix and verification explain it.</summary>
    FixSymptom,

    /// <summary>A decision's title; the answer is the section naming what it rejected.</summary>
    DecisionRejected,

    /// <summary>An amendment's headline; the answer is the amending paragraph, and the original text is the stale answer.</summary>
    AmendmentChange,

    /// <summary>The title of a decision another one amends; the answer holds both ends.</summary>
    AmenderOfDecision,

    /// <summary>An archived task's title; the answer is that entry.</summary>
    ArchiveFeature,

    /// <summary>A decision's title; the answers are the archive entries that cite it.</summary>
    ArchiveDelivered,
}

/// <summary>Something a ranked chunk can be: <paramref name="Role"/> names what finding it MEANS. A section index
/// narrows it to one paragraph, a label to a kind of section, and <paramref name="Not"/> inverts the label —
/// the original text an amendment replaced.</summary>
public sealed record GoldRef(string Role, string RecordKey, int? Section = null, string? Label = null, bool Not = false)
{
    /// <summary>True when <paramref name="c"/> is this gold.</summary>
    public bool Matches(CorpusChunk c) =>
        c.RecordKey == RecordKey
        && (Section is not { } s || (c.FirstSection <= s && s <= c.LastSection))
        && (Label is null || c.Labels.Contains(Label) != Not);
}

/// <summary>A question with its gold. The FIRST gold role is the primary one — what recall and precision count.</summary>
public sealed record CorpusQuestion(int Id, QuestionKind Kind, string Text, IReadOnlyList<GoldRef> Gold)
{
    /// <summary>The role recall and precision count.</summary>
    public string Primary => Gold[0].Role;
}

/// <summary>Records → questions, deterministic for a seed. Built from the records' structure alone, never from a
/// model, so a run costs nothing and two runs ask the same things.</summary>
public static class QuestionBuilder
{
    /// <summary>Up to <paramref name="perKind"/> questions of each kind, sampled with <paramref name="seed"/>.</summary>
    public static IReadOnlyList<CorpusQuestion> Build(IReadOnlyList<CorpusRecord> records, int perKind, int seed)
    {
        var byKey = records.ToDictionary(r => r.Key);
        var all = new List<CorpusQuestion>();
        var next = 0;
        void Add(QuestionKind kind, string text, params GoldRef[] gold) =>
            all.Add(new CorpusQuestion(next++, kind, text, gold));

        var candidates = new Dictionary<QuestionKind, List<CorpusQuestion>>();
        foreach (var kind in Enum.GetValues<QuestionKind>()) candidates[kind] = [];

        foreach (var r in records)
        {
            switch (r.Kind)
            {
                case RecordKind.Fix:
                    if (r.Sections.FirstOrDefault(s => s.Label == "symptom") is { } symptom && symptom.Body.Length >= 40)
                        Add(QuestionKind.FixSymptom, Trim(symptom.Body, 300), new GoldRef("answer", r.Key));
                    break;
                case RecordKind.Decision:
                    if (r.Sections.Any(s => s.Label == "rejected"))
                        Add(QuestionKind.DecisionRejected, $"Which alternatives were rejected for: {r.Title}",
                            new GoldRef("section", r.Key, Label: "rejected"), new GoldRef("document", r.Key));
                    for (var i = 0; i < r.Sections.Count; i++)
                        if (r.Sections[i].IsAmendment && WordCount(r.Sections[i].Headline) >= 5)
                            Add(QuestionKind.AmendmentChange, r.Sections[i].Headline,
                                new GoldRef("amendment", r.Key, Section: i),
                                new GoldRef("stale", r.Key, Label: "amended", Not: true));
                    foreach (var target in r.Amends.Where(byKey.ContainsKey))
                        Add(QuestionKind.AmenderOfDecision, $"What is the current rule on: {byKey[target].Title}",
                            new GoldRef("amender", r.Key), new GoldRef("original", target));
                    var delivered = records.Where(a => a.Kind == RecordKind.Archive && a.Cites.Contains(r.Key)).ToList();
                    if (delivered.Count is > 0 and <= 20)
                        Add(QuestionKind.ArchiveDelivered, $"Which tasks delivered: {r.Title}",
                            [.. delivered.Select(a => new GoldRef("answer", a.Key))]);
                    break;
                case RecordKind.Archive:
                    if (WordCount(r.Title) >= 5) Add(QuestionKind.ArchiveFeature, r.Title, new GoldRef("answer", r.Key));
                    break;
            }
        }

        foreach (var q in all) candidates[q.Kind].Add(q);
        var rng = new Random(seed);
        var picked = candidates.OrderBy(c => c.Key)
            .SelectMany(c => c.Value.OrderBy(_ => rng.Next()).Take(perKind));
        return [.. picked.Select((q, i) => q with { Id = i })];
    }

    private static int WordCount(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static string Trim(string text, int max)
    {
        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length <= max) return text;
        var cut = text.LastIndexOf(' ', max);
        return text[..(cut > 0 ? cut : max)];
    }
}

/// <summary>What one recall scored: the 1-based rank of the first chunk matching each role (0 = not in the
/// list), and for the primary role which ranks matched and how many distinct gold records were reached.</summary>
public sealed record QuestionOutcome(
    IReadOnlyDictionary<string, int> FirstRank, IReadOnlyList<bool> PrimaryAtRank, IReadOnlyDictionary<string, int> PrimaryRecordRank)
{
    /// <summary>True when the primary role is found within the first <paramref name="k"/> chunks.</summary>
    public bool Hit(int k) => PrimaryAtRank.Take(k).Any(x => x);

    /// <summary>Reciprocal rank of the first primary hit (0 when none).</summary>
    public double Reciprocal => PrimaryAtRank.ToList().FindIndex(x => x) is var i and >= 0 ? 1.0 / (i + 1) : 0;

    /// <summary>Fraction of the first <paramref name="k"/> chunks that are primary gold.</summary>
    public double Precision(int k) => k == 0 ? 0 : PrimaryAtRank.Take(k).Count(x => x) / (double)k;

    /// <summary>Distinct gold records reached within <paramref name="k"/>, over the smaller of k and the gold count.</summary>
    public double Recall(int k, int goldRecords) =>
        PrimaryRecordRank.Values.Count(rank => rank is > 0 && rank <= k) / (double)Math.Min(k, Math.Max(1, goldRecords));
}

/// <summary>Scores a ranked chunk list against a question's gold.</summary>
public static class RecallScoring
{
    /// <summary>The outcome of <paramref name="ranked"/> (best first) for <paramref name="q"/>.</summary>
    public static QuestionOutcome Score(CorpusQuestion q, IReadOnlyList<CorpusChunk> ranked)
    {
        var first = q.Gold.Select(g => g.Role).Distinct().ToDictionary(r => r, _ => 0);
        var primary = q.Gold.Where(g => g.Role == q.Primary).ToList();
        var recordRank = primary.Select(g => g.RecordKey).Distinct().ToDictionary(k => k, _ => 0);
        var atRank = new bool[ranked.Count];

        for (var i = 0; i < ranked.Count; i++)
        {
            foreach (var g in q.Gold.Where(g => g.Matches(ranked[i])))
            {
                if (first[g.Role] == 0) first[g.Role] = i + 1;
                if (g.Role != q.Primary) continue;
                atRank[i] = true;
                if (recordRank[g.RecordKey] == 0) recordRank[g.RecordKey] = i + 1;
            }
        }

        return new QuestionOutcome(first, atRank, recordRank);
    }

    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "with", "that", "this", "from", "its", "are", "was", "not", "but", "has", "have", "into",
        "when", "which", "what", "who", "how", "why", "any", "all", "can", "one", "per", "then", "than", "only",
    };

    /// <summary>The share of the query's content words that occur in the gold text — how much of the question a
    /// word match could possibly use. Low means the answer is phrased in other words.</summary>
    public static double Overlap(string query, IEnumerable<string> goldTexts)
    {
        static HashSet<string> Words(string text) => Regex.Matches(text.ToLowerInvariant(), @"[a-z0-9]{3,}")
            .Select(m => m.Value).Where(w => !Stop.Contains(w)).ToHashSet(StringComparer.Ordinal);

        var q = Words(query);
        if (q.Count == 0) return 0;
        var gold = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in goldTexts) gold.UnionWith(Words(t));
        return q.Count(gold.Contains) / (double)q.Count;
    }
}
