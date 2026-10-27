using System.Text.RegularExpressions;

namespace Lyntai.Tests.Memory.Corpus;

/// <summary>The three record shapes the corpus bench reads: a numbered decision (one file each), a fix-log entry
/// and a task-archive entry. Parsed from Markdown by the SHAPE of its headings, so any repository keeping
/// records in these shapes can be measured — the bench itself carries none of their text.</summary>
public enum RecordKind { Decision, Fix, Archive }

/// <summary>One labelled run of a record. <paramref name="Label"/> is the lower-cased lead word
/// (<c>decision</c>, <c>why</c>, <c>rejected</c>, <c>amended</c>, <c>symptom</c>, <c>root</c>…); a section with
/// no lead is <c>body</c>. <paramref name="Headline"/> is the part of a lead that states WHAT the section says
/// (an amendment's one-line claim) as opposed to <paramref name="Tag"/>, which only names the section.</summary>
public sealed record RecordSection(string Label, string Tag, string Headline, string Body)
{
    /// <summary>A later dated change to the record it sits in: an <c>Amended …</c> paragraph or a <c>Note</c> heading.</summary>
    public bool IsAmendment => Label is "amended" or "note";
}

/// <summary>A record: its key, title, date, the decisions its heading cites, the decisions it says it amends,
/// and its sections in order.</summary>
public sealed record CorpusRecord(
    string Key, RecordKind Kind, string Title, DateOnly? Date,
    IReadOnlyList<string> Cites, IReadOnlyList<string> Amends, IReadOnlyList<RecordSection> Sections);

/// <summary>Markdown → <see cref="CorpusRecord"/>. Every parser is total: a heading it cannot read is skipped,
/// never thrown on, because a real record set always holds a stray one.</summary>
public static partial class RecordParser
{
    [GeneratedRegex(@"^##\s+D(?<n>\d+)\s+[—–-]\s+(?<title>.+?)(?:\s+\((?<date>\d{4}-\d{2}-\d{2})\))?\s*$")]
    private static partial Regex DecisionHeading();

    [GeneratedRegex(@"^##\s+(?<date>\d{4}-\d{2}-\d{2})\s+[—–-]\s+(?<title>.+?)(?:\s+\((?<id>[A-Za-z0-9._-]+)\))?\s*$")]
    private static partial Regex FixHeading();

    [GeneratedRegex(@"^##\s+(?<head>.+?)\s*$")]
    private static partial Regex AnyHeading();

    [GeneratedRegex(@"\((?<date>\d{4}-\d{2}-\d{2})(?<rest>[^)]*)\)\s*$")]
    private static partial Regex TrailingDate();

    [GeneratedRegex(@"^\*\*(?<lead>.+?)\*\*\s*(?<rest>.*)$", RegexOptions.Singleline)]
    private static partial Regex BoldLead();

    [GeneratedRegex(@"^-\s+\*\*(?<label>[^*:]+):\*\*\s*(?<rest>.*)$", RegexOptions.Singleline)]
    private static partial Regex FieldBullet();

    [GeneratedRegex(@"\bD(?<n>\d+)\b")]
    private static partial Regex DecisionRef();

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}")]
    private static partial Regex IsoDate();

    /// <summary>One decision file: the <c>## D&lt;n&gt; — title (date)</c> heading, its lead-labelled paragraphs, and
    /// each later <c>## Note, …</c> heading as an amendment section. Null when no decision heading is found.</summary>
    public static CorpusRecord? Decision(string markdown)
    {
        var parts = SplitHeadings(markdown);
        var first = parts.FirstOrDefault(p => DecisionHeading().IsMatch(p.Heading));
        if (first.Heading is null) return null;

        var m = DecisionHeading().Match(first.Heading);
        var key = "D" + m.Groups["n"].Value;
        var sections = new List<RecordSection>();
        AddLeadSections(first.Body, sections);

        foreach (var note in parts.Where(p => p.Heading != first.Heading && p.Heading.StartsWith("## Note", StringComparison.Ordinal)))
        {
            var head = note.Heading[3..].Trim();
            var cut = head.IndexOf(": ", StringComparison.Ordinal);
            var (tag, headline) = cut < 0 ? (head, "") : (head[..cut], head[(cut + 2)..]);
            sections.Add(new RecordSection("note", tag, headline.Trim(), note.Body.Trim()));
        }

        var amends = sections.Where(s => s.Label == "amends")
            .SelectMany(s => DecisionRef().Matches(Head(s.Tag + " " + s.Body, 400)).Select(r => "D" + r.Groups["n"].Value))
            .Where(d => d != key).Distinct().ToList();
        // An in-line "This amends D87" opens a paragraph whose lead is that sentence, so its lead IS the link.
        amends.AddRange(sections.Where(s => s.Label == "amends")
            .SelectMany(s => DecisionRef().Matches(s.Tag).Select(r => "D" + r.Groups["n"].Value))
            .Where(d => d != key && !amends.Contains(d)).Distinct());

        return new CorpusRecord(key, RecordKind.Decision, m.Groups["title"].Value.Trim(),
            ParseDate(m.Groups["date"].Value), [], amends, sections);
    }

    /// <summary>A fix log: one <c>## date - title (ID)</c> entry each, its <c>- **Field:**</c> bullets as sections.</summary>
    public static IReadOnlyList<CorpusRecord> Fixes(string markdown)
    {
        var records = new List<CorpusRecord>();
        foreach (var (heading, body) in SplitHeadings(markdown))
        {
            var (title, date) = FixTitle(heading);
            if (title is null) continue;
            var sections = new List<RecordSection>();
            string? label = null;
            var text = new System.Text.StringBuilder();
            void Flush()
            {
                if (label is not null) sections.Add(new RecordSection(label, label, "", text.ToString().Trim()));
                text.Clear();
            }

            foreach (var line in body.Split('\n'))
            {
                var bullet = FieldBullet().Match(line);
                if (bullet.Success)
                {
                    Flush();
                    label = FirstWord(bullet.Groups["label"].Value);
                    // "Root cause" stays one label so a held-out Symptom is not confused with it.
                    if (bullet.Groups["label"].Value.StartsWith("Root", StringComparison.OrdinalIgnoreCase)) label = "root";
                    text.Append(bullet.Groups["rest"].Value);
                }
                else if (label is null && line.Trim().Length > 0)
                {
                    label = "body";
                    text.Append(line);
                }
                else text.Append('\n').Append(line);
            }
            Flush();

            records.Add(new CorpusRecord($"F{records.Count + 1:D4}", RecordKind.Fix, title, date, [], [], sections.Where(s => s.Body.Length > 0).ToList()));
        }

        return records;
    }

    /// <summary>A task archive: one <c>## ID — title (date, D…)</c> entry each, its paragraphs as body sections.</summary>
    public static IReadOnlyList<CorpusRecord> Archive(string markdown)
    {
        var records = new List<CorpusRecord>();
        foreach (var (heading, body) in SplitHeadings(markdown))
        {
            var m = AnyHeading().Match(heading);
            if (!m.Success || body.Trim().Length == 0) continue;
            var head = m.Groups["head"].Value;
            var date = TrailingDate().Match(head);
            if (!date.Success) continue;

            var cites = DecisionRef().Matches(date.Groups["rest"].Value).Select(r => "D" + r.Groups["n"].Value).Distinct().ToList();
            var title = head[..date.Index].Trim();
            var dash = title.IndexOf(" — ", StringComparison.Ordinal);
            if (dash > 0 && !title[..dash].Contains(' ')) title = title[(dash + 3)..].Trim();

            records.Add(new CorpusRecord($"A{records.Count + 1:D4}", RecordKind.Archive, title,
                ParseDate(date.Groups["date"].Value), cites, [],
                [new RecordSection("body", "", "", body.Trim())]));
        }

        return records;
    }

    /// <summary>A fix heading's title and date. Three shapes are in use: <c>date - title (ID)</c>, <c>ID (open): title
    /// (since date)</c> and <c>title (date)</c>; anything else with a date in it reads as the last.</summary>
    private static (string? Title, DateOnly? Date) FixTitle(string heading)
    {
        var m = FixHeading().Match(heading);
        if (m.Success) return (m.Groups["title"].Value.Trim(), ParseDate(m.Groups["date"].Value));
        var date = IsoDate().Match(heading);
        if (!date.Success) return (null, null);
        var title = Regex.Replace(heading[3..], @"\s*\((since )?\d{4}-\d{2}-\d{2}\)\s*$", "").Trim();
        return (title.Length > 0 ? title : null, ParseDate(date.Value));
    }

    private static void AddLeadSections(string body, List<RecordSection> sections)
    {
        foreach (var raw in Regex.Split(body.Trim(), @"\r?\n\s*\r?\n"))
        {
            var paragraph = raw.Trim();
            if (paragraph.Length == 0) continue;
            var lead = BoldLead().Match(paragraph);
            if (!lead.Success)
            {
                if (sections.Count == 0) sections.Add(new RecordSection("body", "", "", paragraph));
                else sections[^1] = sections[^1] with { Body = sections[^1].Body + "\n\n" + paragraph };
                continue;
            }

            var text = lead.Groups["lead"].Value.Trim();
            var cut = text.StartsWith("Amended", StringComparison.Ordinal) ? text.IndexOf(": ", StringComparison.Ordinal) : -1;
            var (tag, headline) = cut < 0 ? (text.TrimEnd('.', ':'), "") : (text[..cut], text[(cut + 2)..].TrimEnd('.'));
            sections.Add(new RecordSection(LeadLabel(text), tag, headline, lead.Groups["rest"].Value.Trim()));
        }
    }

    private static string LeadLabel(string lead)
    {
        var lower = lead.ToLowerInvariant();
        if (lower.StartsWith("this amends", StringComparison.Ordinal) || lower.StartsWith("amends", StringComparison.Ordinal))
            return "amends";
        return FirstWord(lower);
    }

    private static string FirstWord(string text)
    {
        var word = Regex.Match(text.ToLowerInvariant(), @"[a-z]+");
        return word.Success ? word.Value : "body";
    }

    private static (string Heading, string Body)[] SplitHeadings(string markdown)
    {
        var result = new List<(string, string)>();
        string? heading = null;
        var body = new System.Text.StringBuilder();
        var fenced = false;
        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal)) fenced = !fenced;
            if (!fenced && line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (heading is not null) result.Add((heading, body.ToString()));
                heading = line;
                body.Clear();
            }
            else if (heading is not null) body.Append(line).Append('\n');
        }

        if (heading is not null) result.Add((heading, body.ToString()));
        return [.. result];
    }

    private static DateOnly? ParseDate(string text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", out var d) ? d : null;

    private static string Head(string text, int n) => text.Length <= n ? text : text[..n];

    /// <summary>The first ISO date in <paramref name="text"/>, for an amendment's tag.</summary>
    public static DateOnly? DateIn(string text) => ParseDate(IsoDate().Match(text).Value);
}
