using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Lyntai.Inference;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Ranking;
using Lyntai.Memory.Seeding;
using Lyntai.Storage.Sqlite;

using Lyntai.Memory.Verification;
namespace Lyntai.Benchmarks;

/// <summary>
/// <b>LongMemEval's knowledge-update class — the benchmark where forgetting is supposed to HELP.</b>
///
/// <para>LoCoMo distributes its questions uniformly over months of history, so it rewards a perfect archive
/// and penalises decay by construction (<c>docs/memory-measurements.md</c> §5). This is the opposite shape. A
/// knowledge-update question carries exactly TWO dated sessions: an earlier one stating a fact and a later
/// one REVISING it. The turns that carry each are flagged, so the question this asks is not "can you find
/// it" but <b>"do you prefer the CURRENT value over the superseded one"</b> — which is the claim a decay
/// model actually makes, and one a flat cosine index has no mechanism to make at all.</para>
///
/// <para><b>The headline metric is preference, not recall.</b> Both facts are textually similar and both sit
/// in the store; an archive returns whichever the vector backend likes. Scoring "did you retrieve the answer"
/// would hide that. So the arms are scored on whether the CURRENT turn outranks the STALE one, with plain
/// hit-rates beside it so a preference win cannot be manufactured by retrieving neither.</para>
///
/// <para>Model-free throughout, for the reason the LoCoMo harness gives: no reader and no judge, so neither
/// can be credited or blamed.</para>
///
/// <para><b>Two variants, and the difference between them is the measurement.</b> The oracle file carries
/// only the evidence sessions — two to six per question — so decay has almost nothing to bury.
/// <c>--haystack</c> puts the same questions among ~490 turns of distractors. The question ids are identical
/// in both files, so a seeded <c>--n</c> sample selects the same questions in each, and the digest printed in
/// the preamble is what proves a pair of runs is paired.</para>
/// </summary>
internal static class MemoryLongMemEvalBench
{
    internal const string DataVariable = "LYNTAI_LME_PATH";
    internal const string HaystackVariable = "LYNTAI_LME_S_PATH";
    private const int RecallLimit = 10;

    /// <summary>The multi-shot walk's two bounds, at CLASS scope because both shot curves and the walk they
    /// share read them — a per-method copy is how "shot 2" starts meaning two different things.</summary>
    private const int ShotBudget = 2 * RecallLimit;
    private const int ExpandSeeds = 3;
    private const int DefaultSeed = 20260829;

    /// <summary>Recall limit for the <c>fill</c> arm — big enough that a 5,400-character budget binds before
    /// <c>k</c> does (~46 headlines at the measured ~117 characters each), and reported per run so a reader
    /// can see which of the two actually bound. <b>It is not "shot 1 with a longer tail"</b>: the engine
    /// gathers <c>limit × CandidateMultiplier</c> candidates, so raising the limit widens the POOL in
    /// proportion. The arm is the shipped recall asked for more, and that is the thing worth pricing.</summary>
    private const int DefaultFillLimit = 60;
    private const string FillArm = "fill";

    /// <summary>The COMPLETENESS arm (<c>--detail</c>): the shipped recall at the shipped <c>k</c>, asking for
    /// <see cref="MemoryDetail.Full"/> instead of headlines. Its twin is <c>shot-1</c> / <c>pool-4</c> — same
    /// limit, same multiplier, same ranking — so the pair isolates how much of each item is returned and
    /// nothing else.</summary>
    private const string CompleteArm = "full";
    private const string Task = "lme";
    private const string Scope = "session";

    /// <summary>The plain-cosine control. Not a <see cref="FieldArm"/> — it never touches the graph store,
    /// which is exactly what makes it the arm that cannot move when the engine changes.</summary>
    private const string VectorArm = "vector";

    /// <summary>Adds the cross-encoder twin of every selected arm, plus the matched control it has to be
    /// read against.
    ///
    /// <para><b>The control is not optional.</b> A reranker only works here when the seam stops truncating
    /// (`docs/memory-measurements.md` §5: 78.0% at the shipped 120 characters, 91.0% at 512), so the arm must raise
    /// <c>HeadlineChars</c> — and then a bare <c>arm</c> vs <c>arm+rerank</c> comparison would confound the
    /// reranker with the headline change. Hence the pair: <c>+hl512</c> alone, and <c>+hl512+rerank</c>.</para>
    ///
    /// <para><b>What this run is FOR.</b> LoCoMo rewards a perfect archive and this workload rewards burying
    /// a superseded fact, so a reranker that reorders by query-relevance is exactly the shape that could win
    /// there and lose here — the trade <c>RetrievabilityWeight = 0</c> made at about 7:1. No default moves
    /// until this table has been read.</para></summary>
    private static FieldArm[] WithRerank(FieldArm[] arms, CrossEncoderReranker? reranker, int limit)
    {
        if (reranker is null) return arms;
        var widened = new List<FieldArm>(arms.Length * 3);
        foreach (var arm in arms)
        {
            var options = (arm.Options ?? new GraphMemoryOptions()) with { HeadlineChars = 512 };
            widened.Add(arm);
            widened.Add(arm with { Name = $"{arm.Name}+hl512", Options = options });
            widened.Add(arm with
            {
                Name = $"{arm.Name}+hl512+rerank",
                Options = options,
                Verification = new CrossEncoderVerifier(reranker, limit),
            });
        }
        return [.. widened];
    }

    /// <summary>Which engine arms a run scores, from <c>--arms</c>.
    ///
    /// <para><b>The default is the PUBLISHED pair</b> — <c>lyntai</c> and <c>vector</c> — so every figure on
    /// record reproduces from the command that produced it, and the ladder is opt-in. That is not timidity
    /// about defaults: an arm costs a full ingestion per question, and under <c>--haystack</c> that is ~490
    /// turns each, so a silently-widened default would multiply the cost of every existing invocation.</para>
    ///
    /// <para>Arms come from <see cref="FieldArms"/>, so a name means the same configuration here as it does
    /// on the LoCoMo bench — which is the whole point of running this ladder at all.</para></summary>
    /// <returns>The selected arms, or <c>null</c> when a name is not an arm — after saying so. A typo must
    /// not quietly measure fewer arms and still print a table that looks complete.</returns>
    private static FieldArm[]? SelectConfigs(string[] args, out bool cosine)
    {
        cosine = true;
        if (ArgValue(args, "--arms") is not { } wanted) return [FieldArms.Shipped()];

        var requested = wanted.Split(',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var unknown = requested.Where(a => a != VectorArm && !FieldArms.Has(a)).ToList();
        if (unknown.Count > 0)
        {
            Console.Error.WriteLine($"--arms: unknown arm(s) {string.Join(", ", unknown)}. This mode runs: "
                + $"{string.Join(", ", FieldArms.All().Select(a => a.Name).Append(VectorArm))}");
            return null;
        }

        cosine = requested.Contains(VectorArm, StringComparer.Ordinal);
        return [.. FieldArms.All().Where(a => requested.Contains(a.Name, StringComparer.Ordinal))];
    }

    /// <summary>Narrows a SNAPSHOT mode's own arm names by <c>--arms</c>.
    ///
    /// <para><c>--shots</c> names steps of one walk rather than configurations, so this changes what is
    /// REPORTED and cannot change what is computed — unlike <see cref="SelectConfigs"/>, which also decides
    /// what is ingested. The asymmetry is the LoCoMo bench's and is kept deliberately; what is NOT kept is
    /// letting the flag be accepted and ignored, which is the silent no-op every other path here
    /// refuses.</para></summary>
    /// <returns>The names to report, or <c>null</c> after saying which name is not one of them.</returns>
    private static string[]? FilterArms(string[] args, string[] arms)
    {
        if (ArgValue(args, "--arms") is not { } wanted) return arms;

        var requested = wanted.Split(',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var unknown = requested.Where(a => !arms.Contains(a, StringComparer.Ordinal)).ToList();
        if (unknown.Count > 0)
        {
            Console.Error.WriteLine($"--arms: unknown arm(s) {string.Join(", ", unknown)}. "
                + $"This mode runs: {string.Join(", ", arms)}");
            return null;
        }

        return [.. arms.Where(a => requested.Contains(a, StringComparer.Ordinal))];
    }

    /// <summary>Turns a conversation turn into the durable facts it states, Mem0-style — the WRITE-time
    /// half this engine does not do, since it stores raw turns and resolves at read time with decay.
    ///
    /// <para><b>The source marker rides onto every extracted fact</b>, which is what makes this measurable
    /// at all: the model-free metric matches on <c>(sNtM)</c>, so a synthesized fact that kept its
    /// provenance is scorable where a real Mem0 result is not.</para>
    ///
    /// <para><b>Cached by turn text</b>, so arms that differ only in engine configuration share one
    /// extraction pass rather than paying for the model again.</para>
    ///
    /// <para><b>A positive <paramref name="budget"/> bounds the ANSWER in the prompt</b> and nowhere else —
    /// the reply is never truncated in code, because a code truncation would measure truncation rather than
    /// the model choosing. <see cref="OverBudget"/> is what says which of the two happened. Unset, the
    /// instruction is byte-identical to the unbounded run this is compared against.</para></summary>
    /// <param name="chat">The extracting model.</param>
    /// <param name="budget">Facts per turn the prompt asks for; <c>0</c> or less asks for no bound.</param>
    private sealed class FactExtractor(SweepDoubles.IBenchChat chat, int budget)
    {
        private const string Instruction = """
            You extract durable facts from one message in a conversation.

            Reply with one fact per line. Each line must stand alone and still make sense months later.

            Rules:
            - State only what the message actually says. Never infer or add.
            - Keep names, numbers and dates exactly as written.
            - Skip greetings, filler, questions and opinions about the conversation itself.
            - If the message states no durable fact, reply with exactly: NONE
            - No numbering, no bullets, no commentary.
            """;

        private readonly Dictionary<string, IReadOnlyList<string>> _cache = new(StringComparer.Ordinal);

        /// <summary>The budget is stated as a NUMBER and paired with a rule for choosing which facts to
        /// keep — "be selective" is not a budget, and a model will not invent one
        /// (<c>.claude/knowledge/pitfalls.md</c>, the unbounded-task entry).</summary>
        private readonly string _instruction = budget > 0
            ? $"{Instruction}\n- Reply with AT MOST {budget} fact{(budget == 1 ? "" : "s")}. If the message "
                + $"states more, keep only the {budget} that will still matter months later."
            : Instruction;

        /// <summary>Turns whose extraction returned nothing — the information write-time extraction DROPS,
        /// which no retrieval can recover and which a score alone would blame on the ranker.</summary>
        internal int Empty { get; private set; }

        /// <summary>Turns where the model returned MORE than the budget asked for — the fire counter this
        /// arm needs. A budget nothing obeys leaves the corpus its unbounded size, which reads in the score
        /// exactly like a budget that did not help.</summary>
        internal int OverBudget { get; private set; }

        internal int Calls { get; private set; }

        internal int Facts { get; private set; }

        internal async Task<IReadOnlyList<string>> ExtractAsync(string turn)
        {
            if (_cache.TryGetValue(turn, out var hit)) return hit;

            Calls++;
            var reply = await chat.AskAsync($"{_instruction}\n\nMessage:\n{turn}", maxTokens: 200)
                .ConfigureAwait(false);

            var facts = reply is null
                ? []
                : reply.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(l => !l.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                    .Where(l => l.Length > 2)
                    .ToList();

            if (facts.Count == 0) Empty++;
            if (budget > 0 && facts.Count > budget) OverBudget++;
            Facts += facts.Count;
            _cache[turn] = facts;
            return facts;
        }
    }

    /// <summary>Extraction done to a GOOD STANDARD: one call per SESSION, with the model citing the turn
    /// each fact came from — the field baseline the turn-by-turn extractor was a weak proxy for.
    ///
    /// <para><b>Why per session rather than per turn.</b> A competent write-time consolidator reads a
    /// conversation, not a turn in isolation: only with the session in view can it tell a durable fact from
    /// a passing remark, or merge two turns into one fact. The turn-by-turn version could do neither, which
    /// is part of why it inflated the corpus 7.1× before it was budgeted.</para>
    ///
    /// <para><b>The citation is what keeps it SCORABLE, and it is also the field's own named mitigation</b>
    /// — the survey calls it "reflection grounding: requiring the agent to cite specific episodic evidence".
    /// The model tags each fact with its turn, the tag becomes the same <c>(sNtM)</c> marker the model-free
    /// metric matches on, and a fact citing a turn that does not exist is DROPPED rather than trusted.</para>
    ///
    /// <para><b>Read <see cref="Miscited"/> before any score.</b> A model that cites badly moves evidence
    /// onto the wrong turn, which the survival control cannot see — it would show a fact surviving while the
    /// metric scores it against the wrong marker.</para></summary>
    /// <param name="chat">The extracting model — a strong one, or this arm measures the wrong thing.</param>
    private sealed class SessionFactExtractor(SweepDoubles.IBenchChat chat)
    {
        private const string Instruction = """
            You are building a long-term memory from one session of a conversation.

            Write the durable facts it states. Reply with one fact per line, each line starting with the
            turn number it came from in square brackets, like:
            [3] the user's cat is named Mila
            [7] the user moved to Berlin in May 2026

            Rules:
            - Cite the turn the fact actually came from. Never invent a turn number.
            - State only what the session says. Never infer or add.
            - Keep names, numbers and dates exactly as written.
            - Merge repetitions of the same fact into one line, citing the turn that states it best.
            - Skip greetings, filler, questions and opinions about the conversation itself.
            - If the session states no durable fact, reply with exactly: NONE
            - No commentary, no numbering other than the turn citation.
            """;

        internal int Calls { get; private set; }

        internal int Facts { get; private set; }

        /// <summary>Facts whose cited turn is not in the session — dropped, and counted because a high rate
        /// means the citations are noise and every downstream number is scored against the wrong turn.
        /// </summary>
        internal int Miscited { get; private set; }

        internal int Empty { get; private set; }

        /// <summary>One session's facts, already tagged with the marker the metric matches on.</summary>
        internal async Task<IReadOnlyList<string>> ExtractAsync(IReadOnlyList<Turn> session)
        {
            if (session.Count == 0) return [];

            var transcript = string.Join("\n", session.Select((t, i) => $"[{i}] {t.Text}"));
            Calls++;
            var reply = await chat.AskAsync($"{Instruction}\n\nSession:\n{transcript}", maxTokens: 1200)
                .ConfigureAwait(false);
            if (reply is null) { Empty++; return []; }

            var facts = new List<string>();
            foreach (var line in reply.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (line.Equals("NONE", StringComparison.OrdinalIgnoreCase)) continue;
                var close = line.IndexOf(']');
                if (!line.StartsWith('[') || close < 2
                    || !int.TryParse(line[1..close], out var turn)) { Miscited++; continue; }
                if (turn < 0 || turn >= session.Count) { Miscited++; continue; }

                var body = line[(close + 1)..].Trim();
                if (body.Length <= 2) continue;
                facts.Add($"{session[turn].Tag} {body}");
            }

            if (facts.Count == 0) Empty++;
            Facts += facts.Count;
            return facts;
        }
    }

    /// <summary>The RECONCILING half of write-time consolidation: a new fact that supersedes a stored one
    /// REPLACES it, rather than landing beside it.
    ///
    /// <para><b>Why it is the half worth measuring.</b> Extraction alone inflated the corpus 7.1× with
    /// near-duplicates and cost 14 points of <c>current@k</c> (`docs/memory-measurements.md` §5). That is the failure
    /// mode this removes — and it is also the mechanism the field's write-time designs actually claim, since
    /// extracting a fact resolves nothing on its own.</para>
    ///
    /// <para><b>It asks the model one question per candidate pair, not per fact</b>: only facts the store
    /// already holds something similar to can supersede anything, so similarity gates the model call and the
    /// cost stays near extraction's. A superseded entry is DELETED through the store rather than left to
    /// decay, which is the point — write-time consolidation is a claim that read-time burial is
    /// unnecessary.</para></summary>
    /// <param name="chat">The deciding model.</param>
    /// <param name="engine">The engine whose store is being consolidated.</param>
    /// <param name="store">The store, for the delete a supersession performs.</param>
    /// <param name="vector backend">Gates the model call — see <see cref="Similar"/>.</param>
    /// <param name="decisions">Verdicts shared across arms, keyed on the fact PAIR — see
    /// <see cref="WriteAsync"/>.</param>
    private sealed class Reconciler(
        SweepDoubles.IBenchChat chat, GraphMemoryEngine engine, SqliteMemoryGraphStore store,
        SweepDoubles.CachingVectorProvider vectorProvider, Dictionary<string, bool> decisions)
    {
        /// <summary>How alike a stored fact must be before the model is asked whether it was superseded.
        /// <para>Cost control with a rationale rather than a cap: a supersession is a CHANGED VALUE for the
        /// same thing, so the pair is near-duplicate by construction and a distant pair cannot be one. It
        /// also bounds the run — asking about every candidate would be one model call per stored fact per
        /// write, which is hours at this corpus size.</para></summary>
        private const double Similar = 0.80;

        /// <summary>How many neighbours the count gate looks at. It bounds <see cref="Density"/> the way
        /// <c>SalienceOptions.SimilarityK</c> bounds the engine's own <c>SimilarCount</c>, and
        /// <c>memory-density</c>'s own caveat applies here too: a saturating count measures the WINDOW, so
        /// this must exceed the recurrence threshold or the gate cannot see one.</summary>
        private const int DensityK = 10;

        /// <summary>The count at or above which a write is treated as a RECURRENCE rather than a correction,
        /// so no supersession is considered.
        ///
        /// <para><b>Why a count and not the top-1 similarity the original gate used.</b> A correction and a
        /// recurrence are BOTH near-duplicates of something stored — that is what makes pairwise similarity
        /// unable to separate them, measured in <c>memory-density</c>: `correction` scores a mean
        /// <c>SimilarCount</c> of 1.00 and `recurrence` 6.00, pooled AUC 1.000. A top-1 gate therefore fires
        /// on both, and every recurrence it passes to the model is a chance to delete a CONFIRMATION of a
        /// still-true fact.</para>
        ///
        /// <para><b>Read the constant as a direction, not a fitted value.</b> That measurement is authored
        /// fixtures at an effective n of 1, and its best threshold was the search window rather than a
        /// learned boundary.</para></summary>
        internal const int RecurrenceAt = 3;

        private const string Instruction = """
            You decide whether a NEW fact makes an OLD one obsolete.

            Reply with exactly one word:
            REPLACES  - the new fact states a changed value for the same thing, so the old one is now wrong.
            KEEP      - both can be true at once, or they are about different things.

            Be strict. Two facts about different subjects are always KEEP. A fact that merely repeats the
            old one is KEEP. Only a genuine CHANGE to the same attribute is REPLACES.
            """;

        internal int Asked { get; private set; }

        /// <summary>Pairs answered from the shared cache rather than by asking again. Reported so the ask
        /// count is readable as a COST rather than mistaken for how often reconciliation engaged.</summary>
        internal int Reused { get; private set; }

        internal int Replaced { get; private set; }

        internal int Considered { get; private set; }

        /// <summary>Writes the top-1 gate admitted whose neighbourhood is DENSE — the recurrences a
        /// similarity gate cannot tell from corrections. This is the diagnostic: it is what the original gate
        /// was passing to the model, and every one is a chance to delete a confirmation.</summary>
        internal int Dense { get; private set; }

        /// <summary>Supersessions applied by REINFORCING the replacement instead of deleting the superseded
        /// entry. Counted separately because it is a different act, not a tuned version of the same one.
        /// </summary>
        internal int Boosted { get; private set; }

        /// <summary>Write one fact, superseding whatever it makes obsolete.</summary>
        /// <param name="countGate">Skip a write whose neighbourhood is dense — a RECURRENCE — instead of
        /// asking the model about it.</param>
        /// <param name="boost">Reinforce the replacement rather than deleting what it supersedes. The
        /// retrievability contract forbids SHORTENING a memory (`Stability` may never decrease), so raising
        /// the winner is the only way to widen the gap without deleting — and deletion is what
        /// <c>docs/DECISIONS.md</c> <b>D41</b> exists to refuse.</param>
        internal async Task WriteAsync(string fact, bool countGate = false, bool boost = false)
        {
            var near = (await engine.RecallAsync(new MemoryQuery(Task, Scope, fact, Limit: DensityK))
                .ConfigureAwait(false)).Items;
            var candidate = near.FirstOrDefault();

            // The marker is stripped before comparing: every fact carries one, and leaving it in makes two
            // facts from the SAME turn look alike for a reason that has nothing to do with their content.
            if (candidate is not null)
            {
                Considered++;
                var old = candidate.Content ?? candidate.Headline;
                if (!string.Equals(old, fact, StringComparison.Ordinal)
                    && await CosineOf(Strip(old), Strip(fact)).ConfigureAwait(false) >= Similar)
                {
                    // THE DIAGNOSTIC, counted whether or not the gate acts on it: how many of the pairs the
                    // top-1 gate admits are actually recurrences. Reported even in the arms that ignore it,
                    // because "the old gate was firing on recurrences" is the claim being tested and it must
                    // be readable from the arm that does NOT change behaviour.
                    var density = await DensityOf(fact, near).ConfigureAwait(false);
                    if (density >= RecurrenceAt)
                    {
                        Dense++;
                        if (countGate)
                        {
                            await engine.RememberAsync(new MemoryWrite(Task, Scope, fact))
                                .ConfigureAwait(false);
                            return;
                        }
                    }

                    // The verdict is CACHED across arms, keyed on the pair. Whether one fact supersedes
                    // another is a property of the two facts, so the two reconcile arms asking it separately
                    // buys nothing and doubles a per-call cost that is a whole process on the CLI. It also
                    // removes model nondeterminism as a difference BETWEEN the arms, which they should not
                    // have: they differ in forgetting's vote, not in what the writer believed.
                    var key = $"{Strip(old)}␟{Strip(fact)}";
                    if (!decisions.TryGetValue(key, out var replaces))
                    {
                        Asked++;
                        var verdict = await chat.AskAsync(
                            $"{Instruction}\n\nOLD: {Strip(old)}\n\nNEW: {Strip(fact)}", maxTokens: 4)
                            .ConfigureAwait(false);
                        replaces = verdict?.Contains("REPLACES", StringComparison.OrdinalIgnoreCase) == true;
                        decisions[key] = replaces;
                    }
                    else
                    {
                        Reused++;
                    }

                    if (replaces && long.TryParse(candidate.Reference.Id, out var id))
                    {
                        if (boost)
                        {
                            // BURIAL, NOT DELETION. The superseded entry is left alone and the replacement is
                            // written and then recalled, which is what reinforces it — so decay buries the
                            // loser by the winner climbing past it, and D41's recoverability is untouched.
                            await engine.RememberAsync(new MemoryWrite(Task, Scope, fact))
                                .ConfigureAwait(false);
                            await engine.RecallAsync(new MemoryQuery(Task, Scope, fact, Limit: 1))
                                .ConfigureAwait(false);
                            Boosted++;
                            return;
                        }

                        await store.DeleteAsync(engine.Name, [id]).ConfigureAwait(false);
                        Replaced++;
                    }
                }
            }

            await engine.RememberAsync(new MemoryWrite(Task, Scope, fact)).ConfigureAwait(false);
        }

        /// <summary>How many stored entries actually RESEMBLE this write — the engine's own
        /// <c>SalienceContext.SimilarCount</c> computed bench-side, over the page already recalled so the
        /// gate costs no extra round trip.</summary>
        private async Task<int> DensityOf(string fact, IReadOnlyList<MemoryItem> near)
        {
            var count = 0;
            foreach (var item in near)
            {
                var text = item.Content ?? item.Headline;
                if (string.Equals(text, fact, StringComparison.Ordinal)) continue;
                if (await CosineOf(Strip(text), Strip(fact)).ConfigureAwait(false) >= Similar) count++;
            }

            return count;
        }

        private async Task<double> CosineOf(string a, string b) =>
            Cosine(await vectorProvider.EmbedAsync(a).ConfigureAwait(false),
                await vectorProvider.EmbedAsync(b).ConfigureAwait(false));

        /// <summary>Drops a leading <c>(sNtM)</c> marker so similarity is about the FACT.</summary>
        private static string Strip(string text)
        {
            var close = text.IndexOf(')');
            return close > 0 && text.StartsWith('(') ? text[(close + 1)..].Trim() : text;
        }
    }

    /// <summary>The deep page a recovery probe asks for, well past any caller's limit. It is not a second
    /// opinion on ranking — it is how "the ranking put it below the cut" is told from "the entry is gone",
    /// which is the only distinction <c>docs/DECISIONS.md</c> D41 makes.</summary>
    private const int DeepLimit = 100;

    /// <summary>Write-time extraction against read-time decay — the two ways to resolve a superseded fact.
    ///
    /// <para><b>The question it answers</b> is whether consolidating at WRITE time can stand in for decay:
    /// <c>extract+forget0</c> stores extracted facts with forgetting silent, so if it reaches the decay
    /// arm then extraction replaces the mechanism rather than complementing it. <c>extract</c> keeps both,
    /// which says whether they compose.</para>
    ///
    /// <para><b>Extraction is only half of Mem0's mechanism</b> and this is deliberately the cheaper half:
    /// facts are extracted, never RECONCILED, so a superseded fact and its replacement both land as
    /// entries. The store's <c>DeleteAsync</c> makes the ADD/UPDATE/DELETE half reachable next, and this run
    /// decides whether it is worth building.</para>
    ///
    /// <para><b><c>--facts N</c> bounds the extractor</b>, which is the direct test of whether the measured
    /// inflation was the design or the PROMPT. Off by default, so the unbounded run reproduces.</para>
    ///
    /// <para><b>The prediction, registered before the run.</b> If dilution is the mechanism, bounding lifts
    /// <c>extract</c> toward <c>lyntai</c> and raises <c>current@k</c>; if it is the store's shape, it does
    /// not. Evidence survival is the branch that decides which: below 100% the budget DELETED evidence, and
    /// any gain was bought rather than earned. <c>extract+forget0</c> is predicted to stay indistinguishable
    /// from cosine either way, since a budget shapes the store and not forgetting's vote.</para></summary>
    private static async Task<int> RunExtractionAsync(IReadOnlyList<Question> sampled,
        SweepDoubles.CachingVectorProvider vectorProvider, SweepDoubles.IBenchChat chat, string[] args)
    {
        var budget = ArgValue(args, "--facts") is { } b && int.TryParse(b, out var cap) ? cap : 0;
        var extractor = new FactExtractor(chat, budget);
        // `--sessions` is the GOOD-STANDARD extractor: one call per session with the model citing its turns,
        // rather than one call per turn in isolation. Off by default, so the turn-by-turn runs reproduce.
        var bySession = args.Contains("--sessions") ? new SessionFactExtractor(chat) : null;
        FieldArm[] engines = [FieldArms.Shipped(), FieldArms.Named("+forget0")];
        var reconcile = args.Contains("--reconcile");
        // `extract+reconcile-fixed` changes the reconciler on BOTH axes the diagnosis names — it gates on
        // NEIGHBOURHOOD DENSITY instead of top-1 similarity, and it reinforces the replacement instead of
        // deleting what it supersedes. The two travel together because each is useless alone: a better gate
        // still deletes (which D41 refuses), and a non-destructive act applied to recurrences still promotes
        // the wrong facts. `extract+reconcile` stays as the control and reports the same diagnostic.
        string[] arms = reconcile
            ? ["lyntai", "extract", "extract+reconcile", "extract+reconcile-fixed",
                "extract+reconcile+forget0", VectorArm]
            : ["lyntai", "extract", "extract+forget0", VectorArm];
        int asked = 0, reused = 0, replaced = 0;

        // `dense` counts pairs the TOP-1 gate admitted whose neighbourhood is dense - recurrences it cannot
        // tell from corrections. Accumulated for BOTH arms: the control reports what the shipped gate was
        // passing to the model, which is the claim under test, and the fixed arm reports what it skipped.
        int dense = 0, denseControl = 0, boosted = 0;
        // Shared across arms and across questions: a supersession verdict is about the fact PAIR.
        var decisions = new Dictionary<string, bool>(StringComparer.Ordinal);

        var currentHit = new Dictionary<string, int>(StringComparer.Ordinal);
        var staleHit = new Dictionary<string, int>(StringComparer.Ordinal);
        var prefers = new Dictionary<string, int>(StringComparer.Ordinal);
        var decidable = new Dictionary<string, int>(StringComparer.Ordinal);
        var perQuestion = new Dictionary<string, Dictionary<int, bool>>(StringComparer.Ordinal);
        int survived = 0, evidenceTurns = 0;

        var done = 0;
        foreach (var q in sampled)
        {
            Progress(++done, sampled.Count);

            // Extract ONCE per question and reuse across arms — the model is the cost here, and two arms
            // differing only in a ranking weight must see byte-identical stores or the comparison is not
            // about the weight.
            var extracted = new List<string>();
            if (bySession is not null)
                foreach (var session in q.Turns.GroupBy(t => t.Session).OrderBy(g => g.Key))
                    extracted.AddRange(await bySession.ExtractAsync([.. session]).ConfigureAwait(false));
            else
                foreach (var t in q.Turns)
                    foreach (var fact in await extractor.ExtractAsync(t.Text).ConfigureAwait(false))
                        extracted.Add($"{t.Tag} {fact}");

            // THE CONTROL EXTRACTION NEEDS: did the evidence survive being rewritten? A fact dropped here
            // is unreachable by any ranking, so without this column a retrieval score would blame the
            // ranker for what the extractor deleted.
            foreach (var t in q.Current.Concat(q.Stale))
            {
                evidenceTurns++;
                if (extracted.Any(e => e.StartsWith(t.Tag, StringComparison.Ordinal))) survived++;
            }

            var results = new List<(string Arm, List<string> Got)>();
            foreach (var arm in arms.Where(a => a != VectorArm))
            {
                var config = arm.EndsWith("forget0", StringComparison.Ordinal) ? engines[1] : engines[0];
                var texts = arm == "lyntai" ? [.. q.Turns.Select(t => $"{t.Tag} {t.Text}")] : extracted;

                using var db = new MemoryPolicySweep.SweepDb();
                var engine = EngineFor(config, db, vectorProvider);

                if (arm.Contains("reconcile", StringComparison.Ordinal))
                {
                    // The RECONCILING pass writes through the consolidator, so a superseded fact is deleted
                    // as the replacement arrives rather than left for decay to bury.
                    var fixedGate = arm.Contains("-fixed", StringComparison.Ordinal);
                    var consolidator = new Reconciler(
                        chat, engine, new SqliteMemoryGraphStore(db.Factory), vectorProvider, decisions);
                    foreach (var text in texts)
                        await consolidator.WriteAsync(text, countGate: fixedGate, boost: fixedGate);
                    asked += consolidator.Asked;
                    reused += consolidator.Reused;
                    replaced += consolidator.Replaced;
                    if (fixedGate) { dense += consolidator.Dense; boosted += consolidator.Boosted; }
                    else denseControl += consolidator.Dense;
                }
                else
                {
                    foreach (var text in texts)
                        await engine.RememberAsync(new MemoryWrite(Task, Scope, text));
                }

                results.Add((arm,
                    (await engine.RecallAsync(new MemoryQuery(Task, Scope, q.Text, Limit: RecallLimit)))
                        .Items.Select(i => i.Content ?? i.Headline).ToList()));
            }

            var index = new List<(string Text, float[] Vector)>();
            foreach (var t in q.Turns)
            {
                var content = $"{t.Tag} {t.Text}";
                index.Add((content, await vectorProvider.EmbedAsync(content)));
            }
            results.Add((VectorArm, (await TopKAsync(vectorProvider, index, q.Text, RecallLimit)).ToList()));

            foreach (var (arm, got) in results)
            {
                var cur = FirstIndexOf(got, q.Current);
                var sta = FirstIndexOf(got, q.Stale);
                if (cur >= 0) currentHit[arm] = currentHit.GetValueOrDefault(arm) + 1;
                if (sta >= 0) staleHit[arm] = staleHit.GetValueOrDefault(arm) + 1;
                if (cur < 0 && sta < 0) continue;

                decidable[arm] = decidable.GetValueOrDefault(arm) + 1;
                var preferred = cur >= 0 && (sta < 0 || cur < sta);
                if (preferred) prefers[arm] = prefers.GetValueOrDefault(arm) + 1;
                if (!perQuestion.TryGetValue(arm, out var byQuestion)) perQuestion[arm] = byQuestion = [];
                byQuestion[done] = preferred;
            }
        }

        Console.WriteLine();
        var calls = bySession?.Calls ?? extractor.Calls;
        var factCount = bySession?.Facts ?? extractor.Facts;
        var emptied = bySession?.Empty ?? extractor.Empty;
        var unit = bySession is null ? "turn" : "session";

        Console.WriteLine($"Extraction: {calls} {unit}(s) read, {factCount} fact(s) written, "
            + $"{emptied} {unit}(s) yielded NOTHING");
        Console.WriteLine($"  INFLATION: {(calls == 0 ? 0 : (double)factCount / calls):F1}"
            + $" fact(s) per {unit} - the corpus this arm ranks against, relative to `lyntai`'s raw turns.");
        if (bySession is not null)
        {
            // A miscited fact is scored against the WRONG turn, which evidence survival cannot see: the fact
            // exists, so survival counts it, while the metric matches it on a marker it never came from.
            Console.WriteLine($"  CITATIONS: {bySession.Miscited} fact(s) cited a turn outside their session "
                + "and were DROPPED. A high count means the markers are noise and every arm below is scored");
            Console.WriteLine("  against the wrong turns - read this before the table, not after it.");
        }
        if (budget > 0)
        {
            // The budget is a PROMPT, so an arm that scores its base has two readings a score cannot
            // separate: the bound did not help, or the model ignored it. This is which.
            Console.WriteLine($"  BUDGET: asked for at most {budget}; {extractor.OverBudget}/{extractor.Calls} "
                + $"turn(s) came back OVER ({(extractor.Calls == 0 ? 0 : (double)extractor.OverBudget / extractor.Calls):P1}).");
            Console.WriteLine("  A high rate here means the arm measures a budget the model DECLINED, not a");
            Console.WriteLine("  bounded corpus - read the inflation line above before reading any score below.");
        }

        Console.WriteLine($"  EVIDENCE SURVIVAL: {survived}/{evidenceTurns} "
            + $"({(evidenceTurns == 0 ? 0 : (double)survived / evidenceTurns):P1}) flagged turns still have a "
            + "fact after extraction.");
        Console.WriteLine("  Anything below 100% is information the EXTRACTOR dropped, which no ranking can");
        Console.WriteLine("  recover - so read the arms below against this number, not against each other alone.");
        if (reconcile)
        {
            Console.WriteLine($"  RECONCILIATION: asked {asked} time(s), reused {reused} cached verdict(s), "
                + $"replaced {replaced}. Zero replacements");
            Console.WriteLine("  means the arm IS `extract` whatever it scores - the control that arm needs.");
            Console.WriteLine();
            Console.WriteLine($"  THE DIAGNOSIS: of the pairs the TOP-1 gate admitted, {denseControl} sat in a"
                + $" DENSE neighbourhood");
            Console.WriteLine($"  (>= {Reconciler.RecurrenceAt} stored entries above the same similarity) - recurrences a"
                + " pairwise gate cannot");
            Console.WriteLine("  tell from corrections (`memory-density`). A high count is the claim that the");
            Console.WriteLine("  shipped gate was deleting CONFIRMATIONS of still-true facts; a low one refutes");
            Console.WriteLine($"  it and the fixed arm's score means something else. The fixed arm skipped"
                + $" {dense} and");
            Console.WriteLine($"  boosted {boosted} replacement(s) instead of deleting them.");
            if (denseControl == 0)
                Console.WriteLine("  ! DIAGNOSIS REFUTED: the old gate fired on no recurrence, so density is not"
                    + " what it got wrong.");
        }

        Console.WriteLine();

        Console.WriteLine($"{"Arm",-18} {"prefers current",18} {"current@k",12} {"stale@k",12} {"decidable",11}");
        Console.WriteLine(new string('-', 74));
        foreach (var arm in arms)
        {
            var d = decidable.GetValueOrDefault(arm);
            var p = prefers.GetValueOrDefault(arm);
            var (low, high) = BenchStats.Wilson(p, d);
            Console.WriteLine($"{arm,-18} {(d == 0 ? "-" : $"{(double)p / d:P1} ({p}/{d})"),18} "
                + $"{$"{(double)currentHit.GetValueOrDefault(arm) / sampled.Count:P1}",12} "
                + $"{$"{(double)staleHit.GetValueOrDefault(arm) / sampled.Count:P1}",12} {d,11}"
                + (d == 0 ? "" : $"   95% CI [{low:P1}, {high:P1}]"));
        }

        PrintPairedComparison(arms, perQuestion);
        return 0;
    }

    /// <summary>Does suppression DELETE? The measurement <c>docs/DECISIONS.md</c> D41 has never had.
    ///
    /// <para>Scored only over the questions where the arm actually suppressed the superseded fact, because
    /// those are the only ones where the question arises — an entry the recall returned was never buried,
    /// and counting it as "recovered" would inflate every row toward 100% by construction.</para>
    ///
    /// <para><b>The focused query is the entry's OWN WORDS with its marker stripped</b>, so recovery cannot
    /// come from matching the harness's <c>(sNtM)</c> tag, which is a token no real caller would type. It is
    /// deliberately the easiest honest query — this is a floor test for reachability, not a difficulty
    /// test.</para></summary>
    private static async Task<int> RunRecoveryAsync(IReadOnlyList<Question> sampled,
        SweepDoubles.CachingVectorProvider vectorProvider, string[] args)
    {
        var configs = SelectConfigs(args, out _);
        if (configs is null) return 1;

        var buried = new Dictionary<string, int>(StringComparer.Ordinal);
        var byQuery = new Dictionary<string, int>(StringComparer.Ordinal);
        var byWalk = new Dictionary<string, int>(StringComparer.Ordinal);
        var byEither = new Dictionary<string, int>(StringComparer.Ordinal);
        var atTop = new Dictionary<string, int>(StringComparer.Ordinal);
        var byDeep = new Dictionary<string, int>(StringComparer.Ordinal);
        var deepRankSum = new Dictionary<string, int>(StringComparer.Ordinal);

        var done = 0;
        foreach (var q in sampled)
        {
            Progress(++done, sampled.Count);
            if (q.Stale.Count == 0) continue;

            foreach (var arm in configs)
            {
                using var db = new MemoryPolicySweep.SweepDb();
                var engine = EngineFor(arm, db, vectorProvider);
                foreach (var t in q.Turns)
                    await engine.RememberAsync(new MemoryWrite(Task, Scope, $"{t.Tag} {t.Text}"));

                var page = (await engine.RecallAsync(new MemoryQuery(Task, Scope, q.Text, Limit: RecallLimit)))
                    .Items.Select(i => i.Content ?? i.Headline).ToList();
                if (FirstIndexOf(page, q.Stale) >= 0) continue;   // never buried; nothing to recover

                buried[arm.Name] = buried.GetValueOrDefault(arm.Name) + 1;

                // (a) A MORE FOCUSED QUERY — the buried entry's own content, marker removed.
                var stale = q.Stale[0];
                var focused = (await engine.RecallAsync(
                        new MemoryQuery(Task, Scope, stale.Content, Limit: RecallLimit)))
                    .Items.Select(i => i.Content ?? i.Headline).ToList();
                var rank = FirstIndexOf(focused, q.Stale);
                if (rank >= 0)
                {
                    byQuery[arm.Name] = byQuery.GetValueOrDefault(arm.Name) + 1;
                    if (rank == 0) atTop[arm.Name] = atTop.GetValueOrDefault(arm.Name) + 1;
                }

                // (a2) THE SAME QUERY, A DEEPER PAGE — and this is the one that separates the two things
                // D41 is about. Missing from a page of ten says the RANKING put it there; missing from a
                // page of a hundred says the entry is gone. Without this column the table cannot tell
                // "buried" from "deleted", which is the only distinction it exists to make.
                var deep = (await engine.RecallAsync(
                        new MemoryQuery(Task, Scope, stale.Content, Limit: DeepLimit)))
                    .Items.Select(i => i.Content ?? i.Headline).ToList();
                var deepRank = FirstIndexOf(deep, q.Stale);
                if (deepRank >= 0)
                {
                    byDeep[arm.Name] = byDeep.GetValueOrDefault(arm.Name) + 1;
                    deepRankSum[arm.Name] = deepRankSum.GetValueOrDefault(arm.Name) + deepRank + 1;
                }

                // (b) A RELATED CUE — the walk, reaching it as a neighbour of what the question did return.
                var walked = false;
                await WalkAsync(engine, q.Text, (_, body, _) =>
                    walked |= q.Stale.Any(t => body.Any(b => b.Contains(t.Tag, StringComparison.Ordinal))));
                if (walked) byWalk[arm.Name] = byWalk.GetValueOrDefault(arm.Name) + 1;

                if (rank >= 0 || walked) byEither[arm.Name] = byEither.GetValueOrDefault(arm.Name) + 1;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{"Arm",-16} {"buried",7} {$"page@{RecallLimit}",9} {"walk",7} "
            + $"{$"deep@{DeepLimit}",26} {"mean rank",10}");
        Console.WriteLine(new string('-', 82));
        foreach (var arm in configs.Select(c => c.Name))
        {
            var b = buried.GetValueOrDefault(arm);
            if (b == 0) { Console.WriteLine($"{arm,-16} {0,7}   (nothing was suppressed)"); continue; }

            var d = byDeep.GetValueOrDefault(arm);
            var (low, high) = BenchStats.Wilson(d, b);
            Console.WriteLine($"{arm,-16} {b,7} {$"{(double)byQuery.GetValueOrDefault(arm) / b:P1}",9} "
                + $"{$"{(double)byWalk.GetValueOrDefault(arm) / b:P1}",7} "
                + $"{$"{(double)d / b:P1} [{low:P0}, {high:P0}]",26} "
                + $"{(d == 0 ? "-" : $"{(double)deepRankSum.GetValueOrDefault(arm) / d:F1}"),10}");
        }

        Console.WriteLine();
        Console.WriteLine("  'buried' is the denominator and it is the point: only a suppressed entry can");
        Console.WriteLine("  test the invariant, so an arm that suppresses nothing reports nothing here.");
        Console.WriteLine();
        Console.WriteLine($"  THE COLUMN THAT DECIDES D41 IS `deep@{DeepLimit}`, not `page@{RecallLimit}`.");
        Console.WriteLine("  Absent from a ten-slot page means the RANKING put it there - which is what decay");
        Console.WriteLine("  is FOR. Absent from a hundred means the entry is unreachable, which is deletion");
        Console.WriteLine("  and which `IMemoryGraphStore.SeedAsync`'s 'faintness never excludes' forbids.");
        Console.WriteLine("  'mean rank' is where the focused query actually put it, so 'reachable' does not");
        Console.WriteLine("  quietly mean 'at position 99'.");
        return 0;
    }

    /// <summary>Each arm against the REFERENCE arm, paired question by question.
    ///
    /// <para><b>The reference is <c>vector</c> when it ran</b> — plain cosine is the "no mechanism at all"
    /// baseline, so a comparison against it is the one that answers whether this design does anything —
    /// otherwise the first arm, which makes an ablation ladder read against its own control.</para>
    ///
    /// <para><b>Only questions BOTH arms could decide are paired.</b> An arm that returned neither fact has
    /// no preference to compare, and counting it as a failure would score a retrieval miss as a preference
    /// error — the same conflation the <c>decidable</c> column exists to prevent.</para></summary>
    private static void PrintPairedComparison(
        IReadOnlyList<string> arms, Dictionary<string, Dictionary<int, bool>> perQuestion)
    {
        var reference = arms.Contains(VectorArm, StringComparer.Ordinal) ? VectorArm : arms.FirstOrDefault();
        if (reference is null || !perQuestion.TryGetValue(reference, out var baseline)) return;

        var others = arms.Where(a => !string.Equals(a, reference, StringComparison.Ordinal)).ToList();
        if (others.Count == 0) return;

        Console.WriteLine();
        Console.WriteLine($"Paired against `{reference}`, McNemar exact over the questions both decided:");
        foreach (var arm in others)
        {
            if (!perQuestion.TryGetValue(arm, out var mine)) continue;

            int wins = 0, losses = 0, both = 0;
            foreach (var (question, preferred) in mine)
            {
                if (!baseline.TryGetValue(question, out var theirs)) continue;
                both++;
                if (preferred && !theirs) wins++;
                else if (!preferred && theirs) losses++;
            }

            var p = BenchStats.McNemarExact(wins, losses);
            Console.WriteLine($"  {arm,-24} {both,3} paired   +{wins,-3} -{losses,-3} "
                + $"net {wins - losses,+4}   {BenchStats.Format(p)}");
        }

        Console.WriteLine();
        Console.WriteLine("  '+' is questions this arm preferred the current fact on and the reference did");
        Console.WriteLine("  not; '-' the reverse. Only those DISAGREEMENTS carry information, so a small");
        Console.WriteLine("  net over few discordant pairs is not a result however large the percentage gap.");
    }

    /// <summary>One arm's engine over its own store, with the semantic channel registered when the arm asks
    /// for one. <b>Every arm gets its own store and its own vector store</b> — a recall reinforces what it
    /// returns, so arms sharing one would each mutate the decay state the next reads.</summary>
    private static GraphMemoryEngine EngineFor(FieldArm arm, MemoryPolicySweep.SweepDb db,
        SweepDoubles.CachingVectorProvider vectorProvider)
    {
        var vectors = new InMemoryVectorStore();
        IMemorySeedSource[]? seeds = arm.SemanticK is { } k
            ? [new LexicalSeedSource(), new SubjectSeedSource(),
                new SemanticSeedSource([vectorProvider], vectors, new SemanticSeedOptions { K = k })]
            : null;

        return new GraphMemoryEngine(Task, new SqliteMemoryGraphStore(db.Factory), options: arm.Options, seams: new GraphMemorySeams
            {
                Providers = [vectorProvider],
                Vectors = vectors,
                Ranking = arm.Ranking,
                Verification = arm.Verification,
                SeedSources = seeds,
            });
    }

    private sealed record Turn(int Session, int Index, string Role, string Content, bool HasAnswer)
    {
        /// <summary>The marker rides in the CONTENT so a hit is checkable with no model, exactly as the
        /// LoCoMo harness carries a dialogue id. Every arm reads the same text, so none is advantaged.</summary>
        public string Tag => $"(s{Session}t{Index})";

        public string Text => $"[{Role}] {Content}";
    }

    private sealed record Question(string Id, string Text, string Type, IReadOnlyList<Turn> Turns,
        IReadOnlyList<Turn> Current, IReadOnlyList<Turn> Stale, IReadOnlyList<Turn> Evidence);

    public static async Task<int> RunAsync(string[] args)
    {
        var haystack = args.Contains("--haystack");
        var (path, file, remote, variable) = haystack
            ? (Environment.GetEnvironmentVariable(HaystackVariable)
                ?? Path.Combine("devtools", "_bench-data", "lme_s.json"),
                "lme_s.json", "longmemeval_s_cleaned.json", HaystackVariable)
            : (Environment.GetEnvironmentVariable(DataVariable)
                ?? Path.Combine("devtools", "_bench-data", "lme_oracle.json"),
                "lme_oracle.json", "longmemeval_oracle.json", DataVariable);
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"memory-longmemeval: dataset not found at '{path}'.");
            Console.Error.WriteLine($"  Download it ({(haystack ? "277 MB" : "15 MB")}) and re-run:");
            Console.Error.WriteLine($"    curl -sSL -o devtools/_bench-data/{file} \\");
            Console.Error.WriteLine("      https://huggingface.co/datasets/xiaowu0162/longmemeval-cleaned/"
                + $"resolve/main/{remote}");
            Console.Error.WriteLine($"  Or point {variable} at a copy.");
            return 1;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var vectorProvider = await SweepDoubles.TryRealVectorProviderAsync(http, "memory-longmemeval");
        if (vectorProvider is null) return 1;

        // `--temporal` is the COUNTER-TEST to the knowledge-update class, not more of the same. Temporal
        // reasoning asks things like "what was the FIRST issue after the service", so the answer is often
        // the EARLIER fact and preferring the current one would be wrong. It needs BOTH facts present, which
        // is exactly what the suppression that wins knowledge-update should cost. Different question,
        // different metric: all-evidence recall rather than preference.
        // `--ranks` is a DIAGNOSTIC, not an arm: it asks WHERE in the candidate pool the two facts sit,
        // because the haystack table shows stale@k RISING (54.3 → 62.9) while twenty times more candidates
        // compete for the same ten slots. More competition should crowd the superseded fact out. It needs
        // the knowledge-update pair, so it overrides `--temporal` rather than combining with it.
        // `--multi` is the multi-session class on the SAME metric as temporal, and that sharing is measured
        // rather than assumed (docs/task-archive.md Part 234, 2026-09-04): its evidence spans several
        // sessions in 91% of questions and it carries no current/stale split, so knowledge-update's
        // preference metric is structurally inapplicable and all-evidence recall is what the class asks for.
        var ranks = args.Contains("--ranks");
        var temporal = args.Contains("--temporal") && !ranks;
        var multi = args.Contains("--multi") && !ranks && !temporal;

        // `--class` reaches the THREE SINGLE-SESSION classes, which had no switch at all until 2026-09-11
        // (docs/task-archive.md Part 234). They score on all-evidence recall like temporal and
        // multi-session, and that sharing was MEASURED rather than assumed: they span more than one session
        // 0% of the time, so knowledge-update's preference metric needs a current/stale split they
        // structurally cannot supply.
        //
        // Expect `single-session-assistant` to be flat or unmeasurable even here — 63% of its questions fit
        // entirely inside k = 10 on the ORACLE, so the first recall returns the whole conversation and a
        // shot curve has nothing to expand into. That is why these need `--haystack`, at ~40x the ingestion.
        var singleClasses = new[] { "single-session-user", "single-session-assistant", "single-session-preference" };
        var wantSingle = ArgValue(args, "--class");
        if (wantSingle is not null && !singleClasses.Contains(wantSingle, StringComparer.Ordinal))
        {
            Console.Error.WriteLine($"--class {wantSingle}: not a single-session class. One of "
                + $"{string.Join(", ", singleClasses)}; the other three have their own switches "
                + "(--temporal, --multi, and knowledge-update by default).");
            return 1;
        }
        if (wantSingle is not null && (ranks || temporal || multi))
        {
            Console.Error.WriteLine("--class selects a single-session class and cannot combine with "
                + "--ranks/--temporal/--multi, which each name a class of their own.");
            return 1;
        }

        // Every class scored by all-evidence recall loads the same way: every flagged turn counts and no
        // current/stale split is required. Knowledge-update is the one that needs the pair. Taking this
        // branch is also what makes a new class inherit `Load`'s ZERO-EVIDENCE guard — 6 of
        // single-session-user's questions carry no flagged turn, and on the other branch they would survive
        // loading and score an automatic miss.
        var allEvidence = temporal || multi || wantSingle is not null;
        var wantClass = wantSingle
            ?? (temporal ? "temporal-reasoning" : multi ? "multi-session" : "knowledge-update");
        var questions = Load(path, wantClass, allEvidence);
        if (questions.Count == 0)
        {
            Console.Error.WriteLine($"memory-longmemeval: no {wantClass} question survived loading — a "
                + "knowledge-update needs two dated sessions with flagged answer turns; the all-evidence "
                + "classes need any. A class whose questions all carry ZERO flagged turns loads empty.");
            return 1;
        }

        var seed = ArgValue(args, "--seed") is { } s && int.TryParse(s, out var chosen) ? chosen : DefaultSeed;
        var take = ArgValue(args, "--n") is { } n && int.TryParse(n, out var parsed) ? parsed : questions.Count;
        var sampled = Sample(questions, take, seed);
        var turns = sampled.Sum(q => q.Turns.Count);

        var expandFloor = ArgValue(args, "--expand-floor") is { } f && double.TryParse(f, out var ef) ? ef : 0;

        // `--budget N` caps every arm of a shot table at the same characters. It belongs to `--shots` and
        // nowhere else: the snapshot modes compare CONFIGURATIONS at one recall limit, so a character cap
        // there would price the limit rather than the arm. Rejected rather than ignored, like `--arms` under
        // `--ranks` below - a flag that is accepted and does nothing is the silent no-op every path refuses.
        var shots = args.Contains("--shots");
        ContextBudget[] budgets = [new ContextBudget(0)];
        if (ArgValue(args, "--budget") is { } bs)
        {
            // A LADDER, because the corpus is what a run pays for. Two budget values as two processes
            // re-embed the same 64,911 turns twice (~70 minutes each on 2026-09-06); in one process the
            // second value's ingestions are all vector backend cache hits, so it costs SQLite writes and nothing
            // else. Same reasoning as `--arms` on both field benches.
            var parts = bs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var caps = new List<int>();
            foreach (var part in parts)
            {
                if (!int.TryParse(part, out var bv) || bv <= 0)
                {
                    Console.Error.WriteLine($"--budget: '{part}' is not a positive character count.");
                    return 1;
                }

                caps.Add(bv);
            }

            if (!shots)
            {
                Console.Error.WriteLine("--budget: only applies with --shots, which is the mode whose arms "
                    + "differ in what they SPEND. Add --shots, or drop --budget.");
                return 1;
            }

            budgets = [.. caps.Distinct().Order().Select(b => new ContextBudget(b))];
        }

        // `--fill-k` sizes the arm that SPENDS the budget instead of leaving it on the table. Validated here
        // rather than where it is used, so a typo cannot survive an hour of ingestion.
        var fillK = RecallLimit;
        if (ArgValue(args, "--fill-k") is { } fk)
        {
            if (!int.TryParse(fk, out fillK) || fillK <= 0)
            {
                Console.Error.WriteLine($"--fill-k: '{fk}' is not a positive recall limit.");
                return 1;
            }

            if (budgets.All(b => !b.Binds))
            {
                Console.Error.WriteLine("--fill-k: only applies with --budget, whose cap is what the arm "
                    + "fills. Add --budget, or drop --fill-k.");
                return 1;
            }
        }
        else if (budgets.Any(b => b.Binds))
        {
            fillK = DefaultFillLimit;
        }

        // `--pool M,...` separates a wider POOL from a wider OUTPUT, which `fill` moves together: the engine
        // gathers `Limit x CandidateMultiplier`, so `fill` at k = 80 widens the pool to 320 AND returns 80,
        // while `Limit = 10, CandidateMultiplier = 32` sees the same 320 and returns 10. The shipped
        // multiplier is 4 and has never been measured; including it is the arm's own control, since
        // `pool-4` is shot 1's configuration reached by a different route and must land on it.
        int[] pools = [];
        if (ArgValue(args, "--pool") is { } ps)
        {
            var wanted = new List<int>();
            foreach (var part in ps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(part, out var pv) || pv <= 0)
                {
                    Console.Error.WriteLine($"--pool: '{part}' is not a positive candidate multiplier.");
                    return 1;
                }

                wanted.Add(pv);
            }

            if (budgets.All(b => !b.Binds))
            {
                Console.Error.WriteLine("--pool: only applies with --budget, which is what holds the arms to "
                    + "one context spend. Add --budget, or drop --pool.");
                return 1;
            }

            pools = [.. wanted.Distinct().Order()];
        }

        // `--detail` adds the COMPLETENESS arm: the shipped recall asking for MemoryDetail.Full.
        //
        // It requires `--budget`, and the reason is that without one the arm is not merely uninteresting, it
        // is VACUOUS — provably, not probably. `Detail` is applied in the engine's PROJECTION, after ranking,
        // verification and reinforcement (GraphMemoryEngine.RecallAsync), so it cannot change which nodes come
        // back or in what order; it changes only how much of each one is rendered. This class is scored by
        // `Turn.Tag`, a synthetic `(sNtM)` id sitting at character 0 of every ingested turn, and
        // `MemoryHeadline.Derive` cuts a PREFIX — so the tag survives truncation and every per-item verdict is
        // identical under either detail level. Same items, same verdicts, byte-identical table.
        //
        // A character cap is what makes the lever bite: `ContextBudget.Fit` (and the engine's own
        // `MemoryQuery.CharBudget`, which it reproduces) price each item at `Content?.Length ?? Headline.Length`,
        // so whole items spend the allowance faster and FEWER of them fit. That is the completeness-versus-depth
        // trade, on the one class that scores suppression rather than coverage.
        var detail = args.Contains("--detail");
        if (detail)
        {
            if (budgets.All(b => !b.Binds))
            {
                Console.Error.WriteLine("--detail: only applies with --budget. Detail is a PROJECTION-level "
                    + "choice - it changes how much of each returned item is rendered, never which items are "
                    + "returned - and this class is scored by a turn tag that survives truncation, so with no "
                    + "character cap the arm is identical to `shot-1` by construction. Add --budget, or drop "
                    + "--detail.");
                return 1;
            }

            // Every CLASS is fair game, because every one of them scores by turn tag and cuts its body with
            // the same cap — only the metric differs, and that difference is the point. Knowledge-update
            // scores `clean`, where a cap that trims the tail SUPPRESSES the superseded fact; the
            // all-evidence classes score coverage, where trimming can only lose flagged turns. Running both
            // is what separates a filter from a tax. `--ranks` is the exception and has no body to cap.
            if (ranks)
            {
                Console.Error.WriteLine("--detail: not with --ranks, which scores ONE probe-wrapped engine "
                    + "over a K ladder and never assembles a capped body for an arm to render.");
                return 1;
            }
        }

        if (allEvidence)
        {
            if (temporal)
            {
                Console.WriteLine("=== LongMemEval temporal-reasoning: the COST side of the same bet ===");
                Console.WriteLine();
                Console.WriteLine("Knowledge-update rewards suppressing a superseded fact. This class does not:");
                Console.WriteLine("'what was the FIRST issue after the service' wants the EARLIER fact, and most");
                Console.WriteLine("questions need BOTH. So the metric is all-evidence recall, and the suppression");
                Console.WriteLine("that won the other class is expected to COST here. That is the trade, measured.");
            }
            else if (wantSingle is not null)
            {
                // A banner naming the WRONG class is how a mislabelled table gets published. This branch
                // exists because the single-session classes reach the all-evidence path, which until
                // 2026-09-11 printed multi-session's banner and its statistics for whatever ran on it.
                Console.WriteLine($"=== LongMemEval {wantSingle}: evidence inside ONE session ===");
                Console.WriteLine();
                Console.WriteLine("These classes span more than one session 0% of the time, which is what makes");
                Console.WriteLine("knowledge-update's preference metric structurally inapplicable and leaves");
                Console.WriteLine("all-evidence recall (docs/task-archive.md Part 234). Read a FLAT shot curve");
                Console.WriteLine("here as a property of the class rather than of expansion: on the oracle the");
                Console.WriteLine("store is comparable to or smaller than the page, so the first recall already");
                Console.WriteLine("returns everything and there is nothing for a second shot to reach. That is");
                Console.WriteLine("what --haystack is for.");
            }
            else
            {
                Console.WriteLine("=== LongMemEval multi-session: evidence spread across SEVERAL sessions ===");
                Console.WriteLine();
                Console.WriteLine("2.5 flagged turns per question and 91% of them spanning more than one session,");
                Console.WriteLine("so a question is answered only by gathering evidence the conversation never put");
                Console.WriteLine("side by side. Same metric as temporal - all-evidence recall - because the class");
                Console.WriteLine("has no current/stale split for a preference metric to read");
                Console.WriteLine("(docs/task-archive.md Part 234).");
            }

            Console.WriteLine();
            if (shots)
            {
                Console.WriteLine("--shots asks what EXPANDING buys on that class. If a walk is worth anything");
                Console.WriteLine("on any workload it should be worth it here: the failure mode is a first load");
                Console.WriteLine("holding one flagged turn of two, which is exactly what a second shot is for.");
                Console.WriteLine();
            }
            Preamble(sampled, questions.Count, turns, haystack, seed, wantClass);
            BudgetPreamble(budgets, fillK, pools, detail);
            return shots
                ? await RunTemporalShotsAsync(sampled, vectorProvider, expandFloor, budgets, fillK, pools, args, detail)
                : await RunTemporalAsync(sampled, vectorProvider, args);
        }

        if (args.Contains("--extract"))
        {
            // `--writer cli` reaches a STRONG model through the `claude` CLI. It exists because the first
            // extraction run's verdict was weakened by its own baseline: a 4B model on an unbounded prompt
            // is not the field's design done well, so "write-time consolidation did not substitute for
            // decay" was partly a statement about the extractor. A baseline has to be good before a result
            // measured against it is about anything else.
            SweepDoubles.IBenchChat? writer = ArgValue(args, "--writer") is "cli"
                ? await SweepDoubles.TryCliChatAsync("memory-longmemeval extract")
                : await SweepDoubles.TryRealChatAsync(http, "memory-longmemeval extract");
            if (writer is null) return 1;

            Console.WriteLine("=== WRITE-time extraction against READ-time decay ===");
            Console.WriteLine();
            Console.WriteLine("This engine stores raw turns and resolves a superseded fact at READ time, with");
            Console.WriteLine("decay. The other design in the field consolidates at WRITE time: a model extracts");
            Console.WriteLine("facts as they arrive. This runs both over the same questions, and the arm that");
            Console.WriteLine("decides it is `extract+forget0` - extraction with forgetting SILENT. If that");
            Console.WriteLine("reaches the decay arm, extraction REPLACES the mechanism rather than adding to it.");
            Console.WriteLine();
            Console.WriteLine("Only half of the write-time design: facts are extracted, never RECONCILED, so a");
            Console.WriteLine("superseded fact and its replacement both land. The reconciling half is reachable");
            Console.WriteLine("(the store has DeleteAsync) and this run decides whether it is worth building.");
            Console.WriteLine();
            Preamble(sampled, questions.Count, turns, haystack, seed, "knowledge-update");
            Console.WriteLine($"Extractor: {writer.Model}   (a WRITE-side model, not the reader - nothing here reads)");
            if (ArgValue(args, "--facts") is { } cap)
            {
                Console.WriteLine($"Budget:    at most {cap} fact(s) per turn, stated in the PROMPT and never");
                Console.WriteLine("           truncated in code - so an over-budget count is the model declining,");
                Console.WriteLine("           not the harness enforcing. Unset, the prompt is the unbounded one.");
            }

            Console.WriteLine();
            return await RunExtractionAsync(sampled, vectorProvider, writer, args);
        }

        if (args.Contains("--recover"))
        {
            Console.WriteLine("=== BURIAL, NOT DELETION: is a decayed entry still reachable? ===");
            Console.WriteLine();
            Console.WriteLine("Every other table here scores what decay SUPPRESSES, and none of them can tell");
            Console.WriteLine("'ranked below the cut' from 'gone'. `docs/DECISIONS.md` D41 says the difference is");
            Console.WriteLine("the whole design - an entry is BURIED, never deleted - and that is a claim about");
            Console.WriteLine("the entries a recall did NOT return, which no ranking metric observes.");
            Console.WriteLine();
            Console.WriteLine("So: take the questions where the superseded fact was suppressed, and ask for it");
            Console.WriteLine("two ways - a FOCUSED query in the entry's own words, and a WALK that reaches it as");
            Console.WriteLine("a related neighbour. Recovery near 100% is the invariant holding; anything well");
            Console.WriteLine("below it means decay is deleting, and no gain elsewhere would justify that.");
            Console.WriteLine();
            Preamble(sampled, questions.Count, turns, haystack, seed, "knowledge-update");
            return await RunRecoveryAsync(sampled, vectorProvider, args);
        }

        if (args.Contains("--corroborate"))
        {
            Console.WriteLine("=== Can COUNTING WITNESSES stand in for the judge? (docs/task-archive.md Part 270) ===");
            Console.WriteLine();
            Console.WriteLine("The field's cheap verification gates the WRITE: a claim persists only after two");
            Console.WriteLine("independent sources and three separated re-assertions, no model in the loop. This");
            Console.WriteLine("run asks whether that signal EXISTS here: does the CURRENT fact carry more");
            Console.WriteLine("detectable re-assertions than the SUPERSEDED one? The judge's own paired cells on");
            Console.WriteLine("this corpus fire 4.6:1 BACKWARDS (docs/memory-measurements.md, D109), so the bar");
            Console.WriteLine("a free signal must clear is low - better than backwards, and not flat.");
            Console.WriteLine();
            Preamble(sampled, questions.Count, turns, haystack, seed, "knowledge-update");
            return await RunCorroborationAsync(sampled, vectorProvider);
        }

        if (shots)
        {
            Console.WriteLine("=== What each SHOT buys on the workload this design is FOR ===");
            Console.WriteLine();
            Console.WriteLine("LoCoMo asks for arbitrary old material and a perfect archive wins it by");
            Console.WriteLine("construction. This class asks the opposite: one fact was REVISED, so the context");
            Console.WriteLine("a reader gets should hold the current value and NOT the superseded one. That is");
            Console.WriteLine("what 'clean' scores, and it is the metric a smaller context is supposed to buy.");
            Console.WriteLine();
            Preamble(sampled, questions.Count, turns, haystack, seed, "knowledge-update");
            BudgetPreamble(budgets, fillK, pools, detail);
            return await RunShotsAsync(sampled, vectorProvider, expandFloor, budgets, fillK, pools, args, detail);
        }

        // `--ranks` scores ONE probe-wrapped engine over a K ladder, so it has no arms to select. Rejected
        // rather than ignored: a flag that is accepted and does nothing is the silent no-op every other
        // path here refuses.
        if (ranks && ArgValue(args, "--arms") is not null)
        {
            Console.Error.WriteLine("--arms: not applicable with --ranks, which sweeps K over a single "
                + "configuration rather than comparing arms.");
            return 1;
        }

        if (ranks)
        {
            Console.WriteLine("=== Where the current and stale facts sit in the candidate pool ===");
            Console.WriteLine();
            Console.WriteLine("RRF scores a candidate as the sum over signals of w / (K + rank), K = 60. That");
            Console.WriteLine("curve is CONVEX in rank, so the same rank gap is worth far less deep in the list");
            Console.WriteLine("than near the top. If distractors push both facts down the retrievability order,");
            Console.WriteLine("the signal that separates them quietly stops paying - which is a prediction, and");
            Console.WriteLine("the contribution columns below are what tests it.");
            Console.WriteLine();
            Preamble(sampled, questions.Count, turns, haystack, seed, "knowledge-update");
            await RunRanksAsync(sampled, vectorProvider);
            return 0;
        }

        Console.WriteLine("=== LongMemEval knowledge-update: does the memory PREFER the current fact? ===");
        Console.WriteLine();
        Console.WriteLine("Each question carries an earlier session stating a fact and a later one REVISING");
        Console.WriteLine("it. Both sit in the store and both are textually similar, so retrieving 'the");
        Console.WriteLine("answer' is not the test - preferring the CURRENT one over the superseded one is.");
        Console.WriteLine("That is the claim a decay model makes and a flat index cannot.");
        Console.WriteLine();
        Preamble(sampled, questions.Count, turns, haystack, seed, "knowledge-update");

        var configs = SelectConfigs(args, out var wantsCosine);
        if (configs is null) return 1;

        // `--trigger` measures the RIF trigger's PRECISION (D109) rather than building it: does a judge
        // leave the SUPERSEDED fact unendorsed more often than the current one? One arm, one ingestion.
        TriggerAudit? trigger = null;
        if (args.Contains("--trigger"))
        {
            var judge = await SweepDoubles.TryRealChatAsync(http, "memory-longmemeval trigger");
            if (judge is null) return 1;
            trigger = new TriggerAudit(new LlmMemoryVerificationPolicy(new SweepDoubles.BenchClientFactory(judge)));
            configs = [.. configs.Select(a => a with { Verification = trigger })];
            Console.WriteLine($"memory-longmemeval trigger: judge {judge.Model} at {SweepDoubles.ChatBaseUrl}"
                + SweepDoubles.StandardNote(SweepDoubles.ChatBaseUrl));
        }

        // `--rerank` prices the cross-encoder on the workload this design makes its claim on. It is opt-in
        // and TRIPLES the arms it is given, so it stays off by default: an arm costs a full ingestion per
        // question, ~490 turns each under --haystack.
        CrossEncoderReranker? reranker = null;
        if (args.Contains("--rerank"))
        {
            reranker = new CrossEncoderReranker(http, CrossEncoderReranker.BaseUrl, CrossEncoderReranker.Model);
            if (!await reranker.ReachableAsync())
            {
                Console.Error.WriteLine($"--rerank: nothing answered at {CrossEncoderReranker.BaseUrl}. "
                    + "llama-server -m <bge-reranker*.gguf> --reranking --port 8081, then set "
                    + $"{CrossEncoderReranker.UrlVariable}.");
                return 1;   // asked for and unavailable is an ERROR, never a quietly narrower run
            }
            Console.WriteLine($"memory-longmemeval rerank: {CrossEncoderReranker.Model} "
                + $"at {CrossEncoderReranker.BaseUrl}");
            configs = WithRerank(configs, reranker, RecallLimit);
        }

        var stopwatch = Stopwatch.StartNew();
        string[] arms = [.. configs.Select(c => c.Name), .. wantsCosine ? new[] { VectorArm } : []];
        var currentHit = new Dictionary<string, int>();
        var staleHit = new Dictionary<string, int>();
        var prefers = new Dictionary<string, int>();
        var decidable = new Dictionary<string, int>();

        // PER-QUESTION outcomes, because the arms answer the same questions and the comparison that follows
        // is therefore paired. Counts alone cannot support one: they lose which questions the arms disagreed
        // on, which is the whole of what a paired test reads.
        var perQuestion = new Dictionary<string, Dictionary<int, bool>>(StringComparer.Ordinal);

        var done = 0;
        foreach (var q in sampled)
        {
            Progress(++done, sampled.Count);
            if (trigger is not null) { trigger.Current = q.Current; trigger.Stale = q.Stale; }
            var results = new List<(string Arm, List<string> Got)>();

            // ONE PRISTINE STORE PER ARM. Only the configs a selected arm needs are ingested, which is the
            // whole cost of the run: under `--haystack` a question carries ~490 turns, so ingesting an arm
            // nobody scores is the most expensive way to measure nothing.
            foreach (var arm in configs)
            {
                using var db = new MemoryPolicySweep.SweepDb();
                var engine = EngineFor(arm, db, vectorProvider);
                foreach (var t in q.Turns)
                    await engine.RememberAsync(new MemoryWrite(Task, Scope, $"{t.Tag} {t.Text}"));

                results.Add((arm.Name,
                    (await engine.RecallAsync(new MemoryQuery(Task, Scope, q.Text, Limit: RecallLimit)))
                        .Items.Select(i => i.Content ?? i.Headline).ToList()));
            }

            if (wantsCosine)
            {
                // The cosine index is built from the same turn text every arm ingested, so the control reads
                // the same corpus rather than a parallel one.
                var index = new List<(string Text, float[] Vector)>();
                foreach (var t in q.Turns)
                {
                    var content = $"{t.Tag} {t.Text}";
                    index.Add((content, await vectorProvider.EmbedAsync(content)));
                }
                results.Add((VectorArm, (await TopKAsync(vectorProvider, index, q.Text, RecallLimit)).ToList()));
            }

            foreach (var (arm, got) in results)
            {
                var cur = FirstIndexOf(got, q.Current);
                var sta = FirstIndexOf(got, q.Stale);
                if (cur >= 0) currentHit[arm] = currentHit.GetValueOrDefault(arm) + 1;
                if (sta >= 0) staleHit[arm] = staleHit.GetValueOrDefault(arm) + 1;

                // DECIDABLE means the arm returned at least one of the two, so a preference exists to score.
                // Without this an arm that retrieves NEITHER would score a vacuous 100%.
                if (cur < 0 && sta < 0) continue;
                decidable[arm] = decidable.GetValueOrDefault(arm) + 1;
                var preferred = cur >= 0 && (sta < 0 || cur < sta);
                if (preferred) prefers[arm] = prefers.GetValueOrDefault(arm) + 1;

                // Keyed by the question's position in the sample, which is what makes two arms' outcomes
                // line up. An UNDECIDABLE question is absent rather than false — it is not a failure to
                // prefer the current fact, and folding it in would score a retrieval miss as a preference.
                if (!perQuestion.TryGetValue(arm, out var byQuestion))
                    perQuestion[arm] = byQuestion = [];
                byQuestion[done] = preferred;
            }
        }

        Console.WriteLine($"{"Arm",-10} {"prefers current",18} {"current@k",12} {"stale@k",12} {"decidable",11}");
        Console.WriteLine(new string('-', 66));
        foreach (var arm in arms)
        {
            var d = decidable.GetValueOrDefault(arm);
            var p = prefers.GetValueOrDefault(arm);
            var (low, high) = BenchStats.Wilson(p, d);
            Console.WriteLine($"{arm,-10} {(d == 0 ? "-" : $"{(double)p / d:P1} ({p}/{d})"),18} "
                + $"{$"{(double)currentHit.GetValueOrDefault(arm) / sampled.Count:P1}",12} "
                + $"{$"{(double)staleHit.GetValueOrDefault(arm) / sampled.Count:P1}",12} {d,11}"
                + (d == 0 ? "" : $"   95% CI [{low:P1}, {high:P1}]"));
        }

        PrintPairedComparison(arms, perQuestion);

        Console.WriteLine();
        Console.WriteLine("  'prefers current' is scored only over questions where the arm returned at least");
        Console.WriteLine("  one of the two facts — otherwise retrieving NEITHER would score a vacuous 100%.");
        if (trigger is not null) PrintTrigger(trigger);
        if (reranker is not null)
        {
            var (calls, scored, distinct) = reranker.Audit;
            Console.WriteLine($"  cross-encoder: calls {calls}, pairs scored {scored}, distinct scores {distinct}"
                + (calls > 0 && distinct <= 1 ? "  ! ONE distinct score - it reordered nothing" : ""));
        }
        Console.WriteLine("  'stale@k' is not a failure on its own: returning both is fine if the current one");
        Console.WriteLine("  ranks first. It is here so a preference win cannot hide a recall collapse.");
        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s   "
            + $"vector backend {vectorProvider.Misses} call(s), {vectorProvider.Hits} cache hit(s)"
            + $"{TruncationNote()}.");
        return 0;
    }

    /// <summary>An equal-CONTEXT-SPEND cap over a shot table (<c>--budget N</c>): every arm is allowed the
    /// same characters and fills them however it likes.
    ///
    /// <para><b>Why the tables need it.</b> Uncapped, <c>chars/q</c> varies ~9× down a single column, so
    /// all-evidence recall rewards whichever arm returned MORE — the axis this design deliberately does not
    /// optimise, since a recall returns headlines and pays for content only when asked (<b>D100</b>,
    /// <b>D102</b>). Capped, the question becomes "who scores best for the same context spend".</para>
    ///
    /// <para><b>It REPRODUCES the shipped rule</b> rather than inventing a plausible one: this is
    /// <c>GraphMemoryEngine</c>'s own <c>MemoryQuery.CharBudget</c> cut — whole items, applied after ranking,
    /// an item that does not fit SKIPPED rather than ending the fill, and never empty, so a caller asking
    /// for less than one entry gets one entry. Stopping at the first miss instead reads as the same idea and
    /// scores a different body: at shot 2 the head of the list is the same entries upgraded to full content,
    /// so a prefix rule throws away every cheap headline behind them. The engine's one carve-out —
    /// authoritative material is never dropped — has nothing to bind on here, since this corpus grades
    /// nothing.</para>
    ///
    /// <para><b>The library cannot express this over a WALK</b>, which is what the arm prices.
    /// <c>MemoryWalkOptions</c> bounds items and not characters, and <c>WalkAsync</c> passes <c>null</c> for
    /// each expansion's own budget, so a caller wanting an n-shot walk inside a context budget has to cut the
    /// body afterwards — exactly what this does.</para>
    ///
    /// <para><b><see cref="Bound"/> is the fire counter.</b> A cap that never binds leaves every arm its
    /// unbounded body and reads in the score exactly like a cap that changed nothing.</para></summary>
    private sealed class ContextBudget(int chars)
    {
        internal int Chars => chars;

        internal bool Binds => chars > 0;

        /// <summary>Bodies the cap actually cut, and bodies it was offered.</summary>
        internal int Bound { get; private set; }

        internal int Offered { get; private set; }

        /// <summary>Bodies whose FIRST item alone exceeded the cap, so the never-empty rule kept it and the
        /// arm spent more than the budget. Counted because it makes <c>chars/q</c> legitimately exceed the
        /// cap, and an unexplained overrun reads as the cap leaking.</summary>
        internal int Overran { get; private set; }

        /// <summary>Fill-arm recalls that did NOT spend the budget, and how many of those ran out of CORPUS
        /// rather than of <c>k</c>. Both understate what the allowance is worth and they are not the same
        /// finding: <see cref="FillShortOfK"/> means the store held less than <c>k</c> and the arm degenerates
        /// into "return everything" — which scores near-perfectly on a small store and measures the FIXTURE.
        /// Raising <c>k</c> fixes the first and nothing fixes the second.</summary>
        internal int FillUnspent { get; private set; }

        internal int FillShortOfK { get; private set; }

        internal int FillCalls { get; private set; }

        internal void NoteFill(bool unspent, bool shortOfK)
        {
            FillCalls++;
            if (unspent) FillUnspent++;
            if (shortOfK) FillShortOfK++;
        }

        internal IReadOnlyList<string> Fit(IReadOnlyList<string> body)
        {
            if (chars <= 0) return body;

            Offered++;
            var kept = new List<string>();
            var spent = 0;
            foreach (var b in body)
            {
                if (kept.Count > 0 && spent + b.Length > chars) continue;
                spent += b.Length;
                kept.Add(b);
            }

            if (kept.Count < body.Count) Bound++;
            if (spent > chars) Overran++;
            return kept;
        }
    }

    /// <summary>The arm names a shot table reports. Under a budget the character cap is the control, so
    /// <c>vector-{ShotBudget}</c> goes — <c>k</c> no longer decides what cosine spends, and an arm whose name
    /// promises a slot count that is not what bounds it is worse than no arm — and <c>fill</c> arrives, being
    /// the only arm that tries to SPEND the allowance rather than stopping at the shipped limit.</summary>
    private static string[] ShotArmNames(ContextBudget budget, int[] pools, bool detail = false) => budget.Binds
        ? ["shot-1", "shot-2", "shot-3", FillArm, .. pools.Select(p => $"pool-{p}"),
            .. detail ? new[] { CompleteArm } : [], "vector"]
        : ["shot-1", "shot-2", "shot-3", "vector", $"vector-{ShotBudget}"];

    /// <summary>One label per (arm, budget). <b>With a single budget the suffix is dropped</b>, so a
    /// one-value run prints exactly the table it printed before a ladder existed and stays comparable with
    /// what is already published — which is also the regression check on the ladder itself.</summary>
    private static string Label(string arm, ContextBudget budget, bool ladder) =>
        ladder && budget.Binds ? $"{arm}@{budget.Chars}" : arm;

    /// <summary>Every label a ladder reports, narrowed by <c>--arms</c>.</summary>
    private static string[]? ShotArms(string[] args, ContextBudget[] budgets, int[] pools, bool detail = false)
    {
        var ladder = budgets.Length > 1;
        return FilterArms(args,
            [.. budgets.SelectMany(b => ShotArmNames(b, pools, detail).Select(a => Label(a, b, ladder)))]);
    }

    /// <summary>Cosine's body under a budget: drawn best-first from the WHOLE ranked corpus and then capped,
    /// so it spends the entire allowance on its own best material. That is the strongest form of the arm the
    /// walk has to beat — a shallow top-k could run out of items before it ran out of budget and lose for a
    /// reason that is the harness's rather than cosine's.</summary>
    private static async Task<IReadOnlyList<string>> BudgetedVectorAsync(SweepDoubles.CachingVectorProvider vectorProvider,
        List<(string Text, float[] Vector)> index, string query, ContextBudget budget) =>
        budget.Fit((await TopKAsync(vectorProvider, index, query, index.Count)).ToList());

    /// <summary>The arm that SPENDS the budget: one recall at <paramref name="fillK"/> with the engine's own
    /// <c>MemoryQuery.CharBudget</c> doing the cut.
    ///
    /// <para><b>A fresh store, because a recall REINFORCES what it returns</b> and the caller's walk has
    /// already run on its own. The re-ingestion costs no model call: every embed is a cache hit by the time
    /// this is reached, which is what makes a second store affordable at all.</para>
    ///
    /// <para><b>ONE recall serves the whole ladder, and that is an equivalence rather than a shortcut.</b>
    /// The engine reinforces BEFORE it applies <c>CharBudget</c> (<c>ReinforceAsync</c> precedes the cut in
    /// <c>GraphMemoryEngine.RecallAsync</c>), so a budgeted recall and an unbudgeted one at the same
    /// <c>k</c> differ in nothing but the trailing trim — and that trim is the loop
    /// <see cref="ContextBudget.Fit"/> reproduces, over the same per-item cost
    /// (<c>Content?.Length ?? Headline.Length</c>), with no authoritative material in this corpus for the
    /// engine's one carve-out to bind on. Recalling per budget would pay a full ingestion each to compute
    /// the same rows.</para></summary>
    /// <returns>The UNCUT body at <paramref name="limit"/> and the RECALL's own elapsed milliseconds — the
    /// re-ingestion above is the harness's cost for sharing one recall across the ladder, and billing it to
    /// the arm would report seconds in a column of milliseconds. The caller applies each budget's own
    /// trim.</returns>
    /// <param name="multiplier">Candidates gathered PER returned item. Null takes the shipped
    /// <c>GraphMemoryOptions.CandidateMultiplier</c>, which is what <c>fill</c> uses; a value is what
    /// separates a wider POOL from a wider OUTPUT, since the engine gathers <c>limit × multiplier</c> and
    /// the two arms can therefore be made to see the same candidates and return different counts.</param>
    /// <param name="detail">How much of each returned item to render. The default is the shipped
    /// <see cref="MemoryDetail.Headline"/>; <see cref="MemoryDetail.Full"/> is the <c>full</c> arm and is
    /// visible ONLY through a character cap, since detail is projected after ranking and cannot move the set.</param>
    private static async Task<(IReadOnlyList<string> Body, double Ms)> RecallArmAsync(Question q,
        SweepDoubles.CachingVectorProvider vectorProvider, double expandFloor, int limit, int? multiplier = null,
        MemoryDetail detail = MemoryDetail.Headline)
    {
        using var db = new MemoryPolicySweep.SweepDb();
        var options = new GraphMemoryOptions { ExpansionRetrievabilityFloor = expandFloor };
        if (multiplier is { } m) options = options with { CandidateMultiplier = m };
        var engine = new GraphMemoryEngine("lme", new SqliteMemoryGraphStore(db.Factory), options: options, seams: new GraphMemorySeams
            {
                Providers = [vectorProvider],
                Vectors = new InMemoryVectorStore(),
            });

        foreach (var t in q.Turns)
            await engine.RememberAsync(new MemoryWrite(Task, Scope, $"{t.Tag} {t.Text}"));

        var clock = Stopwatch.StartNew();
        var recall = await engine.RecallAsync(
            new MemoryQuery(Task, Scope, q.Text, Limit: limit, Detail: detail));
        return ([.. recall.Items.Select(i => i.Content ?? i.Headline)], clock.Elapsed.TotalMilliseconds);
    }

    /// <summary>Says the cap is on BEFORE the table, because a budgeted table is not a narrower version of
    /// the unbudgeted one — <c>vector</c> changes meaning (best-first over the whole corpus, not a top-k)
    /// and one arm is gone. A reader handed the numbers alone would compare them with published rows they
    /// do not belong beside.</summary>
    private static void BudgetPreamble(ContextBudget[] budgets, int fillK, int[] pools, bool detail = false)
    {
        var binding = budgets.Where(b => b.Binds).ToList();
        if (binding.Count == 0) return;

        Console.WriteLine($"Budget:    {string.Join(", ", binding.Select(b => b.Chars))} characters per arm, "
            + "enforced in CODE - so this");
        Console.WriteLine("           measures each arm CHOOSING within the same context spend, by the");
        Console.WriteLine("           engine's own CharBudget rule. `vector` is now cosine best-first over the");
        Console.WriteLine("           whole corpus until the budget fills, so the k-named arm is dropped.");
        Console.WriteLine($"Fill:      `fill` is the shipped recall at k = {fillK} with the engine's OWN");
        Console.WriteLine("           CharBudget doing the cut - the arm that SPENDS the allowance instead of");
        Console.WriteLine($"           stopping at k = {RecallLimit}. It is not shot 1 with a longer tail: the");
        Console.WriteLine("           engine gathers k x CandidateMultiplier, so the pool widens with k.");
        if (pools.Length > 0)
        {
            Console.WriteLine($"Pool:      `pool-M` is k = {RecallLimit} at CandidateMultiplier M, which sees");
            Console.WriteLine($"           {RecallLimit} x M candidates and returns {RecallLimit} - so it holds the");
            Console.WriteLine("           OUTPUT fixed and varies only the POOL, which `fill` moves together.");
            Console.WriteLine("           `pool-4` is the shipped multiplier and must land on shot-1: same");
            Console.WriteLine("           configuration by a different route, so it is this arm's own control.");
        }

        if (detail)
        {
            Console.WriteLine($"Detail:    `{CompleteArm}` is k = {RecallLimit} at the shipped multiplier asking for");
            Console.WriteLine("           MemoryDetail.Full - so it and shot-1 choose the SAME items in the same");
            Console.WriteLine("           order and differ only in how much of each is rendered. Detail is");
            Console.WriteLine("           projected after ranking, so without this cap the two are identical;");
            Console.WriteLine("           the cap is what converts whole items into FEWER of them.");
            Console.WriteLine("           A budget too large to bind on either is the arm's VACUITY control: it");
            Console.WriteLine("           must reproduce shot-1 on every quality column while carrying whatever");
            Console.WriteLine("           multiple of the characters this corpus costs. If it does not, the");
            Console.WriteLine("           metric is reading TEXT and no row below is about retrieval.");
        }

        if (binding.Count > 1)
            Console.WriteLine("           Arms are suffixed @<budget>; one ingestion serves the whole ladder.");
        Console.WriteLine();
    }

    /// <summary>Reports whether the cap did anything, because a table produced by a cap that never bound is
    /// the UNCAPPED table under a different heading — the degenerate-arm trap
    /// (<c>.claude/knowledge/pitfalls.md</c>, "an arm that is SUPPOSED to move needs a control proving it
    /// CAN"). The <c>chars/q</c> column above is the second half of the check: an arm may exceed the cap
    /// only where <see cref="ContextBudget.Overran"/> accounts for it, and an arm well under it was bounded
    /// by its own <c>k</c> rather than by the budget.</summary>
    private static void PrintBudget(ContextBudget[] budgets)
    {
        var binding = budgets.Where(b => b.Binds).ToList();
        if (binding.Count == 0) return;

        Console.WriteLine();
        Console.WriteLine("  BUDGET: the cap is the engine's own CharBudget rule - whole items, an item that");
        Console.WriteLine("  does not fit skipped rather than ending the fill, never empty. `chars/q` must be");
        Console.WriteLine("  <= the cap except where a body's FIRST item alone exceeded it (never-empty).");
        foreach (var b in binding)
        {
            Console.WriteLine($"    {b.Chars,7}: cut {b.Bound} of {b.Offered} bodies, {b.Overran} over on the "
                + "first item;");
            // Two independent facts, not a subset: a recall can come up short of k and still be trimmed by
            // the budget, so reporting one "of" the other would be arithmetic nonsense on real data.
            Console.WriteLine($"             `fill` left the budget unspent on {b.FillUnspent}/{b.FillCalls}"
                + $" recalls; {b.FillShortOfK}/{b.FillCalls} returned fewer than k items at all.");
            if (b.Bound == 0)
                Console.WriteLine("             ! DEGENERATE: the cap bound nothing - this is the uncapped table.");
            if (b.FillCalls > 0 && b.FillShortOfK * 2 > b.FillCalls)
                Console.WriteLine("             ! `fill` is returning most of the STORE, so it scores the fixture"
                    + " rather than retrieval - use --haystack.");
            else if (b.FillCalls > 0 && b.FillUnspent * 2 > b.FillCalls)
                Console.WriteLine("             ! `fill` mostly under-spends: raise --fill-k or it is shot 1 widened.");
        }

        Console.WriteLine("  An arm well UNDER its cap was bounded by its own k and not by the budget, so that");
        Console.WriteLine("  row is what that k costs rather than the best it could do with the room.");
    }

    /// <summary>
    /// The per-shot curve on the class where suppression is supposed to pay. <b>'clean' is the column that
    /// matters</b>: the context holds the current fact and NOT the one it superseded, which is what a reader
    /// actually consumes — <c>current@k</c> alone rewards a context that also carries the wrong answer.
    /// <para>Every question gets a pristine store, so unlike the LoCoMo harness there is no cross-question
    /// reinforcement to confound the shots.</para>
    /// </summary>
    private static async Task<int> RunShotsAsync(List<Question> sampled,
        SweepDoubles.CachingVectorProvider vectorProvider, double expandFloor, ContextBudget[] budgets, int fillK,
        int[] pools, string[] args, bool detail = false)
    {
        var stopwatch = Stopwatch.StartNew();
        var arms = ShotArms(args, budgets, pools, detail);
        if (arms is null) return 1;
        var ladder = budgets.Length > 1;
        var cur = new Dictionary<string, int>();
        var sta = new Dictionary<string, int>();
        var clean = new Dictionary<string, int>();
        var items = new Dictionary<string, int>();
        var chars = new Dictionary<string, long>();
        var millis = new Dictionary<string, double>();
        var done = 0;

        foreach (var q in sampled)
        {
            Progress(++done, sampled.Count);
            using var db = new MemoryPolicySweep.SweepDb();
            var store = new SqliteMemoryGraphStore(db.Factory);
            var engine = new GraphMemoryEngine("lme", store, options: new GraphMemoryOptions { ExpansionRetrievabilityFloor = expandFloor }, seams: new GraphMemorySeams
                {
                    Providers = [vectorProvider],
                    Vectors = new InMemoryVectorStore(),
                });

            var index = new List<(string Text, float[] Vector)>();
            foreach (var t in q.Turns)
            {
                var content = $"{t.Tag} {t.Text}";
                await engine.RememberAsync(new MemoryWrite(Task, Scope, content));
                index.Add((content, await vectorProvider.EmbedAsync(content)));
            }
            await vectorProvider.EmbedAsync(q.Text);   // warm the query embed so no arm pays for it alone

            void Score(string arm, IReadOnlyList<string> body, double ms)
            {
                var hasCurrent = q.Current.Any(t => body.Any(b => b.Contains(t.Tag, StringComparison.Ordinal)));
                var hasStale = q.Stale.Any(t => body.Any(b => b.Contains(t.Tag, StringComparison.Ordinal)));
                if (hasCurrent) cur[arm] = cur.GetValueOrDefault(arm) + 1;
                if (hasStale) sta[arm] = sta.GetValueOrDefault(arm) + 1;
                if (hasCurrent && !hasStale) clean[arm] = clean.GetValueOrDefault(arm) + 1;
                items[arm] = items.GetValueOrDefault(arm) + body.Count;
                chars[arm] = chars.GetValueOrDefault(arm) + body.Sum(b => b.Length);
                millis[arm] = millis.GetValueOrDefault(arm) + ms;
            }

            // ONE walk serves every budget: the cap is applied to the body AFTER the walk, so re-walking per
            // budget would buy nothing and cost a reinforcement pass that changes the store underneath.
            await WalkAsync(engine, q.Text, (shot, body, ms) =>
            {
                foreach (var b in budgets) Score(Label($"shot-{shot}", b, ladder), b.Fit(body), ms);
            });

            var binding = budgets.Where(b => b.Binds).ToList();

            // Both deep bodies are computed ONCE and trimmed per budget - see FillAsync for why that is an
            // equivalence and not a shortcut, and why the fill store has to be a separate one.
            // `ms/q` must time the RETRIEVAL and nothing around it. The fill store's re-ingestion is a cost
            // of the harness sharing one recall across the ladder, not of the arm, and billing it here put
            // 14.7 SECONDS in a column whose other rows are milliseconds - a number no reader could take at
            // face value and none should have been asked to.
            var deepClock = Stopwatch.StartNew();
            var deep = binding.Count > 0 ? (await TopKAsync(vectorProvider, index, q.Text, index.Count)).ToList() : [];
            var deepMs = deepClock.Elapsed.TotalMilliseconds;
            var (filled, fillMs) = binding.Count > 0
                ? await RecallArmAsync(q, vectorProvider, expandFloor, fillK)
                : ((IReadOnlyList<string>)[], 0d);

            // Each multiplier needs its OWN store: a recall reinforces what it returns, so scoring two
            // multipliers against one store would let the first change the second's ranking - the shared-store
            // defect Part 118 corrected. The embeds are cache hits, so the cost is SQLite and not the model.
            var pooled = new List<(int M, IReadOnlyList<string> Body, double Ms)>();
            foreach (var m in binding.Count > 0 ? pools : [])
            {
                var (body, ms) = await RecallArmAsync(q, vectorProvider, expandFloor, RecallLimit, m);
                pooled.Add((m, body, ms));
            }

            // The COMPLETENESS arm, on its own store for the same reason every arm above has one. Shipped
            // limit, shipped multiplier, MemoryDetail.Full — so the ONLY difference from `shot-1` is how much
            // of each chosen item is rendered, and the budget is what turns that into fewer items.
            var (whole, wholeMs) = detail && binding.Count > 0
                ? await RecallArmAsync(q, vectorProvider, expandFloor, RecallLimit, null, MemoryDetail.Full)
                : ((IReadOnlyList<string>)[], 0d);

            foreach (var b in budgets)
            {
                if (!b.Binds)
                {
                    foreach (var (arm, k) in new[] { ("vector", RecallLimit), ($"vector-{ShotBudget}", ShotBudget) })
                    {
                        var vclock = Stopwatch.StartNew();
                        var got = (await TopKAsync(vectorProvider, index, q.Text, k)).ToList();
                        Score(arm, got, vclock.Elapsed.TotalMilliseconds);
                    }

                    continue;
                }

                Score(Label("vector", b, ladder), b.Fit(deep), deepMs);

                // Uncut means the WHOLE recall fitted, so k ran out before the budget did and this row
                // understates what the allowance is worth - the one reading the arm's name would hide.
                var fit = b.Fit(filled);
                b.NoteFill(fit.Count == filled.Count, filled.Count < fillK);
                Score(Label(FillArm, b, ladder), fit, fillMs);

                foreach (var (m, body, ms) in pooled)
                    Score(Label($"pool-{m}", b, ladder), b.Fit(body), ms);

                if (detail) Score(Label(CompleteArm, b, ladder), b.Fit(whole), wholeMs);
            }
        }

        var n = sampled.Count;
        Console.WriteLine($"{"Arm",-12} {"clean",10} {"current@k",11} {"stale@k",10} {"items/q",9} "
            + $"{"chars/q",9} {"ms/q",8}");
        Console.WriteLine(new string('-', 74));
        foreach (var arm in arms)
            Console.WriteLine($"{arm,-12} {(double)clean.GetValueOrDefault(arm) / n,10:P1} "
                + $"{(double)cur.GetValueOrDefault(arm) / n,11:P1} {(double)sta.GetValueOrDefault(arm) / n,10:P1} "
                + $"{(double)items.GetValueOrDefault(arm) / n,9:F1} {(double)chars.GetValueOrDefault(arm) / n,9:F0} "
                + $"{millis.GetValueOrDefault(arm) / n,8:F1}");

        Console.WriteLine();
        Console.WriteLine("  'clean' = the context holds the CURRENT fact and not the superseded one. It is the");
        Console.WriteLine("  one a reader's answer actually depends on: a context carrying both hands the model");
        Console.WriteLine("  the contradiction to resolve, which is the work this layer exists to do for it.");
        Console.WriteLine("  'ms/q' is memory-layer time only, and the vector arms are an in-memory brute force");
        Console.WriteLine("  with no persistence and no write-back - so that column compares a database against");
        Console.WriteLine("  an array, not two retrieval strategies.");
        PrintBudget(budgets);
        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s   "
            + $"vector backend {vectorProvider.Misses} call(s), {vectorProvider.Hits} cache hit(s)"
            + $"{TruncationNote()}.");
        return 0;
    }

    /// <summary>Scores the library's walk: <c>MemoryWalk.WalkAsync</c> is the loop, and this calls
    /// <paramref name="score"/> after each step with the cumulative context.
    /// <para><b>Shared by both shot curves on purpose</b>, so "shot 2" cannot quietly mean two things. The
    /// loop itself used to live here AND in <c>MemoryLocomoBench</c>, which is the duplication
    /// <c>docs/task-archive.md</c> Part 234 cited when it asked for this surface.</para>
    /// <para>Shots 2 and 3 come from ONE walk because three is a strict superset of two, so the arms stay
    /// exactly nested and any difference between them is the extra shot and nothing else.</para>
    /// <para><b><c>MaxEntries</c> is passed EXPLICITLY.</b> The library derives twice what step 1 returned,
    /// which equals <c>ShotBudget</c> only when a question's recall fills its limit — leaving it null would
    /// shrink the bound on short recalls and move published figures for a reason that is not a defect. The
    /// expansion floor is the ENGINE's (<c>GraphMemoryOptions.ExpansionRetrievabilityFloor</c>), not this
    /// harness's.</para></summary>
    private static async Task WalkAsync(GraphMemoryEngine engine, string question,
        Action<int, IReadOnlyList<string>, double> score)
    {
        var clock = Stopwatch.StartNew();
        var options = new MemoryWalkOptions { SeedsPerStep = ExpandSeeds, Hops = 1, MaxItems = ShotBudget };

        await foreach (var step in engine.WalkAsync(
            new MemoryQuery(Task, Scope, question, Limit: RecallLimit), options))
        {
            score(step.Number, [.. step.Items.Select(i => i.Content ?? i.Headline)],
                clock.Elapsed.TotalMilliseconds);
            if (step.Number >= 3) break;   // this harness's curve is three shots, as published
        }
    }

    /// <summary>The temporal class's shot curve — the same walk as <see cref="RunShotsAsync"/> scored the
    /// other way round. Knowledge-update wants the superseded fact GONE; this class usually needs every
    /// flagged turn, so the question a shot answers here is whether expanding RECOVERS evidence the first
    /// load missed. That is the one workload where more shots should help most, and it had no curve at
    /// all.</summary>
    private static async Task<int> RunTemporalShotsAsync(List<Question> sampled,
        SweepDoubles.CachingVectorProvider vectorProvider, double expandFloor, ContextBudget[] budgets, int fillK,
        int[] pools, string[] args, bool detail = false)
    {
        var stopwatch = Stopwatch.StartNew();
        var arms = ShotArms(args, budgets, pools, detail);
        if (arms is null) return 1;
        var ladder = budgets.Length > 1;
        var all = new Dictionary<string, int>();
        var any = new Dictionary<string, int>();
        var found = new Dictionary<string, int>();
        var items = new Dictionary<string, int>();
        var chars = new Dictionary<string, long>();
        var evidenceTotal = 0;
        var done = 0;

        foreach (var q in sampled)
        {
            Progress(++done, sampled.Count);
            using var db = new MemoryPolicySweep.SweepDb();
            var store = new SqliteMemoryGraphStore(db.Factory);
            var engine = new GraphMemoryEngine("lme", store, options: new GraphMemoryOptions { ExpansionRetrievabilityFloor = expandFloor }, seams: new GraphMemorySeams
                {
                    Providers = [vectorProvider],
                    Vectors = new InMemoryVectorStore(),
                });

            var index = new List<(string Text, float[] Vector)>();
            foreach (var t in q.Turns)
            {
                var content = $"{t.Tag} {t.Text}";
                await engine.RememberAsync(new MemoryWrite(Task, Scope, content));
                index.Add((content, await vectorProvider.EmbedAsync(content)));
            }
            await vectorProvider.EmbedAsync(q.Text);   // warm the query embed so no arm pays for it alone
            evidenceTotal += q.Evidence.Count;

            void Score(string arm, IReadOnlyList<string> body)
            {
                var hits = q.Evidence.Count(t => body.Any(b => b.Contains(t.Tag, StringComparison.Ordinal)));
                found[arm] = found.GetValueOrDefault(arm) + hits;
                if (hits == q.Evidence.Count) all[arm] = all.GetValueOrDefault(arm) + 1;
                if (hits > 0) any[arm] = any.GetValueOrDefault(arm) + 1;
                items[arm] = items.GetValueOrDefault(arm) + body.Count;
                chars[arm] = chars.GetValueOrDefault(arm) + body.Sum(b => b.Length);
            }

            // ONE walk serves every budget - see the knowledge-update runner for why re-walking is wrong.
            await WalkAsync(engine, q.Text, (shot, body, _) =>
            {
                foreach (var b in budgets) Score(Label($"shot-{shot}", b, ladder), b.Fit(body));
            });

            var binding = budgets.Where(b => b.Binds).ToList();
            var deep = binding.Count > 0 ? (await TopKAsync(vectorProvider, index, q.Text, index.Count)).ToList() : [];
            var filled = binding.Count > 0
                ? (await RecallArmAsync(q, vectorProvider, expandFloor, fillK)).Body
                : [];

            // One store per multiplier - see the knowledge-update runner for why sharing one would let the
            // first recall's reinforcement change the second's ranking.
            var pooled = new List<(int M, IReadOnlyList<string> Body)>();
            foreach (var m in binding.Count > 0 ? pools : [])
                pooled.Add((m, (await RecallArmAsync(q, vectorProvider, expandFloor, RecallLimit, m)).Body));

            // The COMPLETENESS arm, as the knowledge-update runner builds it and for the same reason: its own
            // store, the shipped limit and multiplier, MemoryDetail.Full. Here it is scored on COVERAGE, so
            // a cap that suppresses a superseded fact one class over can only lose flagged turns.
            var whole = detail && binding.Count > 0
                ? (await RecallArmAsync(q, vectorProvider, expandFloor, RecallLimit, null, MemoryDetail.Full)).Body
                : [];

            foreach (var b in budgets)
            {
                if (!b.Binds)
                {
                    foreach (var (arm, k) in new[] { ("vector", RecallLimit), ($"vector-{ShotBudget}", ShotBudget) })
                        Score(arm, (await TopKAsync(vectorProvider, index, q.Text, k)).ToList());
                    continue;
                }

                Score(Label("vector", b, ladder), b.Fit(deep));
                var fit = b.Fit(filled);
                b.NoteFill(fit.Count == filled.Count, filled.Count < fillK);
                Score(Label(FillArm, b, ladder), fit);

                foreach (var (m, body) in pooled)
                    Score(Label($"pool-{m}", b, ladder), b.Fit(body));

                if (detail) Score(Label(CompleteArm, b, ladder), b.Fit(whole));
            }
        }

        var n = sampled.Count;
        Console.WriteLine($"{"Arm",-12} {"all evidence@k",16} {"any evidence@k",16} {"evidence turns",16} "
            + $"{"items/q",9} {"chars/q",9}");
        Console.WriteLine(new string('-', 84));
        foreach (var arm in arms)
            Console.WriteLine($"{arm,-12} "
                + $"{$"{(double)all.GetValueOrDefault(arm) / n:P1}",16} "
                + $"{$"{(double)any.GetValueOrDefault(arm) / n:P1}",16} "
                + $"{$"{(double)found.GetValueOrDefault(arm) / evidenceTotal:P1}",16} "
                + $"{(double)items.GetValueOrDefault(arm) / n,9:F1} "
                + $"{(double)chars.GetValueOrDefault(arm) / n,9:F0}");

        Console.WriteLine();
        Console.WriteLine("  'all evidence@k' is the one that matters: a question in this class usually needs every");
        Console.WriteLine("  flagged turn, so retrieving one of two answers nothing. If expansion is worth");
        Console.WriteLine("  anything on any workload it should be worth it HERE, where the first load missing");
        Console.WriteLine("  one turn of two is the whole failure mode.");
        PrintBudget(budgets);
        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s   "
            + $"vector backend {vectorProvider.Misses} call(s), {vectorProvider.Hits} cache hit(s)"
            + $"{TruncationNote()}.");
        return 0;
    }

    /// <summary>Reports where the pair lands on each ranking signal, and what that position is WORTH under
    /// RRF's own curve. The rank gap and the contribution gap are separate columns on purpose: a gap that
    /// holds while its contribution collapses is the whole hypothesis, and one number cannot show it.</summary>
    private static async Task RunRanksAsync(List<Question> sampled, SweepDoubles.CachingVectorProvider vectorProvider)
    {
        const double K = 60;   // ReciprocalRankFusionOptions.K's shipped default.
        var stopwatch = Stopwatch.StartNew();
        var rows = new List<(int Pool, int RelCur, int RelSta, int RetCur, int RetSta, double RCur, double RSta)>();
        var missing = 0;
        var done = 0;
        var curAtK = new int[RankLadder.K.Length];
        var staAtK = new int[RankLadder.K.Length];
        int agreed = 0, compared = 0;

        foreach (var q in sampled)
        {
            Progress(++done, sampled.Count);
            using var db = new MemoryPolicySweep.SweepDb();
            var store = new SqliteMemoryGraphStore(db.Factory);
            var probe = new RankProbe(new ReciprocalRankFusionPolicy()) { Current = q.Current, Stale = q.Stale };
            var engine = new GraphMemoryEngine("lme", store, seams: new GraphMemorySeams
                {
                    Providers = [vectorProvider],
                    Vectors = new InMemoryVectorStore(),
                    Ranking = probe,
                });

            foreach (var t in q.Turns)
                await engine.RememberAsync(new MemoryWrite(Task, Scope, $"{t.Tag} {t.Text}"));
            await engine.RecallAsync(new MemoryQuery(Task, Scope, q.Text, Limit: RecallLimit));

            if (probe.Seen.Count == 0) { missing++; continue; }
            rows.Add(probe.Seen[^1]);
            for (var k = 0; k < curAtK.Length; k++) { curAtK[k] += probe.CurrentAtK[k]; staAtK[k] += probe.StaleAtK[k]; }
            agreed += probe.Agreed;
            compared += probe.Compared;
        }

        Console.WriteLine($"{"",-22} {"current",12} {"stale",12} {"gap",12}");
        Console.WriteLine(new string('-', 62));
        Console.WriteLine($"{"relevance rank",-22} {Med(rows, r => r.RelCur),12:F1} "
            + $"{Med(rows, r => r.RelSta),12:F1} {Med(rows, r => r.RelSta - r.RelCur),12:F1}");
        Console.WriteLine($"{"retrievability rank",-22} {Med(rows, r => r.RetCur),12:F1} "
            + $"{Med(rows, r => r.RetSta),12:F1} {Med(rows, r => r.RetSta - r.RetCur),12:F1}");
        Console.WriteLine($"{"retrievability value",-22} {Med(rows, r => r.RCur),12:F4} "
            + $"{Med(rows, r => r.RSta),12:F4} {Med(rows, r => r.RCur - r.RSta),12:F4}");
        Console.WriteLine();
        Console.WriteLine($"{"RRF contribution",-22} {"current",12} {"stale",12} {"gap",12}");
        Console.WriteLine(new string('-', 62));
        Console.WriteLine($"{"  from relevance",-22} {Med(rows, r => 1 / (K + r.RelCur)),12:F5} "
            + $"{Med(rows, r => 1 / (K + r.RelSta)),12:F5} "
            + $"{Med(rows, r => 1 / (K + r.RelCur) - 1 / (K + r.RelSta)),12:F5}");
        Console.WriteLine($"{"  from retrievability",-22} {Med(rows, r => 1 / (K + r.RetCur)),12:F5} "
            + $"{Med(rows, r => 1 / (K + r.RetSta)),12:F5} "
            + $"{Med(rows, r => 1 / (K + r.RetCur) - 1 / (K + r.RetSta)),12:F5}");
        Console.WriteLine();
        Console.WriteLine($"{"K",-8} {"current@k",12} {"stale@k",12}   (same pool, same ranks, scored offline)");
        Console.WriteLine(new string('-', 62));
        for (var k = 0; k < RankLadder.K.Length; k++)
            Console.WriteLine($"{RankLadder.K[k],-8:F0} {(double)curAtK[k] / rows.Count,12:P1} "
                + $"{(double)staAtK[k] / rows.Count,12:P1}{(RankLadder.K[k] == 60 ? "   <- shipped" : "")}");

        Console.WriteLine();
        Console.WriteLine($"  CONTROL: the K=60 row reproduces the SHIPPED policy's own top-{RecallLimit} on "
            + $"{agreed}/{compared} recalls.");
        Console.WriteLine("  Anything below 100% means the replica is not the formula this library runs, and");
        Console.WriteLine("  the ladder above is a table about something else.");
        Console.WriteLine();
        Console.WriteLine($"  Pool size (median): {Med(rows, r => r.Pool):F0}   scored on {rows.Count} "
            + $"question(s); {missing} had one of the pair outside the pool entirely.");
        Console.WriteLine("  A POSITIVE gap favours the current fact. 'rank' gaps count positions, so bigger");
        Console.WriteLine("  is better separation; 'contribution' gaps are what RRF actually adds up, and they");
        Console.WriteLine("  are the ones that decide the order.");
        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s   "
            + $"vector backend {vectorProvider.Misses} call(s), {vectorProvider.Hits} cache hit(s)"
            + $"{TruncationNote()}.");
    }

    private static double Med<T>(List<T> rows, Func<T, double> of)
    {
        if (rows.Count == 0) return double.NaN;
        var sorted = rows.Select(of).OrderBy(x => x).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    /// <summary>All-evidence recall. A temporal question usually needs EVERY flagged turn — "the first issue
    /// after the service" is unanswerable from the later fact alone — so partial recall is a miss, and the
    /// any-evidence column beside it shows how much of the gap is partial rather than total.</summary>
    private static async Task<int> RunTemporalAsync(List<Question> sampled,
        SweepDoubles.CachingVectorProvider vectorProvider, string[] args)
    {
        var configs = SelectConfigs(args, out var wantsCosine);
        if (configs is null) return 1;

        var stopwatch = Stopwatch.StartNew();
        string[] arms = [.. configs.Select(c => c.Name), .. wantsCosine ? new[] { VectorArm } : []];
        var all = new Dictionary<string, int>();
        var any = new Dictionary<string, int>();
        var evidenceTotal = 0;
        var evidenceFound = new Dictionary<string, int>();

        var done = 0;
        foreach (var q in sampled)
        {
            Progress(++done, sampled.Count);
            var results = new List<(string Arm, List<string> Got)>();

            // One pristine store per arm, and only for arms this run scores — the same reasoning the
            // knowledge-update mode above states.
            foreach (var arm in configs)
            {
                using var db = new MemoryPolicySweep.SweepDb();
                var engine = EngineFor(arm, db, vectorProvider);
                foreach (var t in q.Turns)
                    await engine.RememberAsync(new MemoryWrite(Task, Scope, $"{t.Tag} {t.Text}"));

                results.Add((arm.Name,
                    (await engine.RecallAsync(new MemoryQuery(Task, Scope, q.Text, Limit: RecallLimit)))
                        .Items.Select(i => i.Content ?? i.Headline).ToList()));
            }

            if (wantsCosine)
            {
                var index = new List<(string Text, float[] Vector)>();
                foreach (var t in q.Turns)
                {
                    var content = $"{t.Tag} {t.Text}";
                    index.Add((content, await vectorProvider.EmbedAsync(content)));
                }
                results.Add((VectorArm, (await TopKAsync(vectorProvider, index, q.Text, RecallLimit)).ToList()));
            }

            evidenceTotal += q.Evidence.Count;

            foreach (var (arm, got) in results)
            {
                var hits = q.Evidence.Count(t => got.Any(g => g.Contains(t.Tag, StringComparison.Ordinal)));
                evidenceFound[arm] = evidenceFound.GetValueOrDefault(arm) + hits;
                if (hits == q.Evidence.Count) all[arm] = all.GetValueOrDefault(arm) + 1;
                if (hits > 0) any[arm] = any.GetValueOrDefault(arm) + 1;
            }
        }

        Console.WriteLine($"{"Arm",-10} {"all evidence@k",16} {"any evidence@k",16} {"evidence turns",16}");
        Console.WriteLine(new string('-', 62));
        foreach (var arm in arms)
            Console.WriteLine($"{arm,-10} "
                + $"{$"{(double)all.GetValueOrDefault(arm) / sampled.Count:P1}",16} "
                + $"{$"{(double)any.GetValueOrDefault(arm) / sampled.Count:P1}",16} "
                + $"{$"{(double)evidenceFound.GetValueOrDefault(arm) / evidenceTotal:P1}",16}");

        Console.WriteLine();
        Console.WriteLine("  'all evidence@k' is the one that matters: a question in this class usually needs every");
        Console.WriteLine("  flagged turn, so retrieving one of two answers nothing. 'evidence turns' is the");
        Console.WriteLine("  per-turn rate, which separates 'missed one question badly' from 'missed a little");
        Console.WriteLine("  everywhere'.");
        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s   "
            + $"vector backend {vectorProvider.Misses} call(s), {vectorProvider.Hits} cache hit(s)"
            + $"{TruncationNote()}.");
        return 0;
    }

    /// <summary>The RIF TRIGGER measurement (**D109**). Wraps a real judge and records, per candidate the
    /// verifier was shown, whether it was the CURRENT fact, the SUPERSEDED one, or neither — against
    /// whether the judge endorsed it.
    ///
    /// <para><b>What decides the question.</b> A competitor penalty fires on the unendorsed half, so it can
    /// only help supersession if the judge leaves the SUPERSEDED fact unendorsed more often than the
    /// current one. Enrichment for evidence in general says nothing: both facts answer the query, so a
    /// trigger that under-endorses both is measuring relevance, not recency. The paired cell is the
    /// decisive one — of the calls where BOTH facts were shown, how often the penalty would fire on the
    /// stale fact alone (correct) against the current fact alone (backwards).</para></summary>
    private sealed class TriggerAudit(IMemoryVerificationPolicy inner) : IMemoryVerificationPolicy
    {
        internal IReadOnlyList<Turn> Current { get; set; } = [];
        internal IReadOnlyList<Turn> Stale { get; set; } = [];

        internal int Calls, Shown, Endorsed;
        internal int CurShown, CurEndorsed, StaShown, StaEndorsed;
        internal int BothShown, FiresOnStaleOnly, FiresOnCurrentOnly, FiresOnNeither, FiresOnBoth;

        private static bool Is(MemoryVerificationCandidate c, IReadOnlyList<Turn> turns)
        {
            var text = c.Content ?? c.Headline;
            return turns.Any(t => text.Contains(t.Tag, StringComparison.Ordinal));
        }

        internal int Failed;

        public async Task<MemoryVerification> VerifyAsync(MemoryVerificationRequest request,
            CancellationToken ct = default)
        {
            MemoryVerification verdict;
            try
            {
                verdict = await inner.VerifyAsync(request, ct).ConfigureAwait(false);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Counted, not swallowed: the engine fails open above, so a judge that times out would
                // otherwise shrink this measurement's denominator invisibly.
                Failed++;
                return MemoryVerification.NoOpinion;
            }
            if (!verdict.Judged) return verdict;

            var endorsed = verdict.RelevantIds.ToHashSet(StringComparer.Ordinal);
            Calls++;
            Shown += request.Candidates.Count;
            Endorsed += request.Candidates.Count(c => endorsed.Contains(c.Id));

            bool curShown = false, curEnd = false, staShown = false, staEnd = false;
            foreach (var c in request.Candidates)
            {
                var isEndorsed = endorsed.Contains(c.Id);
                if (Is(c, Current)) { curShown = true; CurShown++; if (isEndorsed) { curEnd = true; CurEndorsed++; } }
                if (Is(c, Stale)) { staShown = true; StaShown++; if (isEndorsed) { staEnd = true; StaEndorsed++; } }
            }

            if (curShown && staShown)
            {
                BothShown++;
                if (!staEnd && curEnd) FiresOnStaleOnly++;
                else if (staEnd && !curEnd) FiresOnCurrentOnly++;
                else if (staEnd && curEnd) FiresOnNeither++;
                else FiresOnBoth++;
            }
            return verdict;
        }
    }

    /// <summary>The trigger table. Reports the base rate BESIDE the conditional rates, because a
    /// conditional that merely matches the base rate is the null result this run exists to be able to
    /// report.</summary>
    private static void PrintTrigger(TriggerAudit t)
    {
        static string Pct(int n, int d) => d == 0 ? "n/a" : $"{(double)n / d:P1}";
        Console.WriteLine();
        Console.WriteLine("=== RIF TRIGGER PRECISION (D109) — does the unendorsed half know about supersession? ===");
        Console.WriteLine($"  judged calls {t.Calls}   candidates shown {t.Shown}   "
            + $"endorsed {t.Endorsed} ({Pct(t.Endorsed, t.Shown)})   judge failures {t.Failed}");
        Console.WriteLine();
        Console.WriteLine($"  {"population",-28} {"shown",8} {"endorsed",10} {"UNendorsed",12}");
        Console.WriteLine($"  {"every candidate (base rate)",-28} {t.Shown,8} {Pct(t.Endorsed, t.Shown),10} "
            + $"{Pct(t.Shown - t.Endorsed, t.Shown),12}");
        Console.WriteLine($"  {"the CURRENT fact",-28} {t.CurShown,8} {Pct(t.CurEndorsed, t.CurShown),10} "
            + $"{Pct(t.CurShown - t.CurEndorsed, t.CurShown),12}");
        Console.WriteLine($"  {"the SUPERSEDED fact",-28} {t.StaShown,8} {Pct(t.StaEndorsed, t.StaShown),10} "
            + $"{Pct(t.StaShown - t.StaEndorsed, t.StaShown),12}");
        Console.WriteLine();
        Console.WriteLine($"  Of the {t.BothShown} call(s) that showed BOTH facts — where a penalty could discriminate:");
        Console.WriteLine($"    fires on the SUPERSEDED one alone (correct)  {t.FiresOnStaleOnly,5}  {Pct(t.FiresOnStaleOnly, t.BothShown)}");
        Console.WriteLine($"    fires on the CURRENT one alone (backwards)   {t.FiresOnCurrentOnly,5}  {Pct(t.FiresOnCurrentOnly, t.BothShown)}");
        Console.WriteLine($"    fires on BOTH (no discrimination)            {t.FiresOnBoth,5}  {Pct(t.FiresOnBoth, t.BothShown)}");
        Console.WriteLine($"    fires on NEITHER (no discrimination)         {t.FiresOnNeither,5}  {Pct(t.FiresOnNeither, t.BothShown)}");
        Console.WriteLine();
        Console.WriteLine("  READ THE LAST BLOCK, not the first. Enrichment of the unendorsed half for evidence in");
        Console.WriteLine("  GENERAL says nothing about supersession: both facts answer the query. Only the paired");
        Console.WriteLine("  cells separate a penalty that would bury the stale fact from one that fires either way.");
    }

    /// <summary>The corroboration audit (`--corroborate`): is witness-counting a usable free stand-in for
    /// the judge on the workload where the judge fires backwards? No arms and no ingestion — this reads the
    /// CORPUS, because the gate's viability is a property of the corpus before it is a property of any
    /// implementation. A witness is an UNFLAGGED user turn similar to a claim's flagged turn(s); flagged
    /// turns are excluded from the pool so ground-truth evidence multiplicity cannot leak into the detector
    /// count. Two detectors on purpose (embedding cosine; content-token Jaccard), so the conclusion does not
    /// rest on one — and the base-rate row prices detector noise, without which a witness count is
    /// unreadable.</summary>
    private static async Task<int> RunCorroborationAsync(List<Question> sampled,
        SweepDoubles.CachingVectorProvider vectorProvider)
    {
        double[] thetas = [0.60, 0.70, 0.80];
        double[] overlaps = [0.35, 0.50];

        // per threshold: [0] = the current fact, [1] = the superseded one
        var witnessSum = thetas.ToDictionary(t => t, _ => new long[2]);
        var cells = thetas.ToDictionary(t => t, _ => new int[4]);      // cur>sta, sta>cur, tie>0, both-0
        var gatePass = thetas.ToDictionary(t => t, _ => new int[2]);   // the field's constants, per side
        var jWitnessSum = overlaps.ToDictionary(j => j, _ => new long[2]);
        var jCells = overlaps.ToDictionary(j => j, _ => new int[4]);

        var maxSimCur = new List<double>();
        var maxSimSta = new List<double>();
        long noiseWitnesses = 0, noiseTurns = 0;

        var done = 0;
        foreach (var q in sampled)
        {
            Progress(++done, sampled.Count);

            var flagged = q.Evidence.Select(t => (t.Session, t.Index)).ToHashSet();
            var pool = q.Turns
                .Where(t => t.Role.Equals("user", StringComparison.OrdinalIgnoreCase)
                    && !flagged.Contains((t.Session, t.Index)))
                .ToList();

            var poolVecs = await vectorProvider.EmbedAsync([.. pool.Select(t => t.Content)]);
            var curVecs = await vectorProvider.EmbedAsync([.. q.Current.Select(t => t.Content)]);
            var staVecs = await vectorProvider.EmbedAsync([.. q.Stale.Select(t => t.Content)]);

            var simCur = new double[pool.Count];
            var simSta = new double[pool.Count];
            for (var i = 0; i < pool.Count; i++)
            {
                simCur[i] = curVecs.Max(v => Cosine(poolVecs[i], v));
                simSta[i] = staVecs.Max(v => Cosine(poolVecs[i], v));
            }
            maxSimCur.Add(pool.Count == 0 ? 0 : simCur.Max());
            maxSimSta.Add(pool.Count == 0 ? 0 : simSta.Max());

            foreach (var th in thetas)
            {
                var wc = Enumerable.Range(0, pool.Count).Where(i => simCur[i] >= th).ToList();
                var ws = Enumerable.Range(0, pool.Count).Where(i => simSta[i] >= th).ToList();
                witnessSum[th][0] += wc.Count;
                witnessSum[th][1] += ws.Count;
                cells[th][wc.Count > ws.Count ? 0 : ws.Count > wc.Count ? 1 : wc.Count > 0 ? 2 : 3]++;

                // The field's constants: at least three assertions across at least two sessions, the claim's
                // own flagged turn(s) included. Sessions are this corpus's only "independent source" grain.
                var curSessions = q.Current.Select(t => t.Session)
                    .Concat(wc.Select(i => pool[i].Session)).Distinct().Count();
                var staSessions = q.Stale.Select(t => t.Session)
                    .Concat(ws.Select(i => pool[i].Session)).Distinct().Count();
                if (q.Current.Count + wc.Count >= 3 && curSessions >= 2) gatePass[th][0]++;
                if (q.Stale.Count + ws.Count >= 3 && staSessions >= 2) gatePass[th][1]++;
            }

            var poolTokens = pool.Select(t => ContentTokens(t.Content)).ToList();
            var curTokens = q.Current.Select(t => ContentTokens(t.Content)).ToList();
            var staTokens = q.Stale.Select(t => ContentTokens(t.Content)).ToList();
            foreach (var j in overlaps)
            {
                int wc = 0, ws = 0;
                for (var i = 0; i < pool.Count; i++)
                {
                    if (curTokens.Max(f => Jaccard(poolTokens[i], f)) >= j) wc++;
                    if (staTokens.Max(f => Jaccard(poolTokens[i], f)) >= j) ws++;
                }
                jWitnessSum[j][0] += wc;
                jWitnessSum[j][1] += ws;
                jCells[j][wc > ws ? 0 : ws > wc ? 1 : wc > 0 ? 2 : 3]++;
            }

            // Detector noise: how many "witnesses" an ARBITRARY unflagged user turn collects at the middle
            // threshold. Every 7th pool turn, capped at 10 per question — deterministic, no RNG, because a
            // seeded shuffle buys nothing a stride does not.
            for (int i = 0, taken = 0; i < pool.Count && taken < 10; i += 7, taken++)
            {
                noiseTurns++;
                for (var k = 0; k < pool.Count; k++)
                    if (k != i && Cosine(poolVecs[i], poolVecs[k]) >= 0.70) noiseWitnesses++;
            }
        }

        static double Percentile(List<double> xs, double p)
        {
            if (xs.Count == 0) return 0;
            var sorted = xs.OrderBy(x => x).ToList();
            return sorted[Math.Min(sorted.Count - 1, (int)(p * sorted.Count))];
        }

        var n = sampled.Count;
        Console.WriteLine();
        Console.WriteLine("=== witness counts by embedding cosine (whole turns, unflagged user turns only) ===");
        Console.WriteLine($"  {"θ",4} {"witnesses/q cur",16} {"witnesses/q sta",16} {"cur>sta",8} {"sta>cur",8} {"tie>0",6} {"both-0",7} {"gate cur",9} {"gate sta",9}");
        foreach (var th in thetas)
            Console.WriteLine($"  {th,4:0.00} {(double)witnessSum[th][0] / n,16:0.00} {(double)witnessSum[th][1] / n,16:0.00} "
                + $"{cells[th][0],8} {cells[th][1],8} {cells[th][2],6} {cells[th][3],7} "
                + $"{$"{(double)gatePass[th][0] / n:P0}",9} {$"{(double)gatePass[th][1] / n:P0}",9}");
        Console.WriteLine();
        Console.WriteLine("=== witness counts by content-token Jaccard (model-free) ===");
        Console.WriteLine($"  {"≥",4} {"witnesses/q cur",16} {"witnesses/q sta",16} {"cur>sta",8} {"sta>cur",8} {"tie>0",6} {"both-0",7}");
        foreach (var j in overlaps)
            Console.WriteLine($"  {j,4:0.00} {(double)jWitnessSum[j][0] / n,16:0.00} {(double)jWitnessSum[j][1] / n,16:0.00} "
                + $"{jCells[j][0],8} {jCells[j][1],8} {jCells[j][2],6} {jCells[j][3],7}");
        Console.WriteLine();
        Console.WriteLine("=== context the counts are unreadable without ===");
        Console.WriteLine($"  best-witness similarity, CURRENT fact:    p50 {Percentile(maxSimCur, 0.5):0.000}   p90 {Percentile(maxSimCur, 0.9):0.000}");
        Console.WriteLine($"  best-witness similarity, SUPERSEDED fact: p50 {Percentile(maxSimSta, 0.5):0.000}   p90 {Percentile(maxSimSta, 0.9):0.000}");
        Console.WriteLine($"  detector noise at θ=0.70: an arbitrary unflagged user turn collects "
            + $"{(noiseTurns == 0 ? 0 : (double)noiseWitnesses / noiseTurns):0.00} witnesses ({noiseTurns} probes)");
        Console.WriteLine();
        Console.WriteLine("  READ THE PAIRED CELLS against the judge's: 5 correct / 23 backwards / 35 flat");
        Console.WriteLine("  (docs/memory-measurements.md, D109). 'gate' is the field's write-persistence rule");
        Console.WriteLine("  (≥3 assertions across ≥2 sessions): a CURRENT fact that cannot pass it would never");
        Console.WriteLine("  have persisted, which prices the gate as a filter rather than as a preference.");
        return 0;
    }

    private static HashSet<string> ContentTokens(string text)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && char.IsLetterOrDigit(text[i])) { if (start < 0) start = i; continue; }
            if (start >= 0 && i - start >= 3) tokens.Add(text[start..i].ToLowerInvariant());
            start = -1;
        }
        return tokens;
    }

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var intersection = a.Count(b.Contains);
        return (double)intersection / (a.Count + b.Count - intersection);
    }

    private static int FirstIndexOf(List<string> got, IReadOnlyList<Turn> wanted)
    {
        for (var i = 0; i < got.Count; i++)
            if (wanted.Any(t => got[i].Contains(t.Tag, StringComparison.Ordinal)))
                return i;
        return -1;
    }

    private static async Task<IEnumerable<string>> TopKAsync(
        IModelProvider vectorProvider, List<(string Text, float[] Vector)> index, string query, int k)
    {
        var q = await vectorProvider.EmbedAsync(query);
        return index.Select(e => (e.Text, Score: Cosine(q, e.Vector)))
            .OrderByDescending(e => e.Score).Take(k).Select(e => e.Text);
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    /// <summary>One class, and for knowledge-update only the questions whose evidence spans two dated
    /// sessions — the discriminating shape. Distractor sessions are loaded like any other: they are what the
    /// haystack variant is for.</summary>
    private static List<Question> Load(string path, string wantType, bool temporal)
    {
        // Bytes, not text: the haystack file is 277 MB, and reading it as a string doubles that before the
        // document is even built.
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path).AsMemory());
        var result = new List<Question>();

        foreach (var q in doc.RootElement.EnumerateArray())
        {
            if (q.GetProperty("question_type").GetString() != wantType) continue;

            var sessions = q.GetProperty("haystack_sessions");
            var dates = q.GetProperty("haystack_dates").EnumerateArray().Select(d => d.GetString() ?? "").ToList();
            if (sessions.GetArrayLength() < (temporal ? 1 : 2)) continue;

            // Order by the session's own date string. LongMemEval's format sorts lexicographically because
            // it is yyyy/MM/dd — which is why this needs no date parsing and no culture.
            var ordered = sessions.EnumerateArray()
                .Select((s, i) => (Session: s, Date: i < dates.Count ? dates[i] : "", Index: i))
                .OrderBy(x => x.Date, StringComparer.Ordinal)
                .ToList();

            var turns = new List<Turn>();
            foreach (var (session, _, si) in ordered)
                turns.AddRange(session.EnumerateArray().Select((t, ti) => new Turn(si, ti,
                    t.GetProperty("role").GetString() ?? "",
                    t.GetProperty("content").GetString() ?? "",
                    t.TryGetProperty("has_answer", out var h) && h.ValueKind == JsonValueKind.True)));

            // The current value sits in the latest-dated session that CARRIES a flagged turn, never simply in
            // the latest session. In the oracle the two coincide because every session is an evidence session;
            // in the haystack the last-dated session is a distractor nearly every time, so reading it would
            // find no current turn and drop the whole class. `turns` is already in date order.
            var evidence = turns.Where(t => t.HasAnswer).ToList();
            var lastEvidence = evidence.Count == 0 ? -1 : evidence[^1].Session;
            var current = evidence.Where(t => t.Session == lastEvidence).ToList();
            var stale = evidence.Where(t => t.Session != lastEvidence).ToList();
            if (temporal ? evidence.Count == 0 : current.Count == 0 || stale.Count == 0) continue;

            result.Add(new Question(q.GetProperty("question_id").GetString() ?? "",
                q.GetProperty("question").GetString() ?? "", wantType, turns, current, stale, evidence));
        }
        return result;
    }

    /// <summary>
    /// Observes one recall's candidate pool and delegates the actual ranking untouched — so the arm it runs
    /// in is the SHIPPED arm, and the numbers it prints describe the run that produced the table rather than
    /// a reconstruction of it.
    ///
    /// <para><b>Ranks are competition ranks</b> (<c>1 + |strictly better|</c>) because ties are the norm
    /// here, not the exception: every walked candidate reports <c>Relevance 0</c> with <c>Matched null</c>
    /// (<b>D97</b>), so a positional rank would depend on sort order among equals and mean nothing.</para>
    /// </summary>
    private sealed class RankProbe(IMemoryRankingPolicy inner) : IMemoryRankingPolicy
    {
        internal IReadOnlyList<Turn> Current { get; set; } = [];
        internal IReadOnlyList<Turn> Stale { get; set; } = [];
        internal List<(int Pool, int RelCur, int RelSta, int RetCur, int RetSta, double RCur, double RSta)>
            Seen { get; } = [];

        internal int[] CurrentAtK { get; } = new int[RankLadder.K.Length];
        internal int[] StaleAtK { get; } = new int[RankLadder.K.Length];
        internal int Agreed { get; private set; }
        internal int Compared { get; private set; }

        public IReadOnlyList<RankedMemory> Rank(
            IReadOnlyList<MemoryCandidate> candidates, in MemoryRankingContext context)
        {
            var result = inner.Rank(candidates, context);

            var ladder = new RankLadder(candidates);
            var pool = ladder.Pool;
            var cur = Find(pool, Current);
            var sta = Find(pool, Stale);
            if (cur is not { } c || sta is not { } s) return result;

            Seen.Add((pool.Count,
                RankLadder.RankOf(pool, x => x.Node.Relevance, c.Node.Relevance),
                RankLadder.RankOf(pool, x => x.Node.Relevance, s.Node.Relevance),
                RankLadder.RankOf(pool, x => x.Retrievability, c.Retrievability),
                RankLadder.RankOf(pool, x => x.Retrievability, s.Retrievability),
                c.Retrievability, s.Retrievability));

            for (var k = 0; k < RankLadder.K.Length; k++)
            {
                var top = ladder.TopAt(RankLadder.K[k], context.Limit);
                if (top.Any(x => Has(x, Current))) CurrentAtK[k]++;
                if (top.Any(x => Has(x, Stale))) StaleAtK[k]++;

                if (RankLadder.K[k] != RankLadder.Shipped) continue;
                Compared++;
                if (RankLadder.AgreesWithShipped(top, result, context.Limit)) Agreed++;
            }
            return result;
        }

        private static bool Has(MemoryCandidate c, IReadOnlyList<Turn> wanted)
            => wanted.Any(t => c.Node.Content.Contains(t.Tag, StringComparison.Ordinal));

        private static int Rank(IReadOnlyList<MemoryCandidate> all, Func<MemoryCandidate, double> of, double v)
            => 1 + all.Count(x => of(x) > v);

        private static MemoryCandidate? Find(IReadOnlyList<MemoryCandidate> all, IReadOnlyList<Turn> wanted)
        {
            foreach (var c in all)
                if (wanted.Any(t => c.Node.Content.Contains(t.Tag, StringComparison.Ordinal)))
                    return c;
            return null;
        }
    }

    /// <summary>What makes one run comparable to another: which variant ran, how much of the class was
    /// sampled, the seed, and a fingerprint of the sampled ids. Two runs printing the same digest asked the
    /// same questions, which is the whole basis for reading an oracle table against a haystack one.</summary>
    private static void Preamble(List<Question> sampled, int pool, int turns, bool haystack, int seed, string cls)
    {
        Console.WriteLine($"Variant:   {(haystack
            ? "haystack — the evidence sits among distractor sessions"
            : "oracle — evidence sessions only, nothing to bury")}");
        Console.WriteLine($"Questions: {sampled.Count} of {pool} {cls}   seed {seed}   sample {Digest(sampled)}");
        Console.WriteLine($"Ingested:  {turns} turns per arm, {(double)turns / sampled.Count:F0} per question   "
            + $"k = {RecallLimit}   vector backend {SweepDoubles.ServedOrRequestedModel}   model-free");
        Console.WriteLine();
    }

    /// <summary>A live count on stderr, because the haystack variant ingests ~490 turns per question and runs
    /// for the better part of an hour — long enough that a silent process is indistinguishable from a hung
    /// one. Suppressed when stderr is redirected, so a captured run's file holds the table and nothing
    /// else.</summary>
    private static void Progress(int done, int total)
    {
        if (Console.IsErrorRedirected) return;
        Console.Error.Write($"\r  ingesting: question {done}/{total}   ");
        if (done == total) Console.Error.WriteLine();
    }

    /// <summary>A stable fingerprint of the sampled ids, printed instead of the ids because the only question
    /// a reader has is whether two runs sampled the same set.</summary>
    private static string Digest(List<Question> sampled) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(',', sampled.Select(q => q.Id)))))[..12];

    /// <summary>A seeded sample of the class. The pool is sorted by question id first, and the ids are
    /// IDENTICAL in the oracle and haystack files — so one seed and count select the same questions from
    /// either, which is what lets the two tables be read against each other. Taking the whole class skips the
    /// shuffle rather than special-casing it.</summary>
    private static List<Question> Sample(List<Question> pool, int take, int seed)
    {
        var ordered = pool.OrderBy(q => q.Id, StringComparer.Ordinal).ToList();
        if (take >= ordered.Count) return ordered;

        var rng = new Random(seed);
        for (var i = ordered.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (ordered[i], ordered[j]) = (ordered[j], ordered[i]);
        }
        return ordered.Take(take).OrderBy(q => q.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>The truncation footer, empty when nothing was cut. A run that truncated and did not say so
    /// would be claiming to have embedded text it did not.</summary>
    private static string TruncationNote() =>
        SweepDoubles.OpenAiCompatibleVectorProvider.Truncated is var cut and > 0
            ? $", {cut} input(s) truncated to {SweepDoubles.OpenAiCompatibleVectorProvider.MaxInputChars} chars"
            : "";

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
