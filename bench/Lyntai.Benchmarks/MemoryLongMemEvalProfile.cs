using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

using Lyntai.Memory;
using Lyntai.Memory.Engines;

namespace Lyntai.Benchmarks;

/// <summary><c>--profile</c>: LLM fact extraction into a PROFILE written beside the raw exchange, on valid time
/// (<c>TASKS.md</c> Part 348, RV20). A model reads each session in date order against the profile so far and
/// states the attributes it sets or changes; a change is written with <c>ValidFrom</c> the session's date and
/// <c>Supersedes</c> the entry that attribute held, so a default (Current) recall reads the latest value and the
/// former one leaves the page without being buried or deleted.
///
/// <para><b>Scored at the ANSWER</b>, as <c>--layers</c> is, because that is where this class loses: the
/// current fact is on the page 87.1% of the time and the reader still fails 31.4%
/// (<c>docs/memory-measurements.md</c> §5). The arms are the raw page, the profile alone, the two together, and
/// the SAME facts written untimed — the control that prices what valid time adds over extraction, which
/// <c>--extract</c> already refuted without it.</para></summary>
internal static partial class MemoryLongMemEvalBench
{
    private const string ProfileScope = "profile";
    private const int ProfileRecallLimit = 10;
    private const int ProfileBesideLimit = 5;

    /// <summary>One attribute a session stated: the turn it came from, the key it was filed under and the value.
    /// <c>Tag</c> is the same <c>(sNtM)</c> marker the model-free page metrics match on.</summary>
    private sealed record Assertion(int Session, string Date, int TurnIndex, string Tag, string Key, string Value);

    /// <summary>What reading one question's sessions cost and returned. Cached to disk by extractor and variant,
    /// so a run that stops resumes instead of paying the model again.</summary>
    private sealed record ProfileExtraction(List<Assertion> Assertions, int Calls, int Sessions, int EmptySessions,
        int Miscited, int KeysMinted, int KeysReused, long InChars, long OutChars, long Millis);

    private static readonly Regex AssertionLine =
        new(@"^\[(\d+)\]\s*([A-Za-z0-9_.\- ]{1,60}?)\s*=\s*(.+)$", RegexOptions.Compiled);

    private const string ProfileInstruction = """
        You maintain a PROFILE of the user: one line per attribute, written `key = value`.

        Below are the user's messages from ONE conversation session, each numbered. Write the profile lines this
        session states or CHANGES, one per line. EVERY line starts with the number of the message it came from,
        in square brackets, exactly like this example:
        [0] residence = Denver
        [4] dog_count = 2

        Rules:
        - Reuse a key from the known profile when the attribute is the same. Mint a short snake_case key only when
          no known key fits.
        - A value that replaces an earlier one (a new count, a new best time, a move, a new job) goes under the
          SAME key with the NEW value.
        - State only what the user says about themselves and their life. Keep names, numbers and dates exactly as written.
        - Skip questions, requests, opinions about the conversation and anything that is not a durable fact.
        - If the session states nothing durable, reply exactly: NONE
        - No commentary.
        """;

    /// <summary>Longest user message handed to the extractor, so one pasted document cannot dominate a call.</summary>
    private const int ProfileTurnChars = 2500;

    /// <summary>Profile lines shown to the extractor: the most recently touched ones, so the prompt does not grow
    /// with the corpus. Reported, because a bound the model never sees past is a recall limit of its own.</summary>
    private const int ProfileKnownLines = 60;

    private static readonly DateTimeOffset NeverDate = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>LongMemEval's <c>2023/05/20 (Sat) 02:21</c>, read as UTC. The weekday is dropped; an unparseable
    /// date yields <see cref="NeverDate"/> and is counted by the caller.</summary>
    private static DateTimeOffset ParseSessionDate(string text, out bool parsed)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var at = NeverDate;
        parsed = parts.Length >= 1 && DateTimeOffset.TryParseExact(
            parts.Length >= 3 ? $"{parts[0]} {parts[^1]}" : parts[0],
            parts.Length >= 3 ? "yyyy/MM/dd HH:mm" : "yyyy/MM/dd",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out at);
        return parsed ? at : NeverDate;
    }

    private static string ProfileKey(string raw) =>
        string.Join('_', raw.ToLowerInvariant().Split([' ', '-', '.', '_'], StringSplitOptions.RemoveEmptyEntries));

    private static string ProfileValue(string raw) => raw.Trim().TrimEnd('.').Trim();

    private static bool SameValue(string a, string b) =>
        string.Equals(string.Join(' ', MemoryLocomoBench.Normalize(a)), string.Join(' ', MemoryLocomoBench.Normalize(b)), StringComparison.Ordinal);

    /// <summary>Reads one question's sessions in date order, each against the profile so far, USER turns only.
    /// <para><b>User turns only is a stated design choice, and it is measured to lose nothing:</b> every flagged
    /// evidence turn in the knowledge-update class is a user turn (144 of 144 in the oracle file), and the user's
    /// own words are where a profile's facts come from. It is also what makes a call cheap enough to run on every
    /// session of a haystack.</para></summary>
    private static async Task<ProfileExtraction> ExtractProfileAsync(Question q, SweepDoubles.IBenchChat chat)
    {
        var known = new Dictionary<string, (string Value, int Touched)>(StringComparer.Ordinal);
        var assertions = new List<Assertion>();
        int calls = 0, sessions = 0, empty = 0, miscited = 0, minted = 0, reused = 0, touch = 0;
        long inChars = 0, outChars = 0, millis = 0;

        foreach (var session in q.Turns.GroupBy(t => t.Session))
        {
            var userTurns = session.Where(t => t.Role == "user").ToList();
            if (userTurns.Count == 0) continue;
            sessions++;

            var profile = known.Count == 0
                ? "(empty)"
                : string.Join("\n", known.OrderByDescending(k => k.Value.Touched).Take(ProfileKnownLines)
                    .Select(k => $"{k.Key} = {Clip(k.Value.Value, 120)}"));
            var messages = string.Join("\n", userTurns.Select(t =>
                $"[{t.Index}] {Clip(t.Content, ProfileTurnChars)}"));
            var prompt = $"{ProfileInstruction}\n\nKnown profile so far:\n{profile}\n\nSession messages:\n{messages}";

            var clock = Stopwatch.StartNew();
            var reply = await chat.AskAsync(prompt, maxTokens: 700).ConfigureAwait(false);
            millis += clock.ElapsedMilliseconds;
            calls++;
            inChars += prompt.Length;
            outChars += reply?.Length ?? 0;

            var before = assertions.Count;
            foreach (var line in (reply ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (line.Equals("NONE", StringComparison.OrdinalIgnoreCase)) continue;
                var m = AssertionLine.Match(line);
                if (!m.Success || !int.TryParse(m.Groups[1].Value, out var turn)
                    || userTurns.FirstOrDefault(t => t.Index == turn) is not { } source)
                {
                    miscited++;
                    continue;
                }

                var key = ProfileKey(m.Groups[2].Value);
                var value = ProfileValue(m.Groups[3].Value);
                if (key.Length == 0 || value.Length == 0) { miscited++; continue; }

                if (known.ContainsKey(key)) reused++; else minted++;
                known[key] = (value, ++touch);
                assertions.Add(new Assertion(source.Session, source.Date, source.Index, source.Tag, key, value));
            }

            if (assertions.Count == before) empty++;
        }

        return new ProfileExtraction(assertions, calls, sessions, empty, miscited, minted, reused, inChars, outChars, millis);
    }

    private static string Clip(string text, int chars) => text.Length <= chars ? text : text[..chars];

    /// <summary>The extraction for one question, from the disk cache when this extractor already read it.</summary>
    private static async Task<ProfileExtraction> CachedProfileAsync(
        Question q, SweepDoubles.IBenchChat chat, string cacheDir)
    {
        var file = Path.Combine(cacheDir, $"{q.Id}.json");
        if (File.Exists(file))
            return JsonSerializer.Deserialize<ProfileExtraction>(await File.ReadAllTextAsync(file).ConfigureAwait(false))!;

        var fresh = await ExtractProfileAsync(q, chat).ConfigureAwait(false);
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(fresh)).ConfigureAwait(false);
        return fresh;
    }

    /// <summary>What writing one question's profile did.</summary>
    private sealed class ProfileWriteStats
    {
        internal int Written, Superseded, Restated, Refused, ProvenanceTotal, ProvenanceResolved;
    }

    /// <summary>Writes the assertions in order into <paramref name="engine"/>. <paramref name="timed"/> states valid
    /// time and names the entry a changed attribute replaces; untimed writes the same changes with neither, which is
    /// the extraction-only shape. A restatement of the value an attribute already holds writes nothing new — it only
    /// widens the entry's provenance — in BOTH shapes, so the two stores hold the same facts.</summary>
    private static async Task<ProfileWriteStats> WriteProfileAsync(GraphMemoryEngine engine,
        IReadOnlyList<Assertion> assertions, IReadOnlyDictionary<string, string> rawIdOfTag, bool timed)
    {
        var stats = new ProfileWriteStats();
        var held = new Dictionary<string, (MemoryRef Ref, string Value, string Content, DateTimeOffset From,
            List<string> Sources)>(StringComparer.Ordinal);

        foreach (var a in assertions)
        {
            var from = ParseSessionDate(a.Date, out _);
            var source = rawIdOfTag.TryGetValue(a.Tag, out var rawId) ? rawId : null;
            if (source is not null) { stats.ProvenanceTotal++; stats.ProvenanceResolved++; }
            else stats.ProvenanceTotal++;

            if (held.TryGetValue(a.Key, out var prior) && SameValue(prior.Value, a.Value))
            {
                if (source is not null && !prior.Sources.Contains(source)) prior.Sources.Add(source);
                await engine.RememberAsync(new MemoryWrite(Task, ProfileScope, prior.Content,
                    Metadata: Provenance(a.Key, prior.Sources))).ConfigureAwait(false);
                stats.Restated++;
                continue;
            }

            // The key is stored with spaces: a snake_case token shares no word with the question that asks for it,
            // and the default seed is lexical.
            var content = $"{a.Tag} {a.Key.Replace('_', ' ')}: {a.Value}";
            var sources = source is null ? [] : new List<string> { source };
            var write = new MemoryWrite(Task, ProfileScope, content, Metadata: Provenance(a.Key, sources));
            if (timed)
            {
                var start = held.TryGetValue(a.Key, out var former) && former.From > from ? former.From : from;
                write = held.TryGetValue(a.Key, out var replaced)
                    ? write with { ValidFrom = start, Supersedes = [replaced.Ref] }
                    : write with { ValidFrom = start };
            }

            try
            {
                var written = await engine.RememberAsync(write).ConfigureAwait(false);
                held[a.Key] = (written.Reference, a.Value, content, from, sources);
                stats.Written++;
                if (timed && write.Supersedes is { Count: > 0 }) stats.Superseded++;
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                stats.Refused++;
            }
        }

        return stats;
    }

    private static Dictionary<string, string> Provenance(string key, List<string> sources) =>
        new() { ["key"] = key, ["sources"] = string.Join(',', sources) };

    private static string Slug(string text) =>
        new([.. text.Select(c => char.IsLetterOrDigit(c) ? c : '-')]);

    /// <summary>One arm's per-question outcome: the answer's grade and the page the reader saw, scored model-free.</summary>
    private sealed record ProfileOutcome(bool Answered, double F1, bool Preferred, bool Decidable, bool CurrentHit,
        bool StaleHit, int ContextChars);

    private static async Task<int> RunProfileAsync(List<Question> sampled,
        SweepDoubles.CachingVectorProvider vectorProvider, SweepDoubles.OpenAiCompatibleChat reader,
        SweepDoubles.IBenchChat writer, bool haystack, string[] args)
    {
        var parallel = ArgValue(args, "--parallel") is { } p && int.TryParse(p, out var pp) && pp > 0 ? pp : 4;
        var cacheDir = Path.Combine("devtools", "_rv20-cache", $"{Slug(writer.Model)}-{(haystack ? "s" : "oracle")}");
        Directory.CreateDirectory(cacheDir);

        // PHASE 1 — read every question's sessions. Independent across questions, dependent within one (the
        // profile so far is part of the prompt), so this fans out by question and resumes from the cache.
        var extractions = new ProfileExtraction[sampled.Count];
        var reading = 0;
        var wall = Stopwatch.StartNew();
        await Parallel.ForEachAsync(Enumerable.Range(0, sampled.Count),
            new ParallelOptions { MaxDegreeOfParallelism = parallel },
            async (i, _) =>
            {
                extractions[i] = await CachedProfileAsync(sampled[i], writer, cacheDir).ConfigureAwait(false);
                var n = Interlocked.Increment(ref reading);
                if (!Console.IsErrorRedirected) Console.Error.Write($"\r  reading sessions: question {n}/{sampled.Count}   ");
            });
        if (!Console.IsErrorRedirected) Console.Error.WriteLine();
        var readingSeconds = wall.Elapsed.TotalSeconds;
        PrintIdentity(sampled, extractions);
        if (args.Contains("--identity-only")) return 0;

        // PHASE 2 — write the three stores per question and read each as a reader would.
        string[] arms = ["raw", "profile", "profile-untimed", "raw+profile", "raw+profile-untimed"];
        var outcomes = arms.ToDictionary(a => a, _ => new List<ProfileOutcome>(), StringComparer.Ordinal);
        var perQuestion = arms.ToDictionary(a => a, _ => new Dictionary<string, bool>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        var timedStats = new ProfileWriteStats();
        var untimedStats = new ProfileWriteStats();
        var recallMillis = new Dictionary<string, List<double>>(StringComparer.Ordinal)
            { ["raw"] = [], ["profile"] = [], ["profile-untimed"] = [] };
        var survivedCurrent = 0;
        var survivedStale = 0;
        var currentEvidence = 0;
        var staleEvidence = 0;
        int unparsedDates = 0;

        // `--dump` keeps what each arm showed the reader and what it answered, for reading the failures.
        List<string>? dump = args.Contains("--dump") ? [] : null;
        var done = 0;
        var embedsBefore = vectorProvider.Misses;
        foreach (var (q, ex) in sampled.Zip(extractions))
        {
            Progress(++done, sampled.Count);
            unparsedDates += ex.Assertions.Count(a => { ParseSessionDate(a.Date, out var ok); return !ok; });

            var tagged = ex.Assertions.Select(a => a.Tag).ToHashSet(StringComparer.Ordinal);
            currentEvidence += q.Current.Count;
            staleEvidence += q.Stale.Count;
            survivedCurrent += q.Current.Count(t => tagged.Contains(t.Tag));
            survivedStale += q.Stale.Count(t => tagged.Contains(t.Tag));

            // RAW — the shipped engine over the raw turns, exactly as `--layers` reads it.
            using var rawDb = new MemoryPolicySweep.SweepDb();
            var rawEngine = EngineFor(FieldArms.Shipped(), rawDb, vectorProvider);
            var turnOfId = new Dictionary<string, Turn>(StringComparer.Ordinal);
            var rawIdOfTag = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var t in q.Turns)
            {
                var id = (await rawEngine.RememberAsync(new MemoryWrite(Task, Scope, $"{t.Tag} {t.Text}")))
                    .Reference.Id;
                turnOfId[id] = t;
                rawIdOfTag[t.Tag] = id;
            }

            using var timedDb = new MemoryPolicySweep.SweepDb();
            var timedEngine = EngineFor(FieldArms.Shipped(), timedDb, vectorProvider);
            var tw = await WriteProfileAsync(timedEngine, ex.Assertions, rawIdOfTag, timed: true);
            Add(timedStats, tw);

            using var untimedDb = new MemoryPolicySweep.SweepDb();
            var untimedEngine = EngineFor(FieldArms.Shipped(), untimedDb, vectorProvider);
            Add(untimedStats, await WriteProfileAsync(untimedEngine, ex.Assertions, rawIdOfTag, timed: false));

            var turnOfTag = q.Turns.ToDictionary(t => t.Tag, StringComparer.Ordinal);

            async Task<List<MemoryItem>> PageAsync(GraphMemoryEngine engine, string scope, int limit, string arm)
            {
                var clock = Stopwatch.StartNew();
                var recall = await engine.RecallAsync(
                    new MemoryQuery(Task, scope, q.Text, Limit: limit, Detail: MemoryDetail.Full));
                if (recallMillis.TryGetValue(arm, out var list)) list.Add(clock.Elapsed.TotalMilliseconds);
                return [.. recall.Items];
            }

            var rawPage = await PageAsync(rawEngine, Scope, RecallLimit, "raw");
            var profilePage = await PageAsync(timedEngine, ProfileScope, ProfileRecallLimit, "profile");
            var untimedPage = await PageAsync(untimedEngine, ProfileScope, ProfileRecallLimit, "profile-untimed");

            // The date a reader sees on a line is the SOURCE exchange's date in every arm, so no arm is advantaged
            // by showing one and what the control isolates is supersession and the Current reading alone.
            string RawLine(MemoryItem i) =>
                $"[{(turnOfId.TryGetValue(i.Reference.Id, out var t) ? t.Date : "")}] {i.Content ?? i.Headline}";
            string FactLine(MemoryItem i)
            {
                var text = i.Content ?? i.Headline;
                var close = text.IndexOf(')');
                var date = close > 0 && turnOfTag.TryGetValue(text[..(close + 1)], out var t) ? t.Date : "";
                return $"[{date}] {text}";
            }

            List<string> RawLines() => [.. rawPage.Select(RawLine)];
            List<string> Beside(List<MemoryItem> page) =>
                [.. page.Take(ProfileBesideLimit).Select(i => $"PROFILE {FactLine(i)}"), .. RawLines()];

            var contexts = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["raw"] = RawLines(),
                ["profile"] = [.. profilePage.Select(FactLine)],
                ["profile-untimed"] = [.. untimedPage.Select(FactLine)],
                ["raw+profile"] = Beside(profilePage),
                ["raw+profile-untimed"] = Beside(untimedPage),
            };

            var question = $"{q.Text} (asked {q.Asked}; where excerpts disagree, the latest-dated is current)";
            foreach (var arm in arms)
            {
                var lines = contexts[arm];
                var context = string.Join("\n", lines);
                var hypothesis = (await reader.AskAsync(MemoryLocomoBench.AnswerPrompt(context, question), maxTokens: 48))
                    ?.Trim() ?? "";
                var unknown = hypothesis.Length == 0
                    || hypothesis.StartsWith("unknown", StringComparison.OrdinalIgnoreCase);
                var f1 = unknown ? 0 : MemoryLocomoBench.TokenF1(hypothesis, q.Answer);
                var answered = !unknown && await MemoryLocomoBench.JudgeAsync(reader, q.Text, q.Answer, hypothesis);

                var cur = FirstIndexOf(lines, q.Current);
                var sta = FirstIndexOf(lines, q.Stale);
                var decidable = cur >= 0 || sta >= 0;
                outcomes[arm].Add(new ProfileOutcome(answered, f1, cur >= 0 && (sta < 0 || cur < sta), decidable,
                    cur >= 0, sta >= 0, context.Length));
                perQuestion[arm][q.Id] = answered;
                if (dump is not null)
                    dump.Add(JsonSerializer.Serialize(new
                    {
                        q = q.Id, arm, question = q.Text, gold = q.Answer, hypothesis, answered, lines,
                    }));
            }
        }

        var embeds = vectorProvider.Misses - embedsBefore;
        PrintProfile(sampled, extractions, arms, outcomes, perQuestion, timedStats, untimedStats, recallMillis,
            readingSeconds, writer.Model, parallel, embeds, survivedCurrent, currentEvidence, survivedStale,
            staleEvidence, unparsedDates);

        if (dump is not null)
        {
            var file = Path.Combine("devtools", $"_rv20-dump-{DateTime.Now:HHmmss}.jsonl");
            await File.WriteAllLinesAsync(file, dump);
            Console.WriteLine($"  dump: {dump.Count} row(s) → {file}");
        }

        var results = new BenchResults("memory-longmemeval-profile", "answered", args);
        foreach (var arm in arms)
            for (var i = 0; i < sampled.Count; i++)
                results.Add($"{arm}/answered", sampled[i].Id, outcomes[arm][i].Answered, score: outcomes[arm][i].F1);
        await results.WriteAsync();
        return 0;

        static void Add(ProfileWriteStats total, ProfileWriteStats one)
        {
            total.Written += one.Written;
            total.Superseded += one.Superseded;
            total.Restated += one.Restated;
            total.Refused += one.Refused;
            total.ProvenanceTotal += one.ProvenanceTotal;
            total.ProvenanceResolved += one.ProvenanceResolved;
        }
    }

    /// <summary>The question valid time cannot answer: did the extractor file the REVISED value under the key the
    /// old one held? A write can only supersede an entry it names, and nothing is inferred from similar text
    /// (<c>MemoryWrite.Supersedes</c>), so a revision filed under a second key supersedes nothing and both values
    /// stay current. Counted over the questions where an assertion cites BOTH a superseded and a current gold turn.</summary>
    private static void PrintIdentity(List<Question> sampled, ProfileExtraction[] extractions)
    {
        int both = 0, sameKey = 0, sameKeyChanged = 0, otherKey = 0, curOnly = 0, staleOnly = 0, neither = 0;
        foreach (var (q, ex) in sampled.Zip(extractions))
        {
            var cur = ex.Assertions.Where(a => q.Current.Any(t => t.Tag == a.Tag)).ToList();
            var stale = ex.Assertions.Where(a => q.Stale.Any(t => t.Tag == a.Tag)).ToList();
            if (cur.Count == 0 && stale.Count == 0) { neither++; continue; }
            if (stale.Count == 0) { curOnly++; continue; }
            if (cur.Count == 0) { staleOnly++; continue; }

            both++;
            var shared = cur.Select(a => a.Key).Intersect(stale.Select(a => a.Key)).ToList();
            if (shared.Count == 0) { otherKey++; continue; }

            sameKey++;
            if (shared.Any(k => !SameValue(cur.First(a => a.Key == k).Value, stale.First(a => a.Key == k).Value)))
                sameKeyChanged++;
        }

        Console.WriteLine();
        Console.WriteLine($"  IDENTITY  {both} of {sampled.Count} questions cite BOTH a superseded and a current gold turn "
            + $"({curOnly} current only, {staleOnly} superseded only, {neither} neither).");
        Console.WriteLine($"            revision filed under the SAME key: {sameKey}/{both} "
            + $"({(both == 0 ? 0 : (double)sameKey / both):P1}), a changed value under it in {sameKeyChanged}; "
            + $"under ANOTHER key: {otherKey}/{both} ({(both == 0 ? 0 : (double)otherKey / both):P1}) - nothing to supersede.");
    }

    private static void PrintProfile(List<Question> sampled, ProfileExtraction[] extractions, string[] arms,
        Dictionary<string, List<ProfileOutcome>> outcomes, Dictionary<string, Dictionary<string, bool>> perQuestion,
        ProfileWriteStats timed, ProfileWriteStats untimed, Dictionary<string, List<double>> recallMillis,
        double readingSeconds, string model, int parallel, int embeds, int survivedCurrent, int currentEvidence,
        int survivedStale, int staleEvidence, int unparsedDates)
    {
        var n = sampled.Count;
        var calls = extractions.Sum(e => e.Calls);
        var assertions = extractions.Sum(e => e.Assertions.Count);
        var millis = extractions.Sum(e => e.Millis);
        Console.WriteLine();
        Console.WriteLine($"Extractor: {model}   user turns only, one call per session in date order, {parallel} in flight");
        Console.WriteLine($"  COST   {calls} model call(s) = {(double)calls / n:F1} per question; "
            + $"{extractions.Sum(e => e.InChars) / 4000.0:F0}k input + {extractions.Sum(e => e.OutChars) / 4000.0:F0}k output tokens (chars/4)");
        Console.WriteLine($"         {millis / 1000.0:F0} s of model time ({(calls == 0 ? 0 : (double)millis / calls):F0} ms per call, "
            + $"{(double)millis / 1000 / n:F1} s per question); wall {readingSeconds:F0} s (cached questions cost none)");
        Console.WriteLine($"         raw path: 0 model calls at write, 0 at read; recall median "
            + $"raw {BenchStats.Percentile(recallMillis["raw"], 0.5):F0} ms, profile {BenchStats.Percentile(recallMillis["profile"], 0.5):F0} ms");
        Console.WriteLine($"  READ   {extractions.Sum(e => e.Sessions)} session(s), {extractions.Sum(e => e.EmptySessions)} yielded nothing; "
            + $"{assertions} assertion(s) = {(double)assertions / n:F1} per question; "
            + $"{extractions.Sum(e => e.Miscited)} unusable line(s)");
        Console.WriteLine($"  KEYS   {extractions.Sum(e => e.KeysMinted)} minted, {extractions.Sum(e => e.KeysReused)} reused "
            + "(a reuse is the same attribute filed twice; DRIFT is the attribute named a second way)");
        Console.WriteLine($"  EVIDENCE SURVIVAL  current {survivedCurrent}/{currentEvidence} "
            + $"({(currentEvidence == 0 ? 0 : (double)survivedCurrent / currentEvidence):P1}), stale {survivedStale}/{staleEvidence} "
            + $"({(staleEvidence == 0 ? 0 : (double)survivedStale / staleEvidence):P1}) flagged turns cited by an assertion");
        Console.WriteLine($"  WRITE  timed: {timed.Written} written, {timed.Superseded} superseded, {timed.Restated} restated, "
            + $"{timed.Refused} refused; untimed: {untimed.Written} written, {untimed.Restated} restated");
        Console.WriteLine($"  PROVENANCE  {timed.ProvenanceResolved}/{timed.ProvenanceTotal} assertions name a raw entry that exists"
            + (unparsedDates > 0 ? $"   ! {unparsedDates} unparsed session date(s)" : ""));
        Console.WriteLine($"  EMBEDS {embeds} new vector call(s) across raw + profile + control stores");
        Console.WriteLine();

        Console.WriteLine($"{"Arm",-22} {"answered",22} {"F1",6} {"context",8} | {"prefers cur",12} {"current@",9} {"stale@",8} | vs raw (paired)");
        Console.WriteLine(new string('-', 112));
        foreach (var arm in arms)
        {
            var rows = outcomes[arm];
            var hit = rows.Count(r => r.Answered);
            var (low, high) = BenchStats.Wilson(hit, n);
            var decidable = rows.Count(r => r.Decidable);
            var prefers = rows.Count(r => r.Preferred);
            string paired = "";
            if (arm != "raw")
            {
                int wins = 0, losses = 0;
                foreach (var q in sampled)
                {
                    var mine = perQuestion[arm][q.Id];
                    var theirs = perQuestion["raw"][q.Id];
                    if (mine && !theirs) wins++;
                    else if (!mine && theirs) losses++;
                }
                paired = $"+{wins} -{losses}  {BenchStats.Format(BenchStats.McNemarExact(wins, losses))}";
            }

            Console.WriteLine($"{arm,-22} {$"{100.0 * hit / n:F1}% ({hit}/{n}) [{100 * low:F0},{100 * high:F0}]",22} "
                + $"{rows.Average(r => r.F1),6:F3} {rows.Average(r => r.ContextChars),8:F0} | "
                + $"{(decidable == 0 ? "-" : $"{100.0 * prefers / decidable:F1}%"),12} "
                + $"{100.0 * rows.Count(r => r.CurrentHit) / n,8:F1}% {100.0 * rows.Count(r => r.StaleHit) / n,7:F1}% | {paired}");
        }

        Console.WriteLine();
        Console.WriteLine("  answered = the 4B reader's answer judged by the same model (RecallLayers' judge); F1 is the model-free");
        Console.WriteLine("  grade. context = characters handed to the reader. current@/stale@ = a gold current / superseded turn");
        Console.WriteLine("  is cited on the page, matched by (sNtM) marker, so a fact cites the turn it came from.");
    }
}
