using Lyntai.Memory;

namespace Lyntai.Tests.Memory.Corpus;

/// <summary>How DISCRIMINATIVE a subject-cued attribute query is — whether its tokens reach only the cluster
/// or also incidental unrelated material.
/// <para><b>Both are real conditions and neither is the "correct" one.</b> An early draft treated the
/// overlapping form as a mistake to be fixed away, which was wrong: this store tokenizes FTS as
/// <c>trigram</c>, chosen so non-Latin text works at all, and under trigram matching almost any two texts
/// share trigrams. For CJK content there is no stopword to strip and incidental overlap is UNAVOIDABLE — so
/// the overlapping form is closer to normal usage for a large part of this library's audience, while the
/// discriminative form is the best case.</para></summary>
public enum AttributeCueKind
{
    /// <summary>The cue's only live search token is the subject, reaching exactly one cluster member. Every
    /// other member must arrive through the graph, which makes miss = <c>1 - 1/AttributeCount</c> a clean
    /// no-graph floor and the resulting number interpretable.</summary>
    Discriminative,

    /// <summary>The cue also carries ordinary words that appear in unrelated entries, so the cluster competes
    /// against incidental matches. Measures the same question under the retrieval conditions non-Latin
    /// content faces by construction.</summary>
    SharesCommonTokens,
}

/// <summary>How this corpus writes its noise entries.</summary>
public enum CorpusNoiseKind
{
    /// <summary>One fixed skeleton per language, differing only by an id token and one filler word — the
    /// shape every measurement before 2026-08-13 ran on. Models noise as SEMANTICALLY IRRELEVANT: it is
    /// never a right answer to any query.</summary>
    Templated,

    /// <summary>Near-skeletonless junk drawn from <see cref="CorpusLexicon.NoiseVocabulary"/> — models noise
    /// as TEXTUALLY DIVERSE, which is a different thing and the one a novelty-driven salience policy is
    /// sensitive to. <c>StructuralSaliencePolicy</c> is monotone in "unlike anything already stored", so
    /// under <see cref="Templated"/> the second noise entry onward reads as FAMILIAR and the policy's
    /// suspected failure mode is unreachable by construction.</summary>
    Diverse,
}

/// <summary>
/// The four swept parameters that shape a generated <see cref="MemoryCorpus"/>. Each has a single,
/// undivided job so that sweeping one changes exactly the thing its name says and nothing else:
/// <list type="bullet">
/// <item><see cref="ReuseRatio"/> — how many times each topical or hot-ephemeral entry is queried while
/// still current. Reuse is what reinforcement needs to outrun interference, so this is the knob a ranking
/// sweep is actually about (mirrors the "used every round" durable facts in
/// <see cref="Lyntai.Tests.Memory.MemoryDecaySimulationTests"/>).</item>
/// <item><see cref="NoiseDensity"/> — how many standalone noise entries (mentioned once, relevant to
/// nothing) the corpus carries.</item>
/// <item><see cref="CriticalRarity"/> — how RARE the critical-rare class is. Raising it means FEWER
/// critical entries share a fixed budget, not more — it is a rarity dial, not a count.</item>
/// <item><see cref="CandidateCount"/> — how many topical entries populate the corpus: the competing pool a
/// broad recall has to rank against.</item>
/// </list>
/// </summary>
/// <param name="ReuseRatio">Recall repetitions per topical/hot-ephemeral entry while it is current.</param>
/// <param name="NoiseDensity">Count of standalone noise entries.</param>
/// <param name="CriticalRarity">Divides a fixed critical-entry budget — higher is rarer.</param>
/// <param name="CandidateCount">Count of topical entries.</param>
/// <param name="AttributeCount">How many SUBJECT-CUED ATTRIBUTE facts the corpus carries — each stated once,
/// early, then referred to repeatedly by its SUBJECT alone for the rest of the timeline. Zero disables the
/// class.
/// <para><b>The shape: the cue names the SUBJECT, and the answer is the ATTRIBUTE.</b> You refer to a thing
/// by the stable handle you always use for it, and what you need back is the detail you stated once — "my
/// wife" → <i>Alice</i>, "the deploy key" → its value, "the client" → their timezone. The motivating example
/// is the owner's: <i>"my wife is Alice — over many rounds of conversation I only mention my wife, and the
/// name should still be reminded."</i> It is one instance of the pattern, not the pattern.</para>
/// <para><b>Why this is a different question rather than a variation.</b> Every other class here queries an
/// entry by a token that entry is NAMED for, so the cue already contains the answer's own identifier. Here it
/// cannot: the query carries the subject and never the attribute, so a store that only ever matched an entry
/// against its own id would score zero on this class while scoring perfectly on the rest.</para>
/// <para><b>The facts form ONE CLUSTER about ONE persistent entity, and every query declares the WHOLE
/// cluster relevant.</b> That is the load-bearing choice. The owner's fuller statement of the case is
/// <i>"even if I don't mention my wife, this entire relationship of mine should stay relevant"</i> — the
/// conversation's subject is the PERSON continuously, and individual turns merely touch different facets. So
/// a turn that surfaces one facet must keep the rest reachable.</para>
/// <para><b>That makes this the only class here that prices the GRAPH.</b> Every other entry in this corpus
/// is independent, so co-activation edges form between things with no declared relationship and nothing ever
/// checks whether spreading activation reached material the query never lexically matched — the graph's
/// entire reason for existing has gone unmeasured. A store with no graph can score at most
/// <c>1/AttributeCount</c> on this class by construction.</para>
/// <para><b>And it is the class that can price salience.</b> `critical-rare` tests SURVIVAL under
/// interference from a full cue asked once at the end. This tests whether a fact stays ATTACHED across a
/// long conversation — and, because a returned entry is refreshed while a dropped one only ages further,
/// whether a fact that falls out of the top-N can come BACK at all. That ratchet is precisely what "does not
/// fade away" is supposed to resist.</para></param>
/// <param name="ExpandRatio">How often a reuse query is followed by the consumer OPENING one of its relevant
/// entries (<see cref="CorpusExpand"/>): one expansion every <c>ExpandRatio</c> such queries, or none at
/// <c>0</c>.
/// <para><b>Defaults to <c>0</c>, and that default is load-bearing.</b> Every corpus generated before this
/// axis existed must stay byte-identical, or every published measurement moves at once and none of them
/// would be comparable across the boundary. A study that wants expansions opts in explicitly.</para></param>
/// <param name="RoutineCount">How many RECURRING entries the corpus carries — material that is individually
/// low-value and collectively the answer to a frequency question. <c>0</c> (the default) disables the class
/// and leaves every corpus byte-identical.
/// <para><b>Split into two REGIMES, phase A always the larger.</b> B is derived and FLOORED at 1
/// (<c>Math.Max(1, RoutineCount / 3)</c>), and A is the remainder. Values below 3 cannot honour "A is
/// larger" at all (1 inverts to all-B, 2 can only tie) and are refused. The final routine query's answer is
/// <b>B</b> — so a generalisation built on support count alone is not merely imprecise here, it is
/// confidently wrong, and the A entries it returns are scored as pollution. That is the whole reason this
/// class exists rather than a simple "repeated material" one: "usually" is a claim about a RECENT
/// frequency, and a total is not an answer.</para></param>
/// <param name="RoutineSupport">How many routine entries constitute an answer to the frequency question,
/// passed through as <see cref="CorpusQuery.SupportNeeded"/>. It is a MODELLING CHOICE with no principled
/// value hiding in the data — deriving it from the mechanism under test would be circular.
/// <para><b>Below 2 is refused while the class is on</b>, for the reason <paramref name="RoutineCount"/>
/// refuses 1 and 2: those are not smaller settings, they are different metrics. Zero or less IS all-of
/// scoring — the strict branch this class exists to escape — and 1 is any-of, under which a single
/// phase-B hit scores a perfect answer, the reading <see cref="RecallQuality.Measure"/> is written to
/// reject.</para>
/// <para>At the top end it CLAMPS instead of failing: a value at or above the smaller regime's own size
/// scores that query by all-of again, because <c>Measure</c> clamps to the relevant set. That is
/// legitimate rather than a bug — phase B floors at 1 entry, so refusing it would outlaw the smallest
/// legal <paramref name="RoutineCount"/>s — and it means an arm can silently stop exercising the n-of
/// branch, which is why the golden shape picks a count where it does not.</para></param>
public readonly record struct CorpusShape(
    int ReuseRatio, int NoiseDensity, int CriticalRarity, int CandidateCount, int ExpandRatio = 0,
    int AttributeCount = 0, AttributeCueKind AttributeCue = AttributeCueKind.Discriminative,
    CorpusLanguage Language = CorpusLanguage.English, int AuthoritativeCount = 0,
    CorpusNoiseKind NoiseKind = CorpusNoiseKind.Templated, int HeadlineOnlyCount = 0,
    int RoutineCount = 0, int RoutineSupport = 3)
{
    /// <summary>A middling shape: small enough to run in CI, large enough that every class and every
    /// parameter has room to show its effect. <c>ExpandRatio</c> stays <c>0</c> here so the default shape —
    /// which every existing measurement is pinned against — is unchanged.</summary>
    public static CorpusShape Default { get; } = new(ReuseRatio: 4, NoiseDensity: 8, CriticalRarity: 6, CandidateCount: 10);
}

/// <summary>One step in a <see cref="MemoryCorpus"/>'s timeline — a <see cref="CorpusWrite"/>, a
/// <see cref="CorpusQuery"/>, or a <see cref="CorpusExpand"/>. Sealed to exactly those three so a consumer's
/// pattern match is exhaustive.
/// <para><b><see cref="CorpusExpand"/> was added 2026-08-12 and is OPT-IN</b>
/// (<see cref="CorpusShape.ExpandRatio"/> defaults to <c>0</c>), so every corpus generated before it existed
/// is byte-identical and no published measurement moves. A pattern match that predates it therefore keeps
/// working on every existing shape — but will silently skip expansions on a shape that has them, which is
/// why the switches in the sweeps were made exhaustive rather than left with a two-case default.</para>
/// </summary>
public abstract record CorpusStep;

/// <summary>A write step: perform <see cref="Write"/> before advancing to the next step.</summary>
public sealed record CorpusWrite(MemoryWrite Write) : CorpusStep;

/// <summary>
/// A query step: a recall to issue, and the entry ids declared relevant to it AS OF THIS POINT IN THE
/// TIMELINE — not globally. This is the corpus's ground truth. Later work compares an engine's actual
/// recall, taken at this exact step, against this set; it is never inferred from what the engine returns.
/// <para>The same entry id can be relevant to an earlier query step and absent from a later one — that is
/// how the hot-ephemeral class's closing window is expressed. There is no single, time-independent answer
/// to "is entry X relevant"; the answer is a function of WHERE in the timeline you ask.</para>
/// </summary>
/// <param name="Text">The recall query text.</param>
/// <param name="RelevantIds">The entry ids relevant to this query, as of this step. Empty is a legitimate
/// value — for example a hot-ephemeral entry looked up again after its window has closed.</param>
/// <param name="SupportNeeded">How many of <paramref name="RelevantIds"/> constitute an answer. <c>0</c> —
/// every existing class — means all of them. Non-zero only for a query whose truth is a frequency; see
/// <see cref="RecallQuality.Measure"/>.</param>
public sealed record CorpusQuery(string Text, IReadOnlyList<string> RelevantIds, int SupportNeeded = 0) : CorpusStep;

/// <summary>
/// An expansion step: the consumer OPENED this entry after seeing it in a recall — the deliberate act
/// <c>GraphMemoryEngine.ExpandAsync</c> represents, as opposed to the engine merely having returned a
/// headline.
///
/// <para><b>Why this class exists (2026-08-12, <c>TASKS.md</c> Part 64).</b> This engine reinforces
/// everything a recall returned, which is the ranker's own opinion rather than evidence of usefulness —
/// measured as net-harmful to recall quality. The proposed fix is to reinforce on EXPANSION instead, and
/// until this step existed the corpus could not express the act, so the fix was unmeasurable. Every
/// measurement taken before this date exercised reinforcement-on-recall only.</para>
///
/// <para><b><see cref="EntryId"/> is always an entry genuinely relevant to the query it follows, and that is
/// deliberate: this is a perfect usefulness ORACLE.</b> A real consumer's expansions are noisier, so an arm
/// that reinforces on expansion here receives a better signal than any deployment would. That makes its
/// result an UPPER BOUND on what the fix can buy — which is exactly the useful shape: if a perfect signal
/// does not help, the fix is dead without needing a realistic one; if it helps a lot, building a real signal
/// is worth the work.</para>
///
/// <para><b>Expanding does not require the engine to have returned it.</b> <c>ExpandAsync</c> takes a
/// reference, not a recall result, so this step is a pure function of the timeline and stays deterministic —
/// the corpus never has to know what the engine actually recalled, which is what keeps it an instrument
/// rather than a simulation.</para>
/// </summary>
/// <param name="EntryId">The corpus id of the entry opened — relevant to the query this follows.</param>
public sealed record CorpusExpand(string EntryId) : CorpusStep;

/// <summary>
/// A deterministic, seeded corpus of four declared classes — critical-rare, hot-ephemeral, topical, noise —
/// each with a stated ground truth, so recall quality can be measured against a known answer instead of by
/// inspection.
/// <para><b>REFRAMED 2026-08-10 — this corpus is an INSTRUMENT, not a simulation.</b> An earlier version
/// targeted plausible-looking timing and, as a result, compared the two shipped forgetting curves almost
/// entirely where they AGREE: four of six sweep shapes sat at <c>age/S ≤ 1.2</c> (a ~7% difference, by
/// construction), critical-rare — the deciding class — carried only 2-4 independent targets per cell, reuse
/// repeats fired back-to-back with no interposed write (so a printed N of 159 was worth ~24 independent
/// draws), and hot-ephemeral's in-window queries fired so close to their own write that no policy could ever
/// register a miss there. A corpus that cannot express a difference measures nothing, however lifelike its
/// timeline reads — so generation here is now deliberately tuned to the region the two curves shipped at the
/// time (<c>DsrRetrievability</c> and <c>HalfLifeRetrievability</c>, the latter deleted in 3.0 —
/// <c>docs/DECISIONS.md</c>) actually diverged in (<c>age/InitialStability</c> in <b>[1.5, 5]</b>, where they
/// moved apart by 2x or more, rather than the ~7% they differed by near 1), never at the <c>age = 0</c>
/// boundary (retrievability pinned at a perfect 1.0 for EITHER curve) nor so far out that both curves had
/// collapsed to the same near-zero floor. The band remains useful for any FUTURE curve comparison this
/// corpus is asked to discriminate (for instance <c>DsrRetrievability</c> variants), not only the one it was
/// tuned for. A result measured on this corpus describes behaviour in that DISCRIMINATING regime —
/// sensitivity, not realism — which is not the same claim as a result measured against a production corpus,
/// and any report built on it must say so.</para>
/// <para><b>The ordering contract, which every consumer of <see cref="Steps"/> MUST honour:</b> replay the
/// sequence IN ORDER, one step at a time, against a single live engine. Correct usage looks like this:
/// <code>
/// foreach (var step in corpus.Steps)
/// {
///     switch (step)
///     {
///         case CorpusWrite w:
///             await engine.RememberAsync(w.Write);
///             break;
///         case CorpusQuery q:
///             var recall = await engine.RecallAsync(new MemoryQuery(taskKey, scope, q.Text));
///             Measure(recall, q.RelevantIds); // THIS step's relevant set, never some other step's
///             break;
///     }
/// }
/// </code>
/// Replaying out of order, or in two phases (every write, then every query) invalidates every number
/// downstream — the hot-ephemeral class's window and the critical-rare class's "late lookup" both depend on
/// interference that only exists if the intervening writes actually happened first. A write-then-query
/// harness would still run and still print a table; it would just be measuring a different, easier corpus
/// than the one this type declares. There is deliberately no <c>Writes</c>/<c>Queries</c> shortcut property
/// on this type — a call site that genuinely only needs a count or a content check derives it ad hoc with
/// <c>Steps.OfType&lt;CorpusWrite&gt;()</c> / <c>Steps.OfType&lt;CorpusQuery&gt;()</c>, so the ordering
/// context travels with every use instead of living in a comment next to a property that invites consuming
/// the whole collection at once.</para>
/// <para>Every QUERIED entry's content starts with the shared term "item" (mirroring
/// <see cref="Lyntai.Tests.Memory.MemoryDecaySimulationTests"/>'s convention) so a broad recall makes them
/// genuinely COMPETE — without that shared term nothing here would be measuring anything, because nothing
/// would be fighting for the same ranked slots.</para>
/// <para><b>A fifth entry class — FILLER (<c>"padding filler{n}"</c>) — deliberately does NOT share it, and
/// that exception is load-bearing.</b> Filler exists purely to interpose real writes between a target's own
/// write and its own reuse query when nothing else in the corpus would (see <see cref="TopUpTo"/>) — it is
/// the instrument's own padding, not a member of the population being measured. It began life as
/// <c>"item filler{n}"</c> and therefore did both jobs at once: <see cref="Lyntai.Storage.FtsQuery.Build"/>
/// OR-joins a query's tokens, so a filler written moments earlier (retrievability ≈ 1) was a perfectly legal
/// candidate for a query it has nothing to do with, and out-scored already-decayed but genuinely relevant
/// targets under any ranking that rewards freshness. The measured consequence was that a handful of early
/// <c>topic*</c>/<c>hot*</c> entries were never recalled for ANY of their own relevant queries — so
/// <c>IMemoryRetrievabilityPolicy.Reinforce</c> was never even called for them, and every number taken over
/// this corpus was partly a measurement of the padding. <b>A corpus whose own scaffolding competes with its
/// subjects is measuring the scaffolding.</b> Pinned by
/// <c>MemoryCorpusTests.No_filler_entry_can_match_any_query_in_the_corpus</c>, which checks SUBSTRING
/// containment (the SQL backends use an FTS5 <b>trigram</b> tokenizer, so token-boundary equality would pass
/// while the store still matched) over every query term of 3+ characters (<c>FtsQuery.Build</c>'s own floor).
/// Filler is still never declared relevant to anything, the same guarantee the noise class carries — that
/// was always true, and was never sufficient on its own.</para>
/// <para>Only entry CONTENT varies with the seed (via a small filler-word draw made once per entry, in a
/// fixed generation order); entry COUNTS and the timeline's structure are pure functions of
/// <see cref="CorpusShape"/>. That is deliberate: it keeps "same seed" and "same shape" independently
/// verifiable, and it means a corpus's size never becomes a hidden function of the seed. (Padding WRITE
/// COUNTS are also a pure function of shape — only the padding entries' filler WORDS draw from the seed.)
/// </para>
/// <para><b>LANGUAGE is an axis (2026-08-12).</b> <see cref="CorpusShape.Language"/> selects the
/// <see cref="CorpusLexicon"/> every template and every reader comes from; it defaults to
/// <see cref="CorpusLanguage.English"/> and is byte-identical when unset — proved by the goldens in
/// <c>MemoryCorpusGoldenTests</c> that PREDATE this axis and did not move. It was added because the class
/// doc above could describe this corpus as an instrument while every number it produced came from the
/// friendliest tokenization the library supports — and looking at that directly found a real defect in CJK
/// retrieval, not merely a favourable condition (<c>docs/DECISIONS.md</c> D55).
/// <para>Two properties make the two languages COMPARABLE rather than merely both present, and both are
/// pinned: the timelines are structurally identical (same steps, ids and ground truth — the generator's
/// control flow reads only the shape and the seed, and both filler lists are the same LENGTH so the PRNG
/// advances in lockstep), and Chinese content is one spaceless run apart from its ASCII id, so retrieval
/// genuinely goes through trigram expansion instead of quietly re-testing whitespace splitting. Adding a
/// third language means honouring both.</para></para>
/// </summary>
/// <param name="Steps">The ordered timeline — writes and queries interleaved. See the ordering contract
/// above.</param>
public sealed record MemoryCorpus(IReadOnlyList<CorpusStep> Steps)
{
    private const string TaskKey = "corpus";
    private const string Scope = "sim";

    // DsrOptions.InitialStability defaults to 20 (as did the deleted HalfLifeOptions.InitialStability, at
    // the time both existed) — the corpus does not import Lyntai.Memory.Forgetting (it stays pure data, no
    // policy dependency), so this is a DOCUMENTED ASSUMPTION rather than a derived constant, exactly like
    // every "age/InitialStability" ratio already quoted in this file's own history. Every delay constant below
    // is defined as an explicit multiple of it so the multiple — the thing the falsification plan's Task 1
    // brief specifies verbatim — is legible at the call site instead of buried in a bare integer.
    private const int AssumedInitialStability = 20;

    // A fixed critical-entry budget that CriticalRarity DIVIDES. Rarity is a ratio, not a count in its own
    // right, so the budget itself never moves — only how many entries share it. RAISED from 12 to 240
    // (2026-08-10, falsification plan Task 1 Step 2 / TASKS.md Part 55): critical-rare is the DECIDING class
    // for the curve question and the previous budget gave it only 2-4 independent targets per cell, so a
    // single entry flipping moved a cell by 0.25-0.5. 240 / 12 (CriticalRarity at its rarest named setting,
    // "rare-critical" in bench/Lyntai.Benchmarks/MemoryPolicySweep.cs) = 20, clearing the "~20+ independent
    // targets" floor the brief asks for; every LESS rare setting clears it by a wider margin. A future shape
    // rarer than 12 must raise this further to keep the same guarantee — see
    // MemoryCorpusTests.Critical_rare_clears_its_independent_target_floor_at_its_rarest_named_setting.
    private const int CriticalBudget = 240;

    // Not swept: enough rounds that "the window closes" is observable without tying that observation to
    // any of the four swept parameters.
    private const int HotRounds = 5;

    // How many rounds must pass before an earlier round's hot entry is looked up again and found NOT
    // relevant. Fixed and smaller than HotRounds, so every shape gets at least one closed window. Paired
    // with a due-count check in the force-drain block below (2026-08-10 fix) so it no longer BYPASSES
    // HotReuseDelayWrites the way it used to — see that block's own comment.
    private const int HotWindowRounds = 2;

    // = 1.5 x AssumedInitialStability — the falsification plan's own DISCRIMINATING-BAND FLOOR, used
    // verbatim. RAISED from 6 (2026-08-10, Task 1 Step 1/3 / TASKS.md Part 55): 6 was age/S=0.3, and even
    // that was routinely BYPASSED by the force-drain block below reaching the queue before this many writes
    // had actually interposed — measured (old code, old constant), every shape but "high-noise" fired its
    // hot-ephemeral in-window queries at age 1-3, unmissable by either policy. GUARANTEED now, not merely
    // scheduled: the force-drain block below tops up with filler writes (see TopUpTo) whenever a shape's own
    // per-round write budget would otherwise reach this round before enough real writes have interposed, so
    // every shape — not just the widest one — reaches this floor exactly, never less.
    private const int HotReuseDelayWrites = 30;

    // = 5 x AssumedInitialStability — the falsification plan's own DISCRIMINATING-BAND CEILING, used
    // verbatim. REPLACES the previous `Math.Max(MinTopicalReuseDelayWrites, middleWriteBudget / 2)` formula
    // (2026-08-10, Task 1 Step 1): that formula SCALED with shape size, which is exactly how four of the
    // six sweep shapes ended up at age/S≤1.2 (a small shape's own middle-write-budget/2 rarely reached far
    // into the band) — a corpus built to look proportionate to its own shape, not to discriminate. FIXED and
    // GUARANTEED instead: every shape reaches this exact floor via natural interposition when the shape is
    // wide enough, and via filler top-up (TopUpTo) when it is not, so "how big is this shape" no longer
    // decides "how hard is this shape's own curve question."
    private const int TopicalReuseDelayWrites = 100;

    // = 3 x AssumedInitialStability — the discriminating band's MIDPOINT, used as a FLOOR under
    // critical-rare's own age (2026-08-10, Task 1 Step 1). Critical-rare's write happens early and its
    // ground-truth query is appended after every other write in the corpus, so its natural age is already
    // "the whole rest of the corpus" — usually well past this floor on its own once topical/hot-ephemeral's
    // own delays above have run (both exceed it).
    //
    // HONESTLY DISCLOSED, not merely asserted (mutation-checked, 2026-08-10): on the CURRENT 60-shape grid,
    // HotRounds' own 5 rounds are unconditional — never gated by CandidateCount — so their own
    // HotReuseDelayWrites top-up already clears this floor before this constant's own TopUpTo call ever
    // needs to add anything; removing that call does not fail
    // MemoryCorpusTests.Critical_rare_queries_reach_the_discriminating_bands_midpoint on this grid today.
    // Kept anyway as a DIRECT, independent guarantee — belt and braces against a future change to
    // HotRounds/HotReuseDelayWrites (either of which could shrink hot-ephemeral's own contribution) rather
    // than leaving critical-rare's own floor to depend silently on a neighbour class's constants.
    private const int CriticalRareFloorWrites = 60;

    // The filler-word list moved to CorpusLexicon with every other template — it is drawn once per entry
    // from the seeded PRNG and is the ONLY seed-dependent part of an entry's content, in either language.

    /// <summary>Generates a corpus for <paramref name="shape"/> using an explicitly seeded PRNG — never
    /// <see cref="Random.Shared"/>, an unseeded <see cref="Random"/>, or the clock. The same
    /// (shape, seed) pair always produces byte-identical content; a different seed almost certainly
    /// produces different content (the filler draw diverges from the first entry onward).</summary>
    public static MemoryCorpus Generate(in CorpusShape shape, int seed)
    {
        var rng = new Random(seed);
        var steps = new List<CorpusStep>();
        var writesSoFar = 0;
        var fillerEmitted = 0;
        var reuseQueriesFired = 0;   // drives ExpandRatio; see FireReuseBatch

        var reuse = Math.Max(1, shape.ReuseRatio);
        // hoisted out of `shape` like every other dial here — `shape` is an `in` parameter and so cannot be
        // captured by FireReuseBatch below
        var expandRatio = Math.Max(0, shape.ExpandRatio);
        var attributeCount = Math.Clamp(shape.AttributeCount, 0, 3);   // three distinct subjects are defined
        var authoritativeCount = Math.Max(0, shape.AuthoritativeCount);
        var headlineOnlyCount = Math.Max(0, shape.HeadlineOnlyCount);
        var routineCount = Math.Max(0, shape.RoutineCount);
        // NOT clamped, unlike every dial above it: clamping a nonzero support into range would turn an
        // out-of-range value into a DIFFERENT metric (0 is all-of, 1 is any-of) and report it as a result.
        var routineSupport = shape.RoutineSupport;
        // A count below 3 cannot honour "phase A is the larger regime" at all: floor(1/3)=0 forces the
        // 1-entry case to be ALL phase B (inverted), and 2 can only split 1/1 (a tie, not a majority). Refused
        // outright — a fixture bug reported as a system result is worse than a fixture that fails loudly. See
        // this class's own CorpusShape.RoutineCount doc for why A must be larger.
        if (routineCount is > 0 and < 3)
            throw new ArgumentException(
                $"{nameof(CorpusShape.RoutineCount)} must be 0 or >= 3 — a smaller nonzero value cannot keep "
                + $"phase A the larger regime (was {routineCount}).", nameof(shape));
        if (routineCount > 0 && routineSupport < 2)
            throw new ArgumentException(
                $"{nameof(CorpusShape.RoutineSupport)} must be >= 2 while the routine class is on — 0 or "
                + $"less is all-of scoring and 1 is any-of, and neither of those is a frequency (was "
                + $"{routineSupport}).", nameof(shape));
        // Derive B and FLOOR it, rather than deriving A directly: A = count - B keeps A the larger share for
        // EVERY legal count, including 4 — `count * 2 / 3` (the formula this replaces) gave 4 an even 2/2
        // split, a silent tie a hand-picked golden shape (9, an exact multiple of 3) never exercised.
        var routineBCount = routineCount == 0 ? 0 : Math.Max(1, routineCount / 3);
        var routineACount = routineCount - routineBCount;
        var attributeCue = shape.AttributeCue;
        // every template AND every reader for this corpus's own invariants — see CorpusLexicon. Hoisted out
        // of `shape` like every other dial here, because `shape` is an `in` parameter and cannot be captured.
        var lex = CorpusLexicon.For(shape.Language);
        var noiseKind = shape.NoiseKind;
        var noiseCount = Math.Max(1, shape.NoiseDensity);
        var candidateCount = Math.Max(0, shape.CandidateCount);
        var criticalCount = Math.Max(1, CriticalBudget / Math.Max(1, shape.CriticalRarity));

        void Write(string content, MemoryGrade grade = MemoryGrade.Inherit, string? headline = null)
        {
            steps.Add(new CorpusWrite(new MemoryWrite(TaskKey, Scope, content, Headline: headline, Grade: grade)));
            writesSoFar++;
        }

        void Query(string text, params string[] relevant) => steps.Add(new CorpusQuery(text, relevant));

        // The consumer OPENING an entry it just saw — the deliberate act ExpandAsync represents, as opposed
        // to the engine merely having returned a headline. Always a genuinely relevant id, which makes it a
        // perfect usefulness ORACLE; see CorpusExpand's own doc for why that upper-bound framing is the
        // useful one rather than a flaw.
        void Expand(string entryId) => steps.Add(new CorpusExpand(entryId));

        // A dedicated, never-queried write class whose ONLY job is to interpose real writes when nothing
        // else in the corpus would — never counted against NoiseDensity (that dial has its own exact-count
        // contract, MemoryCorpusTests.Raising_NoiseDensity_produces_more_noise_entries) and never declared
        // relevant to anything (MemoryCorpusTests.Filler_entries_are_never_declared_relevant_to_any_query).
        // NOT "item …", unlike every other write class here, and that difference is the whole point — see
        // this type's own class doc. Filler exists to interpose writes, never to compete for a ranked slot,
        // and while it began with the shared "item" token it did BOTH: FtsQuery.Build OR-joins a query's
        // tokens, so every freshly-written filler was a live candidate (retrievability ≈ 1) for every query
        // in the corpus. The leading token is the ONLY thing that changed; the id stays the second token, so
        // every consumer's `content.Split(' ')[1]` still reads it.
        void WriteFiller()
        {
            Write(lex.Padding($"filler{fillerEmitted}", Filler(lex, rng)));
            fillerEmitted++;
        }

        // GUARANTEES a target write count is reached — by padding with filler writes — rather than merely
        // scheduling it and hoping the rest of the corpus gets there first. This is what turns
        // HotReuseDelayWrites/TopicalReuseDelayWrites/CriticalRareFloorWrites from ASPIRATIONS (what the old
        // formula scaled toward) into GUARANTEES (what every shape, including the property grid's smallest,
        // actually reaches) — a no-op whenever natural interference already cleared the target.
        void TopUpTo(int targetWriteCount)
        {
            while (writesSoFar < targetWriteCount) WriteFiller();
        }

        // Fires a repeated reuse batch, interposing ONE filler write between consecutive repeats
        // (2026-08-10, Task 1 Step 2 / TASKS.md Part 55). Before this fix, a batch's `reuse` repeats fired
        // back-to-back with nothing interposed, so they were CORRELATED draws of the same retrieval decision
        // rather than independent ones — a printed N of, say, 40 carried the granularity of 40/ReuseRatio
        // independent targets, not 40. Interposing a real write between repeats means the corpus state
        // genuinely differs from one repeat to the next, so N means N. The FIRST repeat (k=0) gets no filler
        // before it — it fires at exactly the batch's own scheduled age, unchanged.
        // ExpandRatio (2026-08-12): one expansion every N reuse queries, counted ACROSS batches rather than
        // within one, so a shape whose ReuseRatio is smaller than ExpandRatio still produces expansions
        // instead of silently producing none. Emitted AFTER the query it follows, which is the real order —
        // a consumer opens an entry it has just seen listed.
        void FireReuseBatch(Func<int, string> textAt, string relevantId, int count)
        {
            for (var k = 0; k < count; k++)
            {
                if (k > 0) WriteFiller();
                Query(textAt(k), relevantId);
                reuseQueriesFired++;
                if (expandRatio > 0 && reuseQueriesFired % expandRatio == 0) Expand(relevantId);
            }
        }

        // SUBJECT-CUED ATTRIBUTES: stated once, EARLY, then referred to by SUBJECT alone for the rest of the
        // timeline. Written before critical-rare so they sit at the very start, which is what makes the later
        // subject queries a genuine long-range retrieval rather than a fresh one.
        //
        // The content carries BOTH the subject and the attribute; the query below carries ONLY the subject.
        // The cue can therefore never contain its own answer — the property that separates this class from
        // every other one here.
        //
        // Three DIFFERENT kinds of subject on purpose (a person, a credential, a party), because the pattern
        // is "a stable handle you keep using, and the detail you stated once" — of which "my wife is Alice"
        // is one instance rather than the definition. Single-token subjects so the cue is one clean term.
        var attributeSubjects = lex.AttributeSubjects;
        var attributeValues = lex.AttributeValues;
        var attributeIds = new List<(string Id, string Subject)>(attributeCount);
        for (var i = 0; i < attributeCount; i++)
        {
            var id = $"attribute{i}";
            var subject = attributeSubjects[i % attributeSubjects.Count];
            var value = attributeValues[i % attributeValues.Count];
            attributeIds.Add((id, subject));
            Write(lex.Attribute(id, subject, value, Filler(lex, rng)));
        }

        // AUTHORITATIVE: the corpus's only material written at a GRADE, and the only class whose ground truth
        // has no acceptable failure rate — design §5.7.0's objective (1), "never lose an authoritative fact".
        //
        // Until this existed the corpus held ZERO MemoryGrade references, so the engine's highest-priority
        // promise was structurally unmeasurable here: every number this instrument ever produced was about
        // objectives (2) and (3). A null result on objective (1) meant "not exercised", never "kept".
        //
        // Written FIRST so they are the oldest material in the timeline — an entry that never decays must be
        // shown not decaying, and the way to show that is to bury it under everything else. Their probe query
        // is emitted at the very end and deliberately matches NONE of them, so the only thing that can return
        // one is the grade carve-out. That is the promise, stated as a measurement rather than an assertion.
        var authoritativeIds = new List<string>(authoritativeCount);
        for (var i = 0; i < authoritativeCount; i++)
        {
            var id = $"authoritative{i}";
            authoritativeIds.Add(id);
            Write(lex.Authoritative(id, Filler(lex, rng)), MemoryGrade.Authoritative);
        }

        // HEADLINE-ONLY: the marker lives in the AUTHORED headline and appears nowhere in the content, so
        // the probe at the end of this method can only be answered by searching headlines. Every other class
        // lets the engine derive its headline from the content, which makes headline words a subset of
        // content words — and therefore makes headline search unobservable. The 3.0 review narrowed it and
        // widened it back and `memory-sweep` saw neither direction.
        //
        // Opt-in and 0 by default, exactly as AuthoritativeCount is, so every existing corpus is
        // byte-identical and no published measurement moves.
        var headlineOnlyIds = new List<string>(headlineOnlyCount);
        for (var i = 0; i < headlineOnlyCount; i++)
        {
            var id = $"headline{i}";
            headlineOnlyIds.Add(id);
            // Content is PADDING: a real entry that competes for a slot, and whose template shares no term
            // with the marker. Using a queried class instead would let content matching answer the probe.
            Write(lex.Padding(id, Filler(lex, rng)), headline: $"{lex.HeadlineMarker} {id}");
        }

        // ROUTINE, PART 1 — phase A's writes, EARLY, and its own query. RoutineCount entries split into two
        // REGIMES — phase A (the larger, first share) and phase B (the smaller, last share, see the
        // routineACount/routineBCount derivation above). The frequency query fires twice: once here, once at
        // the very end (PART 2, below, beside the corpus's other end-of-timeline probes) — its answer is
        // phase B ONLY at that point, with phase A now pollution. A generalisation built on total support
        // rather than RECENCY answers that second query the wrong way. See CorpusShape.RoutineCount's own doc
        // for the full argument.
        //
        // Split across two places in this method — rather than one contained block, as the first version of
        // this class was — because its two queries need very different ages. This one only needs to clear
        // the discriminating band's own FLOOR (HotReuseDelayWrites) so it is never the age-zero lookup
        // MemoryCorpusTests.No_reuse_query_occurs_at_age_zero forbids; the FINAL query needs phase A aged deep
        // into the band while phase B stays fresh, which is a property of WHERE in the timeline phase B's
        // writes and that query sit, not of this one.
        //
        // Guarded behind RoutineCount > 0, exactly like AuthoritativeCount/HeadlineOnlyCount, so the default
        // corpus emits nothing and stays byte-identical.
        var routinePhaseAIds = new List<string>(routineACount);
        var routinePhaseAEndWrites = 0;
        if (routineCount > 0)
        {
            for (var i = 0; i < routineACount; i++)
            {
                var id = $"routineA{i}";
                routinePhaseAIds.Add(id);
                Write(lex.Routine(id, 0, Filler(lex, rng)));
            }

            routinePhaseAEndWrites = writesSoFar;
            TopUpTo(writesSoFar + HotReuseDelayWrites);

            // Constructed directly rather than through the Query(...) local helper: that helper's signature
            // is `(string text, params string[] relevant)`, and params must be the LAST parameter, so
            // SupportNeeded cannot be appended after it without either reordering every existing call site or
            // adding a second overload purely for this one class.
            steps.Add(new CorpusQuery(lex.RoutineQuery(), [.. routinePhaseAIds], routineSupport));
        }

        // Critical-rare: written once, EARLY. Its ground-truth query is appended at the very end of this
        // method, after every other write below — so recalling it is never a trivially fresh lookup, and
        // the gap between the write and the query IS the whole rest of the corpus.
        var criticalIds = new List<string>(criticalCount);
        for (var i = 0; i < criticalCount; i++)
        {
            var id = $"critical{i}";
            criticalIds.Add(id);
            Write(lex.Critical(id, Filler(lex, rng)));
        }

        // Topical + hot-ephemeral, interleaved unit by unit across the middle of the timeline, with noise
        // spread evenly between units as real interference — not bunched at one end. Each unit advances one
        // topical entry (write + its own `reuse` queries) and, while rounds remain, one hot-ephemeral round
        // (write + `reuse` IN-WINDOW queries). Once a round has aged HotWindowRounds rounds behind the
        // current one, it is looked up again and declared NOT relevant — the window closing, driven by real
        // position in the timeline rather than a label.
        //
        // REUSE QUERIES ARE DEFERRED, never emitted in the same unit as their target's own write. A query
        // fired immediately has age EXACTLY 0, and DsrRetrievability.Retrievability short-circuits
        // `Age <= 0` to a perfect 1.0 (as did the deleted HalfLifeRetrievability.Retrievability) — a target
        // at that retrievability is simultaneously the best textual match (its token is unique) and
        // unmissable by construction, so the query measures nothing.
        //
        // Both delays are now FIXED, band-targeted constants (TopicalReuseDelayWrites, HotReuseDelayWrites —
        // see their own comments), GUARANTEED by TopUpTo rather than merely scheduled — replacing the
        // previous shape-scaled formula that put most shapes near where the two curves agree instead of
        // where they diverge.
        var totalUnits = Math.Max(candidateCount, HotRounds);

        var pendingTopical = new Queue<(int DueWriteCount, string Id)>();
        var pendingHot = new Queue<(int DueWriteCount, string Id)>();

        // Per-unit order is DELIBERATE (fix round 4): a FORCE-DRAIN of the round about to go stale FIRST —
        // before this unit's own writes — then topic write, hot write, noise chunk, THEN the STALE QUERY
        // ITSELF (after this unit's writes), THEN the normal due-count drain. Three consequences, all
        // load-bearing:
        //  (F1) noise now lands AFTER this unit's own topic/hot writes rather than before, so a unit's own
        //       noise can interpose for that SAME unit's hot write. The original order had noise first,
        //       which is exactly how CandidateCount<=HotRounds shapes reached age 0 on their last round
        //       despite a nonzero NoiseDensity.
        //  (F2) force-draining a round's in-window queries BEFORE this unit's writes, then emitting that
        //       round's stale query AFTER them, means this unit's own topic+hot(+noise) writes sit BETWEEN
        //       the two — a real, measured gap. Force-draining immediately before the stale query (tried
        //       first) satisfies ordering but puts nothing between them, so
        //       A_hot_ephemeral_entrys_window_closes_only_after_real_interference — precisely the fact that
        //       exists to guard a real gap — had nothing to measure.
        //  Splitting the stale CHECK (top of the unit) from the stale QUERY (after this unit's writes) means
        //  staleRound is computed once but used twice across the unit's body, which is why it is captured up
        //  front rather than recomputed.
        var noiseEmitted = 0;
        for (var unit = 0; unit < totalUnits; unit++)
        {
            // One subject-cued query per fact per unit — "I mentioned my wife again". Placed at the TOP of
            // the unit, before this unit's writes, so each is asked against everything accumulated so far and
            // never against material written moments earlier.
            //
            // The query names ONE subject and nothing else: no id, no attribute value. But the ground truth
            // declares the WHOLE CLUSTER relevant, and that is the load-bearing modelling choice in this
            // class — stated here rather than buried, because it is an assertion about what memory is for
            // rather than a mechanical detail.
            //
            // These facts all describe ONE persistent entity (the user). The claim being tested is the
            // owner's: "even if I don't mention my wife, this entire relationship of mine should stay
            // relevant." The conversation's subject is the PERSON, continuously — the individual turns just
            // touch different facets. So a turn that surfaces one facet should keep the rest reachable, which
            // is exactly the spreading activation the graph exists to provide and which no other class here
            // exercises: every other entry in this corpus is independent, so co-activation edges form between
            // things with no declared relationship and nothing ever checks whether the graph reached
            // something it did not lexically match.
            //
            // A store with no graph at all can therefore score at most 1/AttributeCount on this class by
            // construction, which is the point: it prices what the graph is FOR.
            // TWO cue forms, and BOTH are real conditions — see AttributeCueKind.
            //
            // Discriminative: "{subject} recallcue". The marker appears in no entry, so it contributes no
            // candidates and exists only to give the classifier an unambiguous key. The SUBJECT is the
            // query's single lexical hook and reaches exactly ONE cluster member, so the others can arrive
            // only through the GRAPH — which makes miss = 1 - 1/AttributeCount a clean no-graph floor.
            //
            // SharesCommonTokens: "remind me about the {subject} recallcue". FtsQuery OR-joins every token of
            // three characters or more, so "the" is a live search term that also matches unrelated entries
            // and the cluster competes against incidental hits. An earlier pass treated this as a wording
            // MISTAKE and deleted it; that was wrong. This store tokenizes FTS as trigram precisely so
            // non-Latin text works, and under trigram matching almost any two texts share trigrams — for CJK
            // content there is no stopword to strip and this contention is UNAVOIDABLE. The discriminative
            // form is the best case; this one is closer to normal usage for much of this library's audience.
            // The GAP between them is the measurement worth having.
            foreach (var (_, subject) in attributeIds)
                Query(
                    attributeCue == AttributeCueKind.Discriminative
                        ? lex.DiscriminativeCue(subject)
                        : lex.CommonTokenCue(subject),
                    [.. attributeIds.Select(a => a.Id)]);

            var staleRound = unit - HotWindowRounds;
            // staleRound < HotRounds guards against a round that never existed — once `unit` runs past
            // HotRounds this would otherwise keep computing staleRound values with no corresponding hot
            // round ever written.
            var staleRoundIsReal = staleRound >= 0 && staleRound < HotRounds;
            var staleId = staleRoundIsReal ? $"hot{staleRound}" : null;

            // Force-drain, BEFORE this unit's own writes: guarantees a round's in-window queries always
            // fire before its own stale query, regardless of how HotReuseDelayWrites is tuned or how sparse
            // a shape's per-round write budget is. Safe to check only the FRONT of the queue: due counts are
            // enqueue-time writesSoFar plus a CONSTANT delay, and writesSoFar never decreases, so due counts
            // are monotonically non-decreasing in enqueue order — if this round's entry is still pending,
            // nothing enqueued after it can have drained either, so it MUST still be at the front.
            //
            // TOP UP TO THE DUE COUNT BEFORE FIRING (2026-08-10 fix, Task 1 Step 3 / TASKS.md Part 55): the
            // ORIGINAL block dequeued and fired UNCONDITIONALLY the instant a round reached the front of the
            // queue, without checking DueWriteCount at all — bypassing HotReuseDelayWrites on every shape
            // whose own per-round write budget was thinner than the delay (measured: every shape but
            // "high-noise"). TopUpTo pads with filler writes when natural writes have not reached the due
            // count yet, so this now reaches AT LEAST HotReuseDelayWrites on every shape, not just the
            // widest one.
            if (staleId is not null && pendingHot.Count > 0 && pendingHot.Peek().Id == staleId)
            {
                var (dueWriteCount, staleQueueId) = pendingHot.Dequeue();
                TopUpTo(dueWriteCount);
                FireReuseBatch(k => lex.HotRepeat(staleQueueId, k), staleQueueId, reuse);
            }

            if (unit < candidateCount)
            {
                var id = $"topic{unit}";
                Write(lex.Topical(id, Filler(lex, rng)));
                pendingTopical.Enqueue((writesSoFar + TopicalReuseDelayWrites, id));
            }

            if (unit < HotRounds)
            {
                var id = $"hot{unit}";
                Write(lex.Hot(id, Filler(lex, rng), unit));
                pendingHot.Enqueue((writesSoFar + HotReuseDelayWrites, id));
            }

            // spread noise evenly across units (cumulative-ratio target), so interference sits THROUGHOUT
            // the middle section rather than piling up at one end of it — and LAST within the unit, so it
            // always interposes for this same unit's own topic/hot writes rather than preceding them
            var noiseTarget = totalUnits > 0 ? (long)(unit + 1) * noiseCount / totalUnits : 0;
            while (noiseEmitted < noiseTarget)
            {
                Write(NoiseEntry(lex, noiseKind, $"noise{noiseEmitted}", rng));
                noiseEmitted++;
            }

            // the stale query itself, AFTER this unit's own writes above — see the force-drain comment
            // above for why the check and the query are split across the unit's body
            if (staleId is not null)
                Query(lex.HotStale(staleId)); // no relevant ids — outside its window now

            // drain anything now due — AFTER this unit's own writes (including its noise), so the
            // interposed count already reflects everything written so far; queues are FIFO-safe here
            // because each entry's due count is its own enqueue-time writesSoFar plus a CONSTANT delay, and
            // writesSoFar never decreases. Already due, so no top-up needed here — TopUpTo would be a no-op.
            while (pendingTopical.Count > 0 && pendingTopical.Peek().DueWriteCount <= writesSoFar)
            {
                var (_, id) = pendingTopical.Dequeue();
                FireReuseBatch(k => lex.TopicalRepeat(id, k), id, reuse);
            }

            while (pendingHot.Count > 0 && pendingHot.Peek().DueWriteCount <= writesSoFar)
            {
                var (_, id) = pendingHot.Dequeue();
                FireReuseBatch(k => lex.HotRepeat(id, k), id, reuse);
            }
        }

        while (noiseEmitted < noiseCount)
        {
            Write(lex.Noise($"noise{noiseEmitted}", Filler(lex, rng)));
            noiseEmitted++;
        }

        // Flush anything whose delay never elapsed within the loop (the shape ran out of room) — TOP UP to
        // the scheduled due count (2026-08-10 fix, Task 1 Step 1/3) rather than firing at whatever partial
        // age happened to accrue. The ORIGINAL flush fired unconditionally with whatever age the loop above
        // had managed to accumulate, which is exactly how the smallest shapes in the property grid (for
        // example CandidateCount=0) ended up with topical/hot-ephemeral queries far short of the
        // discriminating band. These entries still get the LARGEST age of all once topped up, on par with
        // critical-rare's own placement — a reasonable fate for a reuse query the shape was too small to
        // schedule mid-timeline.
        while (pendingTopical.Count > 0)
        {
            var (dueWriteCount, id) = pendingTopical.Dequeue();
            TopUpTo(dueWriteCount);
            FireReuseBatch(k => lex.TopicalRepeat(id, k), id, reuse);
        }

        while (pendingHot.Count > 0)
        {
            var (dueWriteCount, id) = pendingHot.Dequeue();
            TopUpTo(dueWriteCount);
            FireReuseBatch(k => lex.HotRepeat(id, k), id, reuse);
        }

        // Critical-rare's own ground-truth queries, placed at the very END — after every topical,
        // hot-ephemeral and noise write above. TOP UP to CriticalRareFloorWrites first (2026-08-10, Task 1
        // Step 1) — see that constant's own comment for why this is a direct, independent guarantee rather
        // than one this grid's shapes currently need (hot-ephemeral's own unconditional rounds already
        // clear it today).
        TopUpTo(criticalCount + CriticalRareFloorWrites);
        foreach (var id in criticalIds)
        {
            Query(lex.CriticalLookup(id), id);
            Query(lex.CriticalRecall(id), id);
        }

        // ROUTINE, PART 2 — phase B's writes and the FINAL query, placed with the corpus's other
        // end-of-timeline probes rather than immediately after PART 1 above. TOP UP FIRST, to
        // CriticalRareFloorWrites past phase A's own last write — the same midpoint guarantee critical-rare's
        // own queries just used above, belt-and-braces for a shape too thin to reach it naturally — so phase A
        // is aged deep into the discriminating band by the time this query judges it as pollution. Phase B's
        // writes then follow immediately, so IT stays fresh: this is the one guarantee in this method that
        // targets a MINIMUM gap for one regime while keeping the other close to zero on purpose — a
        // deliberate, DOCUMENTED exception to the blanket rule
        // MemoryCorpusTests.No_reuse_query_occurs_at_age_zero states elsewhere.
        if (routineCount > 0)
        {
            TopUpTo(routinePhaseAEndWrites + CriticalRareFloorWrites);

            var routinePhaseBIds = new List<string>(routineBCount);
            for (var i = 0; i < routineBCount; i++)
            {
                var id = $"routineB{i}";
                routinePhaseBIds.Add(id);
                Write(lex.Routine(id, 1, Filler(lex, rng)));
            }

            steps.Add(new CorpusQuery(lex.RoutineQuery(), [.. routinePhaseBIds], routineSupport));
        }

        // The objective-(1) probe, LAST of all: a query that matches no authoritative entry, with every
        // authoritative id declared relevant. Nothing lexical can return them, so a hit is the grade
        // carve-out working and a miss is the library breaking its only promise with no acceptable failure
        // rate. Two probes rather than one because admission and the LIMIT are different failure modes: the
        // carve-out puts an exact fact into the seed, and the engine's Take can still cut it afterwards
        // ("buried, not cut" is a promise about ranking, never about the limit).
        if (authoritativeIds.Count > 0)
        {
            Query(lex.UnrelatedProbe(), [.. authoritativeIds]);
            Query(lex.UnrelatedProbe(), [.. authoritativeIds]);
        }

        // The headline probe, emitted LAST for the same reason the critical-rare one is: the gap between the
        // write and the query is the whole rest of the corpus, so a hit is retrieval rather than freshness.
        // The marker appears in no content anywhere, so the ONLY thing that can answer this is a search that
        // reads headlines — which is exactly the dimension the instrument was blind to.
        if (headlineOnlyIds.Count > 0) Query(lex.HeadlineMarker, [.. headlineOnlyIds]);

        return new MemoryCorpus(steps);
    }

    private static string Filler(CorpusLexicon lex, Random rng) => lex.Fillers[rng.Next(lex.Fillers.Count)];

    /// <summary>One noise entry, in whichever form the shape asked for.
    ///
    /// <para><b>Both call sites route through here on purpose.</b> Noise is emitted from two places — spread
    /// across the units, then topped up at the end — and a second copy of this branch is how one of them
    /// silently keeps writing templated junk in a diverse-noise run, which would dilute the very contrast
    /// being measured without failing anything.</para>
    ///
    /// <para><b>The <see cref="CorpusNoiseKind.Templated"/> path draws from <paramref name="rng"/> EXACTLY as
    /// it did before this method existed</b> — one <c>Filler</c> call, nothing else — so a corpus that does
    /// not ask for diverse noise is byte-identical, goldens included. The diverse path draws a different
    /// number of values, which is fine because it is a different corpus; what must not change is the
    /// default.</para></summary>
    private static string NoiseEntry(CorpusLexicon lex, CorpusNoiseKind kind, string id, Random rng)
    {
        if (kind == CorpusNoiseKind.Templated) return lex.Noise(id, Filler(lex, rng));

        // Sampled WITHOUT replacement: a repeated word inside one entry is a within-entry collision that
        // makes the entry less diverse than the vocabulary can express, which is the opposite of the point.
        var pool = lex.NoiseVocabulary;
        var picked = new List<string>(DiverseNoiseWords);
        var taken = new HashSet<int>();
        while (picked.Count < DiverseNoiseWords && taken.Count < pool.Count)
        {
            var i = rng.Next(pool.Count);
            if (taken.Add(i)) picked.Add(pool[i]);
        }
        return lex.DiverseNoise(id, picked);
    }

    /// <summary>How many vocabulary words one diverse-noise entry carries. Five out of a sixty-word pool
    /// keeps the expected shared-word count between any two entries well below one, which is what "textually
    /// diverse" has to mean for the hypothesis to be under test at all.</summary>
    private const int DiverseNoiseWords = 5;
}
