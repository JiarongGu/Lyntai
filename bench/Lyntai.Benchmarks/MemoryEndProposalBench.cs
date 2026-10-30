using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Benchmarks;

/// <summary>
/// <b>The wrong-end instrument: what a proposed end costs, per proposer and corpus</b> (<c>TASKS.md</c> MC8, <c>docs/DECISIONS.md</c>
/// D241). It builds the harness and promotes NOTHING: a verdict is printed only for an independently adjudicated corpus and a bar
/// written before the run, and what a YES does is a host's act on the record, never this program's.
///
/// <para><b>Every arm starts from an equivalent ISOLATED snapshot.</b> One database is ingested per question and cloned per arm
/// (<c>SweepDb.Clone</c> checkpoints first), so the first arm's reinforcement and links cannot reach the second. Execution order is
/// COUNTERBALANCED across questions, which removes order bias and nothing else; the isolation is what removes contamination
/// (<c>docs/memory-currency-synthesis.md</c> §5). Each arm's recall is repeated within its own clone, and a recall mutates only that clone.</para>
///
/// <para><b>Four rates, reported separately</b>, never as one accuracy: a wrong end hiding a fact that still held, a missed replacement,
/// an unaffected claim lost, and a stale fact kept; beside them lost current claims and lost historical evidence.</para>
///
/// <para><b>Two reviewers must agree.</b> A pair they label differently abstains: it is counted and left out of every rate.</para>
/// </summary>
internal static partial class MemoryLongMemEvalBench
{
    private const int EndProposalCandidates = 10;

    private enum PairLabel { Replaces, Amends, Unrelated }

    /// <summary>The bar, written BEFORE the run: a ceiling per rate and the fewest agreed pairs a verdict needs.</summary>
    private sealed record EndBar(double WrongEndMax, double MissedMax, double UnaffectedLostMax, double StaleKeptMax, int MinPairs);

    private sealed class ArmTally
    {
        public int Pairs, Replaces, Amends, WrongEnd, Missed, UnaffectedLost, Walks, StaleKept, CurrentLost, HistoryLost;
        public int Proposed;
    }

    internal static async Task<int> RunEndProposalAsync(string[] args)
    {
        var path = Environment.GetEnvironmentVariable(HaystackVariable)
            ?? Path.Combine("devtools", "_bench-data", "lme_s.json");
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"memory-end-proposal: haystack dataset not found at '{path}' (see memory-longmemeval --haystack).");
            return 1;
        }

        var seed = ArgValue(args, "--seed") is { } s && int.TryParse(s, out var chosen) ? chosen : DefaultSeed;
        var take = ArgValue(args, "--n") is { } n && int.TryParse(n, out var parsed) ? parsed : 20;
        var repeats = ArgValue(args, "--repeats") is { } r && int.TryParse(r, out var rr) && rr > 0 ? rr : 3;
        var proposers = (ArgValue(args, "--proposers") ?? "none,first,oracle").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var adjudication = ArgValue(args, "--adjudication") is { } aj ? ReadAdjudication(aj) : null;
        var bar = ArgValue(args, "--bar") is { } bp ? ReadBar(bp, out _) : null;
        string? barDigest = bar is null ? null : Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(ArgValue(args, "--bar")!)))[..16];

        SweepDoubles.IBenchChat? chat = null;
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        if (proposers.Contains("model"))
        {
            chat = await SweepDoubles.TryRealChatAsync(http, "memory-end-proposal");
            if (chat is null) return 1;
        }

        var questions = Sample(Load(path, "knowledge-update", false), take, seed);
        Console.WriteLine($"memory-end-proposal: {questions.Count} knowledge-update pairs (seed {seed}), proposers {string.Join(", ", proposers)}, " +
            $"{repeats} repeated walks per arm, candidates {EndProposalCandidates}");
        Console.WriteLine(adjudication is null
            ? "  SCREENING ONLY: no --adjudication, so every pair is assumed to REPLACE and no verdict can be printed."
            : $"  adjudication: {adjudication.Count} pair(s) labelled by two reviewers; a disagreement abstains.");
        Console.WriteLine(bar is null
            ? "  no --bar: no verdict can be printed. Write the bar before the run and pass it."
            : $"  bar {barDigest}: {bar}");

        var tallies = proposers.ToDictionary(p => p, _ => new ArmTally(), StringComparer.Ordinal);
        var abstained = 0;
        var unlabelled = 0;
        var index = 0;
        foreach (var q in questions)
        {
            var label = PairLabel.Replaces;
            if (adjudication is not null)
            {
                if (!adjudication.TryGetValue(q.Id, out var verdict)) { unlabelled++; index++; continue; }
                if (verdict is null) { abstained++; index++; continue; }
                label = verdict.Value;
            }

            using var baseDb = new MemoryPolicySweep.SweepDb();
            var seams = new GraphMemorySeams { AgePolicies = [new PerWriteAgePolicy()] };
            var baseEngine = new GraphMemoryEngine(Task, new SqliteMemoryGraphStore(baseDb.Factory), null, seams);
            var currentTexts = q.Current.Select(t => t.Text).ToHashSet(StringComparer.Ordinal);
            var staleTexts = q.Stale.Select(t => t.Text).ToHashSet(StringComparer.Ordinal);
            var staleRefs = new HashSet<MemoryRef>();
            foreach (var t in q.Turns)
                if (!currentTexts.Contains(t.Text) && t.Text.Length > 0)
                {
                    var stored = await baseEngine.RememberAsync(new MemoryWrite(Task, Scope, t.Text));
                    if (staleTexts.Contains(t.Text)) staleRefs.Add(stored.Reference);
                }

            // counterbalanced: the arms run in a rotation that advances with the question, so no arm is always first
            var order = proposers.Skip(index % proposers.Length).Concat(proposers.Take(index % proposers.Length)).ToList();
            foreach (var name in order)
            {
                using var arm = baseDb.Clone();
                var engine = new GraphMemoryEngine(Task, new SqliteMemoryGraphStore(arm.Factory), null, seams);
                var claim = q.Current[0].Text;
                var candidates = await engine.FindUpdatesAsync(new MemoryWrite(Task, Scope, claim), EndProposalCandidates);
                var pick = await ProposeAsync(name, chat, claim, candidates, staleTexts);

                var successor = (await engine.RememberAsync(new MemoryWrite(Task, Scope, claim))).Reference;
                var currentRefs = new HashSet<MemoryRef> { successor };
                foreach (var extra in q.Current.Skip(1))
                    currentRefs.Add((await engine.RememberAsync(new MemoryWrite(Task, Scope, extra.Text))).Reference);
                var pickedStale = pick is { } p && candidates.Any(c => c.Item.Reference == p && staleTexts.Contains(c.Item.Content ?? ""));
                if (pick is { } ending)
                    await engine.EndAsync(Task, ending, new MemoryEnding("proposed by the arm, applied in an isolated clone")
                        { Successor = successor });

                var tally = tallies[name];
                tally.Pairs++;
                if (label == PairLabel.Replaces) tally.Replaces++;
                if (label == PairLabel.Amends) tally.Amends++;
                if (pick is not null) tally.Proposed++;
                if (label == PairLabel.Amends && pickedStale) tally.WrongEnd++;
                if (label == PairLabel.Replaces && !pickedStale) tally.Missed++;
                if (pick is not null && !pickedStale) tally.UnaffectedLost++;

                // the walk is repeated inside THIS clone: a recall reinforces and links, and nothing here reaches another arm
                for (var walk = 0; walk < repeats; walk++)
                {
                    tally.Walks++;
                    var current = await engine.RecallAsync(new MemoryQuery(Task, Scope, q.Text, RecallLimit));
                    var hasStale = current.Items.Any(i => staleRefs.Contains(i.Reference));
                    var hasCurrent = current.Items.Any(i => currentRefs.Contains(i.Reference));
                    // historical evidence is PRESERVED when the entry is still readable by reference whatever its validity: a
                    // History page is ranked and says nothing about whether the ledger kept it
                    var kept = false;
                    foreach (var reference in staleRefs)
                        kept |= (await engine.ExpandAsync(reference, hops: 0)).Items.Count > 0;
                    if (label == PairLabel.Replaces && hasStale) tally.StaleKept++;
                    if (!hasCurrent) tally.CurrentLost++;
                    if (!kept) tally.HistoryLost++;
                }
            }
            index++;
        }

        Console.WriteLine();
        Console.WriteLine($"  abstained (reviewers disagreed): {abstained}; not adjudicated and skipped: {unlabelled}");
        Console.WriteLine($"  {"proposer",-8} {"pairs",5} {"proposed",8} {"wrong end",10} {"missed",8} {"unaffected lost",16} {"stale kept",11} {"current lost",13} {"history lost",13}");
        foreach (var (name, t) in tallies)
            Console.WriteLine($"  {name,-8} {t.Pairs,5} {t.Proposed,8} {Rate(t.WrongEnd, t.Amends),10} {Rate(t.Missed, t.Replaces),8} " +
                $"{Rate(t.UnaffectedLost, t.Pairs),16} {Rate(t.StaleKept, t.Walks),11} {Rate(t.CurrentLost, t.Walks),13} {Rate(t.HistoryLost, t.Walks),13}");

        Console.WriteLine();
        if (adjudication is null || bar is null)
            Console.WriteLine("  VERDICT WITHHELD: a verdict needs an adjudicated corpus and a bar written before the run.");
        else
            foreach (var (name, t) in tallies.Where(x => x.Key == "model"))
            {
                var ok = t.Pairs >= bar.MinPairs
                    && Frac(t.WrongEnd, t.Amends) <= bar.WrongEndMax && Frac(t.Missed, t.Replaces) <= bar.MissedMax
                    && Frac(t.UnaffectedLost, t.Pairs) <= bar.UnaffectedLostMax && Frac(t.StaleKept, t.Walks) <= bar.StaleKeptMax;
                Console.WriteLine($"  {name}: {(t.Pairs < bar.MinPairs ? $"too few agreed pairs ({t.Pairs} < {bar.MinPairs})" : ok ? "MEETS the preregistered bar" : "MISSES the preregistered bar")} on this corpus and this model only.");
            }
        Console.WriteLine("  This run promotes nothing. A bar met moves a class for that (model, corpus) pair only when the host records it in");
        Console.WriteLine("  docs/memory.md and docs/memory-measurements.md §5; a bar missed changes nothing and is recorded as a refutation.");
        Console.WriteLine("  Stop rule: the first actionable rate stops this instrument; no run is spent after it.");
        return 0;
    }

    private static async Task<MemoryRef?> ProposeAsync(string proposer, SweepDoubles.IBenchChat? chat, string claim,
        IReadOnlyList<MemoryUpdateCandidate> candidates, HashSet<string> staleTexts)
    {
        switch (proposer)
        {
            case "none": return null;
            case "first": return candidates.Count == 0 ? null : candidates[0].Item.Reference;
            case "oracle":
                return candidates.FirstOrDefault(c => staleTexts.Contains(c.Item.Content ?? ""))?.Item.Reference;
            case "model" when chat is not null && candidates.Count > 0:
                var listing = new StringBuilder();
                for (var i = 0; i < candidates.Count; i++)
                    listing.Append(i + 1).Append(". ").AppendLine(Clip(candidates[i].Item.Content ?? candidates[i].Item.Headline));
                var answer = await chat.AskAsync(
                    $"A new statement was just recorded:\n\"{Clip(claim)}\"\n\nExisting records:\n{listing}\n" +
                    "Does the new statement REPLACE one record, so that record is no longer true? Answer with that record's number, " +
                    "or 0 if none is replaced. Answer with the number only.", maxTokens: 4);
                return answer is not null && int.TryParse(new string([.. answer.Where(char.IsDigit)]), out var number)
                    && number >= 1 && number <= candidates.Count ? candidates[number - 1].Item.Reference : null;
            default: return null;
        }
    }

    private static string Clip(string text) => text.Length <= 600 ? text : text[..600] + "…";

    private static double Frac(int hit, int of) => of == 0 ? 0 : (double)hit / of;

    private static string Rate(int hit, int of) => of == 0 ? "n/a" : $"{hit}/{of} ({Frac(hit, of):P0})";

    /// <summary>Per question id: the label two reviewers agreed on, or null where they disagreed (the pair abstains).</summary>
    private static Dictionary<string, PairLabel?> ReadAdjudication(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var result = new Dictionary<string, PairLabel?>(StringComparer.Ordinal);
        foreach (var pair in doc.RootElement.GetProperty("pairs").EnumerateArray())
        {
            var labels = pair.GetProperty("reviewers").EnumerateArray()
                .Select(r => Enum.Parse<PairLabel>(r.GetProperty("label").GetString()!, ignoreCase: true)).ToList();
            if (labels.Count < 2) throw new InvalidDataException($"pair '{pair.GetProperty("id").GetString()}' has fewer than two reviewers");
            result[pair.GetProperty("id").GetString()!] = labels.Distinct().Count() == 1 ? labels[0] : null;
        }
        return result;
    }

    private static EndBar ReadBar(string path, out string hash)
    {
        var bytes = File.ReadAllBytes(path);
        hash = Convert.ToHexStringLower(SHA256.HashData(bytes))[..16];
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;
        return new EndBar(root.GetProperty("wrongEndMax").GetDouble(), root.GetProperty("missedMax").GetDouble(),
            root.GetProperty("unaffectedLostMax").GetDouble(), root.GetProperty("staleKeptMax").GetDouble(),
            root.GetProperty("minPairs").GetInt32());
    }
}
