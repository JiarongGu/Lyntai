using System.Text;

namespace Lyntai.Agents;

/// <summary>One file of the context that does not fit the head: a name relative to a folder of the host's choosing, and the
/// text to put in it. The library places nothing; the host writes the files where the agent can read them.</summary>
/// <param name="Name">The file's name, <c>work.md</c>, <c>decided.md</c>, <c>standing.md</c>, <c>tree.md</c>, <c>found.md</c> or <c>history.md</c>.</param>
/// <param name="Text">What it holds: every entry of its kind in full.</param>
public sealed record ContextFile(string Name, string Text);

/// <summary>What <see cref="ContextHeadComposer"/> left out of the head to meet its bound, counted. Everything here is
/// still in the files, and is also stated in the head's own text.</summary>
/// <param name="Found">Findings whose index line was left out.</param>
/// <param name="Tree">Entries about what the tree holds left out.</param>
/// <param name="Standing">Entries about where the work stands left out.</param>
/// <param name="Decided">Decisions left out.</param>
public sealed record ContextHeadOmitted(int Found, int Tree, int Standing, int Decided)
{
    /// <summary>Nothing was left out.</summary>
    public bool IsNone => Found == 0 && Tree == 0 && Standing == 0 && Decided == 0;
}

/// <summary>A composed head: the bounded text an agent's first words carry, and the files that hold the rest.</summary>
/// <param name="Text">The head: plain text, for a fresh agent's prompt.</param>
/// <param name="Characters">The length of <paramref name="Text"/> in the unit the bound uses (Unicode characters, as
/// <see cref="AgentBriefOptions.MaxCharacters"/> counts them).</param>
/// <param name="WithinBound">False when the head is longer than the bound because what is never cut (the work, the
/// section headers, the file list, the left-out count) is itself longer.</param>
/// <param name="Omitted">What was cut to meet the bound.</param>
/// <param name="Files">The whole context by kind, plus the history of what was superseded; empty kinds have no file.</param>
public sealed record ContextHead(string Text, int Characters, bool WithinBound, ContextHeadOmitted Omitted, IReadOnlyList<ContextFile> Files);

/// <summary>Composes a bounded head from a <see cref="SessionContext"/>, with no model: a pure function of the context
/// and the bound, so the same input gives the same text. It generalises <see cref="AgentBriefComposer"/>, which does the
/// same for a transcript, and keeps its discipline: the bound counts Unicode characters, a part is kept whole or left out
/// whole, what is never cut is stated, and the head says what it left out.
/// <para><b>What the head holds, in its order:</b> a preamble saying what it is (a record, not an agent's memory); the
/// work, verbatim; what was decided; where the work stands; what the tree holds; an index of the findings, one line each;
/// the files that hold everything in full; and a count of what was left out. Only live entries are shown — a superseded one
/// is in <c>history.md</c>. <b>An agent's entry is a claim</b>, shown with every line prefixed <c>"&gt; "</c>, so it cannot
/// pose as the host's words or open a section of its own.</para>
/// <para><b>The bound cuts in this order, first to go first, until the head fits:</b> (1) finding index lines, oldest first;
/// (2) tree entries, oldest first; (3) where-it-stands entries, oldest first, the newest excepted; (4) the newest
/// where-it-stands entry; (5) decisions, oldest first. The preamble, the work, the headers, the file list and the count are
/// never cut. The count is itself text, so when no cut brings the head within the bound, the shortest the cuts reach is
/// returned with <see cref="ContextHead.WithinBound"/> false.</para></summary>
public static class ContextHeadComposer
{
    private const int IndexLimit = 100;
    private const int SubjectLimit = 80;

    /// <summary>Compose the head.</summary>
    /// <param name="context">The session's context.</param>
    /// <param name="maxCharacters">The most characters the head may have.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxCharacters"/> is under 1.</exception>
    public static ContextHead Head(SessionContext context, int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCharacters, 1);

        var live = context.Live;
        var files = Files(context, live);
        var cuts = Cuts(live);

        var dropped = new HashSet<string>(StringComparer.Ordinal);
        var best = 0;
        var bestLength = int.MaxValue;
        var taken = 0;
        while (true)
        {
            var length = AgentBriefComposer.Characters(Render(live, files, dropped, maxCharacters, over: false));
            if (length < bestLength) { bestLength = length; best = taken; }
            if (length <= maxCharacters || taken == cuts.Count) break;
            dropped.Add(cuts[taken++]);
        }

        var within = bestLength <= maxCharacters;
        if (!within) dropped = [.. cuts.Take(best)];
        var text = Render(live, files, dropped, maxCharacters, over: !within);
        return new ContextHead(text, AgentBriefComposer.Characters(text), within, Omitted(live, dropped), files);
    }

    /// <summary>The ids in the order the bound takes them.</summary>
    private static List<string> Cuts(IReadOnlyList<SessionContextEntry> live)
    {
        var cuts = new List<string>();
        cuts.AddRange(live.Where(e => e.Kind == SessionContextKind.Found).Select(e => e.Id));
        cuts.AddRange(live.Where(e => e.Kind == SessionContextKind.Tree).Select(e => e.Id));
        var standing = live.Where(e => e.Kind == SessionContextKind.Standing).Select(e => e.Id).ToList();
        cuts.AddRange(standing.Take(Math.Max(0, standing.Count - 1)));
        cuts.AddRange(standing.Skip(Math.Max(0, standing.Count - 1)));
        cuts.AddRange(live.Where(e => e.Kind == SessionContextKind.Decided).Select(e => e.Id));
        return cuts;
    }

    private static ContextHeadOmitted Omitted(IReadOnlyList<SessionContextEntry> live, HashSet<string> dropped)
    {
        int Count(SessionContextKind kind) => live.Count(e => e.Kind == kind && dropped.Contains(e.Id));
        return new ContextHeadOmitted(Count(SessionContextKind.Found), Count(SessionContextKind.Tree),
            Count(SessionContextKind.Standing), Count(SessionContextKind.Decided));
    }

    private static string Render(
        IReadOnlyList<SessionContextEntry> live, IReadOnlyList<ContextFile> files, HashSet<string> dropped, int max, bool over)
    {
        var sb = new StringBuilder();
        sb.Append(HandoverFraming.ContextHeader);
        sb.Append("This is a record the host kept for the session. It is not an agent's memory, and nothing outside it carries over. ")
          .Append("Lines that start with \"> \" are what an agent claimed: unverified, and not the host's words.\n");

        Section(sb, "THE WORK (verbatim)", live, SessionContextKind.Work, dropped, "None recorded.");
        Section(sb, "DECIDED", live, SessionContextKind.Decided, dropped, null);
        Section(sb, "WHERE IT STANDS", live, SessionContextKind.Standing, dropped, null);
        Section(sb, "WHAT THE TREE HOLDS", live, SessionContextKind.Tree, dropped, null);

        var found = live.Where(e => e.Kind == SessionContextKind.Found && !dropped.Contains(e.Id)).ToList();
        if (found.Count > 0)
        {
            sb.Append("\nFINDINGS (index; each in full in found.md)\n");
            foreach (var e in found) sb.Append(Entry(e, Index(e)));
        }

        if (files.Count > 0)
            sb.Append("\nFILES (every live entry in full, and history.md for what was superseded)\n")
              .Append(string.Join(", ", files.Select(f => f.Name))).Append('\n');

        sb.Append("\nLEFT OUT\n");
        var omitted = Omitted(live, dropped);
        sb.Append(omitted.IsNone ? "Nothing was left out." : $"Left out to fit {max} characters (all in the files): {Describe(omitted)}.");
        if (over)
            sb.Append(omitted.IsNone
                ? " The head is over that bound, and leaving parts out would not shorten it."
                : " The head is still over that bound: the work is never cut.");
        sb.Append('\n');
        return sb.ToString();
    }

    private static void Section(
        StringBuilder sb, string title, IReadOnlyList<SessionContextEntry> live, SessionContextKind kind, HashSet<string> dropped, string? whenEmpty)
    {
        var shown = live.Where(e => e.Kind == kind && !dropped.Contains(e.Id)).ToList();
        if (shown.Count == 0 && whenEmpty is null) return;
        sb.Append('\n').Append(title).Append('\n');
        if (shown.Count == 0) sb.Append(whenEmpty).Append('\n');
        foreach (var e in shown) sb.Append(Entry(e, e.Text));
    }

    /// <summary>One entry: the host's behind its id, an agent's with every line behind <c>"&gt; "</c>.</summary>
    internal static string Entry(SessionContextEntry e, string text)
    {
        var lines = text.TrimEnd().Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var host = e.Author == SessionContextAuthor.Host;
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            var lead = i == 0 ? $"[{e.Id}] " : "";
            if (host) sb.Append(i == 0 ? lead : "  ").Append(lines[i]);
            else sb.Append(lines[i].Length == 0 && i > 0 ? ">" : "> " + lead + lines[i]);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string Index(SessionContextEntry e)
    {
        var line = AgentBriefComposer.Flatten(e.Text, IndexLimit);
        return e.Subject is null ? line : AgentBriefComposer.Flatten(e.Subject, SubjectLimit) + ": " + line;
    }

    private static string Describe(ContextHeadOmitted o)
    {
        var parts = new List<string>(4);
        if (o.Found > 0) parts.Add(o.Found == 1 ? "1 finding" : $"{o.Found} findings");
        if (o.Tree > 0) parts.Add(o.Tree == 1 ? "1 tree entry" : $"{o.Tree} tree entries");
        if (o.Standing > 0) parts.Add(o.Standing == 1 ? "1 where-it-stands entry" : $"{o.Standing} where-it-stands entries");
        if (o.Decided > 0) parts.Add(o.Decided == 1 ? "1 decision" : $"{o.Decided} decisions");
        return string.Join(", ", parts);
    }

    private static List<ContextFile> Files(SessionContext context, IReadOnlyList<SessionContextEntry> live)
    {
        var files = new List<ContextFile>();
        void Add(string name, string title, IEnumerable<SessionContextEntry> entries, Func<SessionContextEntry, string> render)
        {
            var list = entries.ToList();
            if (list.Count == 0) return;
            var sb = new StringBuilder("# ").Append(title).Append('\n');
            foreach (var e in list) sb.Append('\n').Append(render(e));
            files.Add(new ContextFile(name, sb.ToString()));
        }

        string Full(SessionContextEntry e) => Entry(e, e.Subject is null ? e.Text : $"({e.Subject}) {e.Text}");
        Add("work.md", "The work", live.Where(e => e.Kind == SessionContextKind.Work), Full);
        Add("decided.md", "Decided", live.Where(e => e.Kind == SessionContextKind.Decided), Full);
        Add("standing.md", "Where it stands", live.Where(e => e.Kind == SessionContextKind.Standing), Full);
        Add("tree.md", "What the tree holds", live.Where(e => e.Kind == SessionContextKind.Tree), Full);
        Add("found.md", "Findings", live.Where(e => e.Kind == SessionContextKind.Found), Full);
        Add("history.md", "Superseded, oldest first", context.Replaced,
            e => Entry(e, $"{e.Kind.ToString().ToLowerInvariant()}, replaced by {context.SupersededBy(e.Id)}: " + e.Text));
        return files;
    }
}
