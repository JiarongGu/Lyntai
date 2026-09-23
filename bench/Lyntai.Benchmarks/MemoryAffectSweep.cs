using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Lyntai.Benchmarks;

/// <summary>
/// <c>memory-affect</c> — does a turn's emotional AROUSAL predict whether it is asked about later? The one
/// question about affect-as-a-memory-axis that no record here answered (<c>docs/task-archive.md</c> Part 273).
///
/// <para><b>Why a predictor study and not an oracle arm.</b> <c>memory-importance</c> already priced a
/// PERFECT importance signal through salience, retention and store admission: a redistribution, not an
/// improvement. An arousal oracle on authored fixtures is that oracle keyed on arousal, and the fixture's
/// author would decide whether arousal marks what gets asked. What is empirical is whether it does on real
/// conversations — LoCoMo's evidence turns are that ground truth.</para>
///
/// <para><b>Two detectors, so the answer rests on neither</b>: a chat model rating 1–9, and the NRC-VAD
/// lexicon (model-free, non-commercial research use, not redistributed — the run refuses without a local
/// copy). <b>Length is the confound</b>: a longer turn carries more facts and more affect words, so every
/// signal is also scored within length quartiles.</para>
///
/// <para>Ratings are cached per model under <c>devtools/_bench-data/</c>, keyed by a hash of the text, so a
/// re-run re-reads rather than re-asks.</para>
/// </summary>
internal static class MemoryAffectSweep
{
    // PRE-REGISTERED before the first run: the model's arousal must reach this AUC within length quartiles.
    // A judgement call, never a value read off a run.
    private const double SignalFloor = 0.60;

    // The rater's positive control must separate its fixed calm and intense sentences at least this well,
    // or the table measures a rater that cannot rate.
    private const double ControlFloor = 0.90;

    private const int Lanes = 4;

    private static readonly string[] Calm =
    [
        "I had toast for breakfast.", "The meeting is at three.", "I put the folder back on the shelf.",
        "It rained a little today.", "I watered the plants this morning.",
    ];

    private static readonly string[] Intense =
    [
        "I just got the job, I'm screaming!!", "My house caught fire last night and we barely got out.",
        "I can't believe she lied to me, I'm furious!", "We won the championship!!! Best day ever!",
        "I was in a car crash today and I'm still shaking.",
    ];

    private sealed record Rating(double? Arousal, double? Valence);

    private sealed record Row(bool Evidence, int Length, double? Arousal, double? ValenceExtremity,
        double? LexiconArousal);

    public static async Task<int> RunAsync()
    {
        var dataPath = Environment.GetEnvironmentVariable(MemoryLocomoBench.DataVariable)
            ?? Path.Combine("devtools", "_bench-data", "locomo10.json");
        var lexiconPath = Environment.GetEnvironmentVariable("LYNTAI_NRC_VAD_PATH")
            ?? Path.Combine("devtools", "_bench-data", "nrc-vad", "unigrams-NRC-VAD-Lexicon-v2.1.txt");
        if (!File.Exists(dataPath) || !File.Exists(lexiconPath))
        {
            Console.Error.WriteLine("memory-affect: needs LoCoMo and the NRC-VAD unigram lexicon locally.");
            Console.Error.WriteLine($"  LoCoMo  '{dataPath}' — see `memory-locomo` for the download.");
            Console.Error.WriteLine($"  NRC-VAD '{lexiconPath}' — https://saifmohammad.com/WebPages/nrc-vad.html");
            Console.Error.WriteLine("  (non-commercial research use, no redistribution: never commit it).");
            return 1;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var chat = await SweepDoubles.TryRealChatAsync(http, "memory-affect");
        if (chat is null) return 1;

        var stopwatch = Stopwatch.StartNew();
        var lexicon = LoadLexicon(lexiconPath);
        var (conversations, questions) = MemoryLocomoBench.Load(dataPath);
        var cachePath = Path.Combine("devtools", "_bench-data",
            $"affect-ratings.{Regex.Replace(chat.Model, "[^A-Za-z0-9.-]", "_")}.tsv");
        var cache = LoadCache(cachePath);

        var turns = conversations.SelectMany(c => c.Turns.Select(t => (Conv: c.Id, Turn: t))).ToList();
        var keys = turns.Select(t => (t.Conv, t.Turn.DiaId)).ToHashSet();
        var cited = questions
            .SelectMany(q => q.Evidence.SelectMany(SplitEvidence).Select(e => (q.ConvId, e)))
            .ToHashSet();
        var resolved = cited.Count(keys.Contains);

        PrintPreamble(chat.Model, turns.Count, questions.Count);

        async Task<Rating> RateAsync(string text)
        {
            var key = Hash(text);
            if (cache.TryGetValue(key, out var hit)) return hit;
            string? reply;
            // One stalled call must not discard every rating already made (`pitfalls.md`, fan-out bench): it
            // counts as unparsed under C2, and stays out of the cache so a re-run asks again.
            try { reply = await chat.AskAsync(Prompt(text), maxTokens: 24); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return new(null, null); }
            var rating = Parse(reply);
            cache[key] = rating;
            return rating;
        }

        // The positive control FIRST, so a rater that cannot rate is seen before the corpus is spent on it.
        var calm = new List<double>();
        var intense = new List<double>();
        foreach (var s in Calm) calm.Add((await RateAsync(s)).Arousal ?? double.NaN);
        foreach (var s in Intense) intense.Add((await RateAsync(s)).Arousal ?? double.NaN);
        var controlModel = Auc(intense, calm);
        var controlLexicon = Auc([.. Intense.Select(s => LexiconArousal(s, lexicon) ?? double.NaN)],
            [.. Calm.Select(s => LexiconArousal(s, lexicon) ?? double.NaN)]);

        var ratings = new Rating[turns.Count];
        var done = 0;
        await Parallel.ForEachAsync(Enumerable.Range(0, turns.Count),
            new ParallelOptions { MaxDegreeOfParallelism = Lanes },
            async (i, _) =>
            {
                ratings[i] = await RateAsync(turns[i].Turn.Text);
                var n = Interlocked.Increment(ref done);
                if (n % 500 == 0) Console.WriteLine($"  rated {n}/{turns.Count} ({stopwatch.Elapsed.TotalMinutes:F1} min)");
            });
        SaveCache(cachePath, cache);

        var rows = turns.Select((t, i) => new Row(
            cited.Contains((t.Conv, t.Turn.DiaId)), t.Turn.Text.Length, ratings[i].Arousal,
            ratings[i].Valence is { } v ? Math.Abs(v - 5) : null, LexiconArousal(t.Turn.Text, lexicon))).ToList();

        var unparsed = rows.Count(r => r.Arousal is null);
        var uncovered = rows.Count(r => r.LexiconArousal is null);
        var positives = rows.Count(r => r.Evidence);

        Console.WriteLine();
        Console.WriteLine("Controls");
        var c1 = controlModel >= ControlFloor;
        Console.WriteLine($"  C1 the model separates 5 calm from 5 intense fixed sentences: AUC {controlModel:0.000}  " +
            $"{(c1 ? "ok" : "<== SUSPECT")}   (lexicon on the same: {controlLexicon:0.000})");
        var c2 = unparsed <= rows.Count / 20;
        Console.WriteLine($"  C2 the model answered: {rows.Count - unparsed}/{rows.Count} turns, {unparsed} unparsed " +
            $"(fail-open, excluded)  {(c2 ? "ok" : "<== SUSPECT")}");
        Console.WriteLine($"  C3 evidence ids resolved to a turn: {resolved}/{cited.Count}; lexicon matched a word in " +
            $"{rows.Count - uncovered}/{rows.Count} turns");

        var quartile = Quartiles(rows.Select(r => r.Length).ToList());
        Console.WriteLine();
        Console.WriteLine($"{rows.Count} turns, {positives} cited as evidence by a category 1-4 question " +
            $"(base rate {(double)positives / rows.Count:P1})");
        Console.WriteLine($"  {"signal",-24} {"AUC",7} {"AUC within length quartiles",28} {"top-decile evidence rate",25}");
        var model = Report("model arousal", rows, r => r.Arousal, quartile);
        Report("model |valence - 5|", rows, r => r.ValenceExtremity, quartile);
        Report("lexicon arousal (mean)", rows, r => r.LexiconArousal, quartile);
        Report("length (chars) — control", rows, r => r.Length, quartile);

        Console.WriteLine();
        Console.WriteLine(!(c1 && c2)
            ? "VERDICT WITHHELD — an instrument check failed above."
            : model >= SignalFloor
                ? $"VERDICT: YES — model arousal separates evidence at {model:0.000} within length quartiles (floor {SignalFloor:0.00})."
                : $"VERDICT: NO — model arousal reaches {model:0.000} within length quartiles, under the pre-registered {SignalFloor:0.00}.");
        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalMinutes:F1} min; ratings cached at {cachePath}.");
        return 0;
    }

    private static string Prompt(string text) =>
        $$"""
        Rate the emotional tone of this chat message.
        Arousal: 1 = calm, flat, routine; 9 = intense, excited, agitated.
        Valence: 1 = very negative; 5 = neutral; 9 = very positive.

        Message: {{text}}

        Answer with ONLY: {"arousal": <1-9>, "valence": <1-9>}
        """;

    private static Rating Parse(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return new(null, null);
        static double? Read(string reply, string name) =>
            Regex.Match(reply, $"\"{name}\"\\s*:\\s*([1-9])\\b") is { Success: true } m
                ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)
                : null;
        return new(Read(reply, "arousal"), Read(reply, "valence"));
    }

    /// <summary>AUC overall and within length quartiles, plus how evidence-dense the top tenth by this signal
    /// is — the share a retention boost on it would protect. Returns the stratified AUC.</summary>
    private static double Report(string label, IReadOnlyList<Row> rows, Func<Row, double?> signal,
        Func<int, int> quartile)
    {
        var scored = rows.Where(r => signal(r) is not null).ToList();
        double Value(Row r) => signal(r)!.Value;
        var raw = Auc([.. scored.Where(r => r.Evidence).Select(Value)], [.. scored.Where(r => !r.Evidence).Select(Value)]);

        double weighted = 0, weight = 0;
        foreach (var group in scored.GroupBy(r => quartile(r.Length)))
        {
            var pos = group.Where(r => r.Evidence).Select(Value).ToList();
            var neg = group.Where(r => !r.Evidence).Select(Value).ToList();
            if (pos.Count == 0 || neg.Count == 0) continue;
            weighted += Auc(pos, neg) * pos.Count * neg.Count;
            weight += (double)pos.Count * neg.Count;
        }
        var stratified = weight == 0 ? double.NaN : weighted / weight;

        var top = scored.OrderByDescending(Value).Take(Math.Max(1, scored.Count / 10)).ToList();
        var baseRate = (double)scored.Count(r => r.Evidence) / scored.Count;
        var topRate = (double)top.Count(r => r.Evidence) / top.Count;
        Console.WriteLine($"  {label,-24} {raw,7:0.000} {stratified,28:0.000} {$"{topRate:P1} ({topRate / baseRate:0.00}x)",25}");
        return stratified;
    }

    /// <summary>Rank-sum AUC with ties at half credit; a NaN on either side is dropped.</summary>
    private static double Auc(IReadOnlyList<double> positives, IReadOnlyList<double> negatives)
    {
        var p = positives.Where(x => !double.IsNaN(x)).ToList();
        var n = negatives.Where(x => !double.IsNaN(x)).ToList();
        if (p.Count == 0 || n.Count == 0) return double.NaN;
        double wins = 0;
        foreach (var a in p)
            foreach (var b in n)
                wins += a > b ? 1 : a == b ? 0.5 : 0;
        return wins / ((double)p.Count * n.Count);
    }

    private static Func<int, int> Quartiles(List<int> lengths)
    {
        var sorted = lengths.OrderBy(x => x).ToList();
        var cuts = new[] { 0.25, 0.5, 0.75 }.Select(q => sorted[(int)(q * (sorted.Count - 1))]).ToArray();
        return length => cuts.Count(c => length > c);
    }

    /// <summary>Mean arousal of the lexicon words a turn contains, on the lexicon's own −1..1 scale; null when
    /// it contains none.</summary>
    private static double? LexiconArousal(string text, IReadOnlyDictionary<string, double> lexicon)
    {
        var hits = Regex.Matches(text.ToLowerInvariant(), "[a-z']+")
            .Select(m => m.Value.Trim('\''))
            .Where(lexicon.ContainsKey)
            .Select(w => lexicon[w])
            .ToList();
        return hits.Count == 0 ? null : hits.Average();
    }

    private static Dictionary<string, double> LoadLexicon(string path) =>
        File.ReadLines(path).Skip(1)
            .Select(line => line.Split('\t'))
            .Where(f => f.Length >= 3)
            .GroupBy(f => f[0], StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => double.Parse(g.First()[2], CultureInfo.InvariantCulture),
                StringComparer.Ordinal);

    /// <summary>LoCoMo's evidence field is a list, but some entries pack several ids into one string.</summary>
    private static IEnumerable<string> SplitEvidence(string evidence) =>
        evidence.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.Replace(" ", "", StringComparison.Ordinal));

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..24];

    private static ConcurrentDictionary<string, Rating> LoadCache(string path)
    {
        var cache = new ConcurrentDictionary<string, Rating>(StringComparer.Ordinal);
        if (!File.Exists(path)) return cache;
        foreach (var f in File.ReadLines(path).Select(l => l.Split('\t')).Where(f => f.Length == 3))
            cache[f[0]] = new(Num(f[1]), Num(f[2]));
        return cache;

        static double? Num(string s) => s == "-" ? null : double.Parse(s, CultureInfo.InvariantCulture);
    }

    private static void SaveCache(string path, ConcurrentDictionary<string, Rating> cache) =>
        File.WriteAllLines(path, cache.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e =>
            $"{e.Key}\t{Fmt(e.Value.Arousal)}\t{Fmt(e.Value.Valence)}"));

    private static string Fmt(double? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static void PrintPreamble(string model, int turns, int questions)
    {
        Console.WriteLine();
        Console.WriteLine("memory-affect — does a turn's AROUSAL predict whether it is asked about later?");
        Console.WriteLine();
        Console.WriteLine($"  corpus     LoCoMo: {turns} turns, {questions} category 1-4 questions; positive = cited as evidence");
        Console.WriteLine($"  detectors  {model} (1-9, temperature 0, fail-open COUNTED) / NRC-VAD v2.1 unigrams (model-free)");
        Console.WriteLine($"  bar        PRE-REGISTERED: model arousal AUC >= {SignalFloor:0.00} WITHIN length quartiles");
        Console.WriteLine();
        Console.WriteLine("  NOT measured: whether acting on it moves recall (memory-importance says a perfect importance");
        Console.WriteLine("  signal redistributes rather than improves); 'unresolved' as a state; query-less recall, which");
        Console.WriteLine("  no benchmark here has ground truth for. English only.");
    }
}
