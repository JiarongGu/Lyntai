using System.Diagnostics;
using System.Text.Json;
using Lyntai.Memory;
using Lyntai.Storage;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Memory.Corpus;
using Lyntai.Tests.Memory.Prototype;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Benchmarks;

/// <summary>
/// VN2 on the REAL record set: the partial-amendment case, run on a projection over curated memory and on a claim
/// ledger, each over SQLite and over files (<c>memory-corpus --profile</c>; the corpus is the one
/// <see cref="MemoryCorpusBench"/> reads, never carried here, and the output is numbers).
///
/// <para><b>What a field is.</b> A decision is a SUBJECT and each of its non-amendment sections is a FIELD, named by
/// the section's lead word. A decision's dated notes, and a later decision that says it amends this one, are the
/// AMENDMENTS: each states one new claim. <b>No record says which field an amendment changes</b> (that is the finding
/// <c>corpus-amendments-supersession</c> recorded as "what ends is a claim the caller knows"), so the target here is
/// the field whose text shares the most content words with the amendment, and a target is called DECISIVE when its
/// overlap beats the runner-up's by 0.10. That margin was fixed before the first run and is not tuned.</para>
///
/// <para><b>What is measured.</b> The corpus's shape (how often an amendment leaves other fields standing, and how
/// often its target is identifiable from the record alone), then for each option and store: whether every field of
/// every amended decision READS exactly (no search, no model, no ranking), whether the history and an as-of read
/// agree, that no read wrote anything, and the cost of ingest and of a read. The graph's own ranking is not run
/// (<b>D217</b>).</para>
/// </summary>
internal static class ProfileAmendmentBench
{
    private const double DecisiveMargin = 0.10;
    private const int MinFieldChars = 40;

    private sealed record Field(string Id, string Body);

    private sealed record Subject(string Key, DateTimeOffset From, IReadOnlyList<Field> Fields);

    private sealed record Amendment(string Kind, string Subject, string Field, string Claim, DateTimeOffset From,
        double Top, double Margin, int FieldCount)
    {
        public bool Decisive => Margin >= DecisiveMargin;
    }

    private sealed record ArmResult(string Arm, int Claims, double IngestSeconds, long Bytes, int Amended, int Fields,
        int ReadsCorrect, int StaleReturned, int StandingLost, int AsOfOk, int AsOfTotal, int HistoryOk, int HistoryTotal, int Writes,
        double ReadP50, double ReadP95, int Clamped);

    public static async Task<int> RunAsync(IReadOnlyList<CorpusRecord> records)
    {
        var decisions = records.Where(r => r.Kind == RecordKind.Decision && r.Date is not null).ToList();
        var subjects = decisions.Select(d => new Subject(d.Key, At(d.Date!.Value), FieldsOf(d)))
            .Where(s => s.Fields.Count > 0).ToDictionary(s => s.Key);
        var (amendments, undatable) = Amendments(decisions, subjects);
        var now = At(decisions.Max(d => d.Date!.Value).AddDays(30));
        Describe(subjects, amendments, undatable);

        var results = new List<ArmResult>();
        foreach (var arm in new[] { ("curated-projection/sqlite", false, true), ("curated-projection/files", false, false),
                     ("claim-ledger/sqlite", true, true), ("claim-ledger/files", true, false) })
        {
            Console.WriteLine($"-- {arm.Item1}: ingesting {subjects.Values.Sum(s => s.Fields.Count)} claims and {amendments.Count} amendments…");
            results.Add(await RunArmAsync(arm.Item1, arm.Item2, arm.Item3, subjects, amendments, now));
        }

        Print(results);
        Save(subjects, amendments, undatable, results);
        return 0;
    }

    private static IReadOnlyList<Field> FieldsOf(CorpusRecord decision)
    {
        var seen = new Dictionary<string, int>();
        var fields = new List<Field>();
        foreach (var s in decision.Sections.Where(s => !s.IsAmendment && s.Label != "amends" && s.Body.Length >= MinFieldChars))
        {
            var n = seen[s.Label] = seen.GetValueOrDefault(s.Label) + 1;
            fields.Add(new Field(n == 1 ? s.Label : $"{s.Label}#{n}", s.Body));
        }

        return fields;
    }

    private static (List<Amendment> Amendments, int Undatable) Amendments(
        IReadOnlyList<CorpusRecord> decisions, Dictionary<string, Subject> subjects)
    {
        var list = new List<Amendment>();
        var undatable = 0;

        void Add(string kind, string subject, string claim, string text, DateTimeOffset from)
        {
            var scores = subjects[subject].Fields.Select(f => (f.Id, Score: RecallScoring.Overlap(text, [f.Body])))
                .OrderByDescending(x => x.Score).ToList();
            var margin = scores.Count > 1 ? scores[0].Score - scores[1].Score : scores[0].Score;
            list.Add(new Amendment(kind, subject, scores[0].Id, claim, from, scores[0].Score, margin, scores.Count));
        }

        foreach (var d in decisions.Where(d => subjects.ContainsKey(d.Key)))
        {
            foreach (var note in d.Sections.Where(s => s.IsAmendment && s.Headline.Length > 0))
            {
                if (RecordParser.DateIn(note.Tag) is not { } date) { undatable++; continue; }
                Add("note", d.Key, note.Headline, note.Headline + " " + note.Body, At(date));
            }

            // a later decision that says it amends this one: its title is the claim, its first section the text
            foreach (var target in d.Amends.Where(subjects.ContainsKey))
                Add("amender", target, d.Title, d.Title + " " + (d.Sections.FirstOrDefault(s => !s.IsAmendment)?.Body ?? ""), At(d.Date!.Value));
        }

        return (list, undatable);
    }

    private static void Describe(Dictionary<string, Subject> subjects, List<Amendment> amendments, int undatable)
    {
        var amended = amendments.Select(a => a.Subject).Distinct().ToList();
        var counts = amended.Select(k => subjects[k].Fields.Count).OrderBy(x => x).ToList();
        var partial = amendments.Where(a => a.FieldCount >= 2).ToList();
        Console.WriteLine($"corpus: {subjects.Count} decisions with fields, {subjects.Values.Sum(s => s.Fields.Count)} fields; "
            + $"{amendments.Count} datable amendments ({amendments.Count(a => a.Kind == "note")} notes, "
            + $"{amendments.Count(a => a.Kind == "amender")} amenders; {undatable} undatable notes) on {amended.Count} decisions");
        Console.WriteLine($"  fields per amended decision: median {counts[counts.Count / 2]}, max {counts[^1]}; "
            + $"{partial.Count} of {amendments.Count} amendments hit a decision with 2+ fields (a PARTIAL amendment)");
        Console.WriteLine($"  a whole-record ending would end {partial.Sum(a => a.FieldCount - 1)} standing fields that these amendments leave alone");
        Console.WriteLine($"  target identifiable from the record alone (overlap margin >= {DecisiveMargin:F2}): "
            + $"{partial.Count(a => a.Decisive)} of {partial.Count} partial amendments ({partial.Count(a => a.Decisive) / (double)Math.Max(1, partial.Count):P0}); "
            + $"median top overlap {partial.Select(a => a.Top).OrderBy(x => x).ElementAtOrDefault(partial.Count / 2):F2}");
    }

    private static async Task<ArmResult> RunArmAsync(string name, bool ledger, bool sqlite,
        Dictionary<string, Subject> subjects, List<Amendment> amendments, DateTimeOffset now)
    {
        using var db = sqlite ? new MemoryPolicySweep.SweepDb() : null;
        var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "devtools", "_vn2-files", Guid.NewGuid().ToString("N"));
        ServiceProvider? sp = null;
        try
        {
            IProfileOption option;
            if (sqlite)
                option = ledger ? new ProfileLedger(new SqliteMemoryGraphStore(db!.Factory), now)
                    : new ProfileOverCurated(new SqliteCuratedMemoryStore(db!.Factory));
            else
            {
                var services = new ServiceCollection();
                services.AddLyntai(b => b.UseFileSystemStorage(o => o.Root = Path.GetFullPath(dir)));
                sp = services.BuildServiceProvider();
                option = ledger ? new ProfileLedger(sp.GetRequiredService<IMemoryGraphStore>(), now)
                    : new ProfileOverCurated(sp.GetRequiredService<ICuratedMemoryStore>());
            }

            var clock = Stopwatch.StartNew();
            var held = new Dictionary<(string, string), ProfileClaim>();
            var expect = new Dictionary<(string, string), string>();
            foreach (var s in subjects.Values)
                foreach (var f in s.Fields)
                {
                    var key = (s.Key, f.Id);
                    held[key] = await option.SetAsync(new ProfileField(s.Key, f.Id), f.Body, s.From, "record");
                    expect[key] = f.Body;
                }

            var clamped = 0;
            var applied = new List<(Amendment A, DateTimeOffset At, string Before)>();
            foreach (var a in amendments.OrderBy(a => a.From).ThenBy(a => a.Subject))
            {
                var key = (a.Subject, a.Field);
                var field = new ProfileField(a.Subject, a.Field);
                // strictly after the claim it replaces, or the replaced interval is empty
                var at = a.From > held[key].ValidFrom ? a.From : held[key].ValidFrom.AddDays(1);
                if (at != a.From) clamped++;
                var current = (await option.ReadAsync(field, now)).Current!;
                if (await option.CorrectAsync(field, a.Claim, at, a.Kind + ":" + a.Subject, current.Handle) != Correction.Applied)
                    throw new InvalidOperationException($"{name}: a correction of {a.Subject}/{a.Field} was not applied");
                applied.Add((a, at, expect[key]));
                held[key] = (await option.ReadAsync(field, now)).Current!;
                expect[key] = a.Claim;
            }

            var ingest = clock.Elapsed.TotalSeconds;

            var amendedSubjects = amendments.Select(a => a.Subject).Distinct().ToList();
            var targets = amendments.Select(a => (a.Subject, a.Field)).ToHashSet();
            var before = new Dictionary<string, string>();
            foreach (var s in amendedSubjects) before[s] = await option.FingerprintAsync(s);

            int correct = 0, stale = 0, standingLost = 0, fieldsRead = 0;
            var times = new List<double>();
            foreach (var s in subjects.Values)
                foreach (var f in s.Fields)
                {
                    var key = (s.Key, f.Id);
                    var t = Stopwatch.StartNew();
                    var read = (await option.ReadAsync(new ProfileField(s.Key, f.Id), now)).Current;
                    times.Add(t.Elapsed.TotalMilliseconds);
                    if (!amendedSubjects.Contains(s.Key)) continue;
                    fieldsRead++;
                    if (read?.Value == expect[key]) correct++;
                    else if (targets.Contains(key) && read?.Value == f.Body) stale++;
                    else if (!targets.Contains(key)) standingLost++;
                }

            var writes = 0;
            foreach (var s in amendedSubjects) if (before[s] != await option.FingerprintAsync(s)) writes++;

            int asOfOk = 0, historyOk = 0;
            foreach (var (a, at, previous) in applied)
            {
                var field = new ProfileField(a.Subject, a.Field);
                if ((await option.AsOfAsync(field, at.AddDays(-1)))?.Value == previous) asOfOk++;
            }

            foreach (var g in applied.GroupBy(x => (x.A.Subject, x.A.Field)))
                if ((await option.HistoryAsync(new ProfileField(g.Key.Subject, g.Key.Field))).Count == 1 + g.Count()) historyOk++;

            times.Sort();
            var bytes = sqlite ? SqliteBytes(db!) : DirBytes(dir);
            return new ArmResult(name, expect.Count + applied.Count, ingest, bytes, amendedSubjects.Count, fieldsRead, correct,
                stale, standingLost, asOfOk, applied.Count, historyOk, applied.Select(x => (x.A.Subject, x.A.Field)).Distinct().Count(), writes,
                times[times.Count / 2], times[Math.Min(times.Count - 1, (int)(0.95 * times.Count))], clamped);
        }
        finally
        {
            sp?.Dispose();
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch (IOException) { /* gitignored scratch */ }
        }
    }

    private static long SqliteBytes(MemoryPolicySweep.SweepDb db)
    {
        db.Checkpoint();
        return new[] { db.DbPath, db.DbPath + "-wal" }.Where(File.Exists).Sum(f => new FileInfo(f).Length);
    }

    private static long DirBytes(string dir) =>
        Directory.Exists(dir) ? new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;

    private static void Print(List<ArmResult> results)
    {
        Console.WriteLine();
        Console.WriteLine($"{"arm",-28}{"claims",7}{"ingest s",9}{"MiB",7}{"correct",14}{"stale",6}{"lost",5}{"as-of",10}{"history",9}{"writes",7}{"read p50",9}{"p95",7}");
        foreach (var r in results)
            Console.WriteLine($"{r.Arm,-28}{r.Claims,7}{r.IngestSeconds,9:F1}{r.Bytes / 1048576.0,7:F1}"
                + $"{r.ReadsCorrect + "/" + r.Fields,14}{r.StaleReturned,6}{r.StandingLost,5}{r.AsOfOk + "/" + r.AsOfTotal,10}"
                + $"{r.HistoryOk + "/" + r.HistoryTotal,9}{r.Writes,7}{r.ReadP50,9:F2}{r.ReadP95,7:F2}");
        Console.WriteLine("  correct = fields of an amended decision whose exact read returned the expected value; stale = an amended field that"
            + " still read its original; lost = a standing field that did not read; writes = amended subjects a read changed; times in ms");
    }

    private static void Save(Dictionary<string, Subject> subjects, List<Amendment> amendments, int undatable, List<ArmResult> results)
    {
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "devtools", "_corpus-results"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"profile-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        var partial = amendments.Where(a => a.FieldCount >= 2).ToList();
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            decisions = subjects.Count, fields = subjects.Values.Sum(s => s.Fields.Count),
            amendments = amendments.Count, notes = amendments.Count(a => a.Kind == "note"),
            amenders = amendments.Count(a => a.Kind == "amender"), undatable,
            amendedDecisions = amendments.Select(a => a.Subject).Distinct().Count(),
            partial = partial.Count, decisive = partial.Count(a => a.Decisive),
            standingFieldsAWholeEndingWouldEnd = partial.Sum(a => a.FieldCount - 1),
            results,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"\nresults → {path}");
    }

    private static DateTimeOffset At(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
