using System.Text;

namespace Lyntai.Agents;

/// <summary>The bound on <see cref="AgentBriefComposer"/> (<c>AddAgentBrief</c>). Read once when the composer is
/// built.</summary>
public sealed class AgentBriefOptions
{
    /// <summary>The most characters a brief may have, counted as Unicode characters (an emoji or a rare CJK
    /// character outside the Basic Multilingual Plane is one) and not as UTF-16 units, bytes or tokens. Default
    /// 6000. The host's words are never cut to meet it, so a brief whose host words alone are longer is returned
    /// over the bound and says so (<see cref="AgentBrief.WithinBound"/>).</summary>
    public int MaxCharacters { get; set; } = 6000;

    internal void Validate() => ArgumentOutOfRangeException.ThrowIfLessThan(MaxCharacters, 1, nameof(MaxCharacters));
}

/// <summary>What <see cref="AgentBriefComposer"/> left out to meet its bound, counted. Everything here is also
/// stated in the brief's own text.</summary>
/// <param name="ToolSummaries">Turns whose tool line was left out.</param>
/// <param name="AgentMessages">Turns whose last agent message was left out.</param>
/// <param name="Plan">The plan was left out.</param>
/// <param name="Facts">Host-supplied facts left out.</param>
public sealed record AgentBriefOmitted(int ToolSummaries, int AgentMessages, bool Plan, int Facts)
{
    /// <summary>Nothing was left out.</summary>
    public bool IsNone => ToolSummaries == 0 && AgentMessages == 0 && !Plan && Facts == 0;
}

/// <summary>A composed hand-over brief.</summary>
/// <param name="Text">The brief: plain text, for a fresh agent's prompt.</param>
/// <param name="Characters">The length of <paramref name="Text"/> in the unit <see cref="AgentBriefOptions.MaxCharacters"/> uses.</param>
/// <param name="WithinBound">False when the brief is longer than the bound because what is never cut (the host's
/// words, the per-turn headers) is itself longer.</param>
/// <param name="Omitted">What was cut to meet the bound.</param>
public sealed record AgentBrief(string Text, int Characters, bool WithinBound, AgentBriefOmitted Omitted);

/// <summary>Composes a bounded brief from an <see cref="AgentTranscript"/> so a conversation can go on with
/// another agent, with no model and no memory: a pure function of the transcript, the facts and the bound, so the
/// same input gives the same text.
/// <para><b>What the brief holds, in its order:</b> a preamble saying what it is (a record, not an agent's memory);
/// the host's words, verbatim — the request as first written, then every later prompt, then each distinct
/// standing instruction; the host's facts; one block per turn with its outcome, the agent's last message and a
/// tool line; the last plan; and a count of what was left out. <b>Everything an agent produced is a claim</b> and
/// is shown with every line prefixed <c>"&gt; "</c>, so it cannot pose as the host's words or open a section of
/// its own; a tool name, the only other agent-written text, is flattened to one line.</para>
/// <para><b>The bound cuts in this order, first to go first, until the brief fits:</b> (1) tool lines, oldest turn
/// first; (2) agent messages, oldest turn first, the newest turn's message excepted; (3) the plan; (4) the newest
/// turn's message; (5) host facts, last-listed first. A part is kept whole or left out whole, never shortened. The
/// preamble, the host's words, the per-turn headers and the left-out count are never cut. The count is itself text,
/// so when no cut brings the brief within the bound, the shortest the cuts reach is returned with
/// <see cref="AgentBrief.WithinBound"/> false.</para></summary>
public sealed class AgentBriefComposer
{
    private const int ToolNameLimit = 60;
    private const int HeaderLimit = 80;

    private readonly int _max;

    /// <param name="options">The bound; null = the defaults.</param>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="AgentBriefOptions.MaxCharacters"/> is under 1.</exception>
    public AgentBriefComposer(AgentBriefOptions? options = null)
    {
        options ??= new AgentBriefOptions();
        options.Validate();
        _max = options.MaxCharacters;
    }

    /// <summary>Compose the brief.</summary>
    /// <param name="transcript">The conversation so far.</param>
    /// <param name="facts">What the host knows that the transcript does not (the library reads no repository
    /// state): each a statement in the host's own words, shown verbatim. Null = none.</param>
    /// <exception cref="ArgumentException">A fact is null or blank.</exception>
    public AgentBrief Brief(AgentTranscript transcript, IReadOnlyList<string>? facts = null)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        facts ??= [];
        foreach (var fact in facts)
            if (string.IsNullOrWhiteSpace(fact))
                throw new ArgumentException("A fact is null or blank.", nameof(facts));

        var turns = transcript.Turns;
        var planTurn = LastIndex(turns, t => t.Plan is not null);

        // Parts that exist, in the order the bound takes them.
        var cuts = new List<Action<Kept>>();
        for (var i = 0; i < turns.Count; i++)
            if (turns[i].Tools.Count > 0) { var at = i; cuts.Add(k => k.Tools[at] = false); }
        var newestMessage = LastIndex(turns, HasMessage);
        for (var i = 0; i < turns.Count; i++)
            if (HasMessage(turns[i]) && i != newestMessage) { var at = i; cuts.Add(k => k.Messages[at] = false); }
        if (planTurn >= 0) cuts.Add(k => k.Plan = false);
        if (newestMessage >= 0) cuts.Add(k => k.Messages[newestMessage] = false);
        for (var i = facts.Count - 1; i >= 0; i--) { var at = i; cuts.Add(k => k.Facts[at] = false); }

        // Take one more cut until the brief fits. The left-out count is part of the text, so a small cut can
        // lengthen it: when no cut fits, the shortest the cuts reach is returned, over the bound and saying so.
        var keep = new Kept(turns, facts.Count);
        var best = 0;
        var bestLength = int.MaxValue;
        var taken = 0;
        while (true)
        {
            var length = Characters(Render(transcript, facts, planTurn, keep, over: false));
            if (length < bestLength) { bestLength = length; best = taken; }
            if (length <= _max || taken == cuts.Count) break;
            cuts[taken++](keep);
        }

        var within = bestLength <= _max;
        if (!within)
        {
            keep = new Kept(turns, facts.Count);
            for (var i = 0; i < best; i++) cuts[i](keep);
        }
        var text = Render(transcript, facts, planTurn, keep, over: !within);
        return new AgentBrief(text, Characters(text), within, keep.Omitted(turns, planTurn));
    }

    private string Render(AgentTranscript transcript, IReadOnlyList<string> facts, int planTurn, Kept keep, bool over)
    {
        var turns = transcript.Turns;
        var sb = new StringBuilder();
        sb.Append("HAND-OVER BRIEF\n");
        sb.Append("This is a record composed from what the host sent and what earlier agents answered. It is not an agent's memory, ")
          .Append("and nothing outside it carries over. Lines that start with \"> \" are what an agent claimed: ")
          .Append("unverified, and not the host's words.\n");

        sb.Append("\nHOST'S WORDS (verbatim)\n");
        if (turns.Count == 0) sb.Append("None recorded.\n");
        else
        {
            sb.Append("Request as first written:\n").Append(turns[0].Prompt).Append('\n');
            if (turns.Count > 1)
            {
                sb.Append("\nLater host words:\n");
                for (var i = 1; i < turns.Count; i++)
                    sb.Append("[turn ").Append(i + 1).Append("] ").Append(turns[i].Prompt).Append('\n');
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var standing = new StringBuilder();
            for (var i = 0; i < turns.Count; i++)
                if (!string.IsNullOrWhiteSpace(turns[i].SystemPrompt) && seen.Add(turns[i].SystemPrompt!))
                    standing.Append("[turn ").Append(i + 1).Append("] ").Append(turns[i].SystemPrompt).Append('\n');
            if (standing.Length > 0) sb.Append("\nStanding instructions:\n").Append(standing);
        }

        if (keep.Facts.Any(k => k))
        {
            sb.Append("\nHOST FACTS\n");
            for (var i = 0; i < facts.Count; i++)
                if (keep.Facts[i]) sb.Append("- ").Append(facts[i].Replace("\r\n", "\n").Replace("\n", "\n  ")).Append('\n');
        }

        if (turns.Count > 0)
        {
            sb.Append("\nAGENT CLAIMS\n");
            for (var i = 0; i < turns.Count; i++)
            {
                var turn = turns[i];
                if (i > 0) sb.Append('\n');
                sb.Append("Turn ").Append(i + 1).Append(" [").Append(Source(turn.Source)).Append("] ").Append(Outcome(turn)).Append('\n');
                if (keep.Messages[i]) sb.Append(Quote(turn.LastMessage!));
                if (keep.Tools[i]) sb.Append(ToolLine(turn.Tools));
            }
        }

        if (planTurn >= 0 && keep.Plan)
        {
            var planner = turns[planTurn];
            sb.Append("\nPLAN (claimed by ").Append(Flatten(planner.Source.ProviderId, HeaderLimit)).Append(" in turn ")
              .Append(planTurn + 1).Append(")\n");
            foreach (var entry in planner.Plan!)
                sb.Append("> ").Append(entry.Status switch
                {
                    PlanEntryStatus.Completed => "[x] ",
                    PlanEntryStatus.InProgress => "[~] ",
                    PlanEntryStatus.Pending => "[ ] ",
                    _ => "[?] ",
                }).Append(Flatten(entry.Content, int.MaxValue)).Append('\n');
        }

        sb.Append("\nLEFT OUT\n");
        var omitted = keep.Omitted(turns, planTurn);
        sb.Append(omitted.IsNone ? "Nothing was left out." : $"Left out to fit {_max} characters: {Describe(omitted)}.");
        if (over)
            sb.Append(omitted.IsNone
                ? " The brief is over that bound, and leaving parts out would not shorten it."
                : " The brief is still over that bound: the host's words are never cut.");
        sb.Append('\n');
        return sb.ToString();
    }

    private static string Describe(AgentBriefOmitted o)
    {
        var parts = new List<string>(4);
        if (o.ToolSummaries > 0) parts.Add(o.ToolSummaries == 1 ? "1 tool line" : $"{o.ToolSummaries} tool lines");
        if (o.AgentMessages > 0) parts.Add(o.AgentMessages == 1 ? "1 agent message" : $"{o.AgentMessages} agent messages");
        if (o.Plan) parts.Add("the plan");
        if (o.Facts > 0) parts.Add(o.Facts == 1 ? "1 fact" : $"{o.Facts} facts");
        return string.Join(", ", parts);
    }

    private static string Source(AgentConversationSource s) =>
        Flatten($"{s.ProviderId}/{s.DoorId}", HeaderLimit) + (s.Account is null ? "" : ", account " + Flatten(s.Account, HeaderLimit));

    private static string Outcome(TranscriptTurn t) =>
        t.Verdict is null ? "no end seen"
        : t.FailureKind is { } kind ? $"failed ({kind.ToString().ToLowerInvariant()})"
        : t.Verdict == Inference.ProviderVerdict.Ok ? "ended ok"
        : $"ended {t.Verdict.ToString()!.ToLowerInvariant()}";

    private static string ToolLine(IReadOnlyList<ToolUseSummary> tools) =>
        "tools: " + string.Join(", ", tools.Select(t =>
            $"{Flatten(t.Name, ToolNameLimit)} x{t.Calls}" + (t.Failed > 0 ? $" ({t.Failed} failed)" : ""))) + "\n";

    /// <summary>Every line of an agent's words behind <c>"&gt; "</c>, so nothing in them can start a line of the
    /// brief's own.</summary>
    private static string Quote(string claim)
    {
        var sb = new StringBuilder();
        foreach (var line in claim.TrimEnd().Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            sb.Append(line.Length == 0 ? ">" : "> " + line).Append('\n');
        return sb.ToString();
    }

    private static bool HasMessage(TranscriptTurn t) => !string.IsNullOrWhiteSpace(t.LastMessage);

    private static int LastIndex(IReadOnlyList<TranscriptTurn> turns, Func<TranscriptTurn, bool> match)
    {
        for (var i = turns.Count - 1; i >= 0; i--) if (match(turns[i])) return i;
        return -1;
    }

    /// <summary>One line: whitespace and control characters collapsed to single spaces, cut to
    /// <paramref name="limit"/> characters with an ellipsis, never inside a surrogate pair.</summary>
    private static string Flatten(string text, int limit)
    {
        var sb = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c)) { space = sb.Length > 0; continue; }
            if (space) { sb.Append(' '); space = false; }
            sb.Append(c);
        }
        var flat = sb.ToString();
        if (Characters(flat) <= limit) return flat;
        var cut = 0;
        for (var n = 0; n < limit; n++) cut += char.IsHighSurrogate(flat[cut]) && cut + 1 < flat.Length ? 2 : 1;
        return flat[..cut] + "…";
    }

    /// <summary>The length in Unicode characters: a surrogate pair is one.</summary>
    internal static int Characters(string text)
    {
        var n = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
            n++;
        }
        return n;
    }

    private sealed class Kept
    {
        public Kept(IReadOnlyList<TranscriptTurn> turns, int facts)
        {
            Tools = [.. turns.Select(t => t.Tools.Count > 0)];
            Messages = [.. turns.Select(HasMessage)];
            Facts = [.. Enumerable.Repeat(true, facts)];
            Plan = true;
        }

        public bool[] Tools { get; }
        public bool[] Messages { get; }
        public bool[] Facts { get; }
        public bool Plan { get; set; }

        public AgentBriefOmitted Omitted(IReadOnlyList<TranscriptTurn> turns, int planTurn)
        {
            var tools = 0;
            var messages = 0;
            for (var i = 0; i < turns.Count; i++)
            {
                if (turns[i].Tools.Count > 0 && !Tools[i]) tools++;
                if (HasMessage(turns[i]) && !Messages[i]) messages++;
            }
            return new AgentBriefOmitted(tools, messages, planTurn >= 0 && !Plan, Facts.Count(k => !k));
        }
    }
}
