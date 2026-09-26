namespace Lyntai.Memory;

/// <summary>How the graph engine retrieves. Every value is defaulted; several are <b>unmeasured</b> and say
/// so. Every count is validated where it is SET, so none is clamped where it is read.</summary>
public sealed record GraphMemoryOptions
{
    /// <summary>How far to spread from the seed set. Three or more hops reaches most of a connected graph,
    /// which defeats the purpose. Reasoned, not measured. Zero walks nothing beyond the seeds.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Set below zero.</exception>
    public int Hops { get; init => field = MemoryOption.Require(value, 0, nameof(GraphMemoryOptions), ZeroIsOff); } = 2;

    /// <summary>The character budget an expansion falls back to when the caller passes none — the "engine's
    /// configured budget" <see cref="IExpandableMemory.ExpandAsync"/> has always promised.
    /// <para>Null (the default) means UNBOUNDED. It bounds the NEIGHBOURS only: the expanded entry's own
    /// content is always returned whole, because that is what expansion is for.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set below zero.</exception>
    public int? ExpandCharBudget { get; init => field = MemoryOption.Require(value, 0, nameof(GraphMemoryOptions), ZeroIsOff); }

    /// <summary>The retrievability below which <c>PruneAsync</c> may REMOVE an entry — "forgotten enough to
    /// delete".
    /// <para>Recall does not use it. Deleting is the only thing in this model that removes a memory, and it
    /// is always explicit; being faint hides an entry behind stronger ones
    /// (<see cref="Lyntai.Memory.Ranking.MultiplicativeRankingOptions.RelativeFloor"/>) and never removes
    /// it.</para>
    /// <para><b>A starting point, not a tuned value</b> — chosen against a synthetic corpus, never against
    /// production usage.</para></summary>
    public double MinRetrievability
    {
        get;
        init => field = MemoryOption.Require(value, MemoryOptionRange.Finite, nameof(GraphMemoryOptions), Disables);
    } = 0.05;

    /// <summary>Length cap for a DERIVED headline; an authored one is used as given, and authoritative
    /// content is never shortened at all. A derived one is cut at a space in the cap's latter half, else
    /// between characters, and ends in <c>…</c>. <b>A starting point, not a tuned value.</b></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set below one.</exception>
    public int HeadlineChars
    {
        get;
        init => field = MemoryOption.Require(value, 1, nameof(GraphMemoryOptions),
            "a headline of no characters leaves the index nothing to show.");
    } = 120;

    /// <summary>How many of the returned nodes get co-activation edges. A ten-item recall would otherwise
    /// write forty-five edges every turn. Reasoned, not measured. Zero writes none.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Set below zero.</exception>
    public int CoActivationCap { get; init => field = MemoryOption.Require(value, 0, nameof(GraphMemoryOptions), ZeroIsOff); } = 5;

    /// <summary>
    /// How many of a recall's slots <see cref="MemoryGrade.Authoritative"/> material may take. <c>null</c>
    /// (the default) means "as many as it needs" — design §5.7.0's objective (1), never lose an
    /// authoritative fact (<c>docs/DECISIONS.md</c> <b>D56</b>). <b>Always capped by the recall's own
    /// limit</b>, whatever value is set here — this option can only ever REDUCE displacement, never raise
    /// the number of items a recall returns.
    ///
    /// <para><b>What a value buys.</b> With <c>2</c> against a limit of <c>10</c>, at most two slots go to
    /// re-admitted exact facts and eight remain for ordinary hits — for a task that marks many facts
    /// authoritative and still needs ordinary recall through. <c>0</c> reserves nothing and re-breaks
    /// objective (1), which is why it is not the default.</para>
    ///
    /// <para><b>It counts EVERY authoritative candidate, not only the ones a ranking policy dropped.</b> A
    /// policy does not omit an exact fact, it RANKS it, and one the query did not match carries
    /// <c>Relevance 0</c>, so reserving only for OMITTED entries would change nothing. A reserved entry
    /// keeps the policy's own score where it produced one.</para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Set below zero.</exception>
    public int? AuthoritativeReserve { get; init => field = MemoryOption.Require(value, 0, nameof(GraphMemoryOptions), ZeroIsOff); }

    /// <summary>How many recent entries an <see cref="Lyntai.Memory.Annotation.IMemoryAnnotationPolicy"/> is
    /// shown, so a pronoun in the fact being written is resolvable ("she works at a hospital" is about
    /// nobody without them). Zero shows none, which makes every annotation a judgement on the write alone.
    /// <para>Costs one no-query seed per write, and only when an annotator is registered — no annotator, no
    /// read.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set below zero.</exception>
    public int AnnotationContext { get; init => field = MemoryOption.Require(value, 0, nameof(GraphMemoryOptions), ZeroIsOff); } = 8;

    /// <summary>How many existing entries each annotated SUBJECT links the new one to. Bounds the write:
    /// subjects × this many edges, so a fact about three entities cannot quietly become a hub. Reasoned, not
    /// measured — the constant is a starting point. Zero records subjects and links nothing.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Set below zero.</exception>
    public int AnnotationLinkK { get; init => field = MemoryOption.Require(value, 0, nameof(GraphMemoryOptions), ZeroIsOff); } = 3;

    /// <summary>How many already-used subjects an annotator is shown as REUSE candidates, most-used first.
    /// Zero shows none, which measurably degrades consistency: real models answered three facts about one
    /// person with three different-but-defensible handles, and nothing linked. Costs one grouped read per
    /// annotated write.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Set below zero.</exception>
    public int AnnotationKnownSubjects
    {
        get;
        init => field = MemoryOption.Require(value, 0, nameof(GraphMemoryOptions), ZeroIsOff);
    } = 24;

    /// <summary>Skip the annotation of a write that was owed a vector and did not get one — an embedding backend
    /// and an <see cref="IVectorStore"/> are registered and <see cref="SimilarityK"/> is above zero, and this
    /// write's embed failed or its backend was unavailable. Its <see cref="MemoryWriteResult.Ran"/> then carries
    /// neither <see cref="MemorySources.Similarity"/> nor <see cref="MemorySources.Annotation"/>, so a consumer
    /// that retries every write lacking the first pays for the annotation once, on the retry that keeps the
    /// vector. Off by default: every write is annotated whether or not it was indexed.
    /// <para>An unstated grade on a skipped write is stored as <see cref="MemoryGrade.Associative"/>, and a retry
    /// does not apply the annotator's suggestion over it. A vector the index refuses AFTER the embed succeeded
    /// is not foreseen, so that write is still annotated.</para></summary>
    public bool SkipAnnotationWithoutVector { get; init; }

    /// <summary>How many candidates to fetch per requested item. The store bounds the candidate set with
    /// plain arithmetic and the policy ranks it exactly afterwards, so a multiple above 1 is what keeps
    /// that ranking meaningful.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Set below one.</exception>
    public int CandidateMultiplier
    {
        get;
        init => field = MemoryOption.Require(value, 1, nameof(GraphMemoryOptions),
            "fewer candidates than the items asked for leaves the ranking nothing to choose between.");
    } = 4;

    /// <summary>Items returned when the query names no limit.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Set below one.</exception>
    public int DefaultLimit
    {
        get;
        init => field = MemoryOption.Require(value, 1, nameof(GraphMemoryOptions),
            "a recall whose default returns nothing is no recall; pass Limit per query to ask for less.");
    } = 10;

    /// <summary>How many near neighbours a new entry is linked to when similarity enrichment is wired (an
    /// embedding backend and an <see cref="IVectorStore"/> are registered).
    /// <b>A starting point, not a tuned value</b> — chosen against a synthetic corpus, never against
    /// production usage.
    /// <para><b>Zero disables linking, indexing and novelty together</b>: a write is not embedded, so
    /// its <see cref="MemoryWriteResult.Ran"/> never carries <see cref="MemorySources.Similarity"/> — while a
    /// recall still reports it, since there the flag means enrichment is wired.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set below zero.</exception>
    public int SimilarityK { get; init => field = MemoryOption.Require(value, 0, nameof(GraphMemoryOptions), ZeroIsOff); } = 5;

    /// <summary>Cosine similarity below which enrichment does not link. Without a floor a new entry links
    /// to its <see cref="SimilarityK"/> nearest neighbours however unrelated they are, which in a small or
    /// young graph means linking to nearly everything. <b>A starting point, not a tuned value</b> — chosen
    /// against a synthetic corpus, never against production usage.
    /// <para><b>It has a SECOND job: it is also the floor
    /// <see cref="Lyntai.Memory.Salience.SalienceContext.SimilarCount"/> counts against</b>, so the two move
    /// together and cannot be tuned apart. Raising it beyond what any pair of entries reaches — a value above
    /// <c>1</c> disables linking outright — therefore reports <c>0</c> to every registered
    /// <c>IMemorySaliencePolicy</c> as well. That is exactly the documented way to isolate
    /// <c>memory-enrichment</c>'s linking half from its novelty half (<c>docs/memory.md</c> §4), and it stays
    /// correct for <c>SalienceContext.Novelty</c>, which reads the probe's top score rather than this floor.
    /// A policy reading the COUNT, though, sees "the store resembles nothing" rather than "this signal is
    /// off" — the two are indistinguishable through that member, so turn the recipe off before trusting
    /// it.</para></summary>
    public double MinSimilarity
    {
        get;
        init => field = MemoryOption.Require(value, MemoryOptionRange.Finite, nameof(GraphMemoryOptions), Disables);
    } = 0.6;

    /// <summary>Half-life of a co-activation edge's WEIGHT — never the retrievability curve's own connection
    /// boost, which is a forgetting-curve concern and lives on <c>DsrOptions</c> instead. Without decay here,
    /// edges only ever grow: every pair that has ever co-occurred stays linked at a rising weight, the graph
    /// saturates, and spreading stops discriminating because everything reaches everything.
    /// <see cref="Lyntai.Memory.Engines.GraphMemoryEngine"/> reads this directly (<c>EffectiveEdgeWeight</c>),
    /// independent of whichever retrievability policy is registered — it is this ENGINE's own knob, not the
    /// curve's, which is why it lives here rather than on an options record a policy owns. Not
    /// <c>DsrOptions.EdgeHalfLife</c>, which decays connection STRENGTH inside the curve — same name, same
    /// default, different quantity.</summary>
    public double EdgeHalfLife
    {
        get;
        init => field = MemoryOption.Require(value, MemoryOptionRange.Finite, nameof(GraphMemoryOptions), Disables);
    } = 100;

    /// <summary>The retrievability a NEIGHBOUR must still have to be walked to. <c>0</c> — the shipped
    /// default — admits every neighbour, which is what every release through 3.0.2 did.
    ///
    /// <para><b>Why it exists.</b> <see cref="EdgeHalfLife"/> decays the EDGE; nothing consulted the ENTRY on
    /// this path, so a recall could bury a superseded fact and an expansion of its neighbour handed that fact
    /// straight back — forgetting governed recall and had no vote in traversal. Measured on LongMemEval's
    /// knowledge-update class, all 70 questions: a context holding the current value and NOT the superseded
    /// one fell from 31.4% to 28.6% as the walk went deeper, and a floor of 0.8 holds it at 31.4%.</para>
    ///
    /// <para><b>It EXCLUDES, and ordering was tried first and measured doing nothing.</b> Weighting the walk
    /// order by retrievability cannot help unless the caller's budget binds, and in the measured case it did
    /// not — everything the walk found fitted, so the position it found it in was irrelevant. A floor is the
    /// smaller surface that actually moves the number.</para>
    ///
    /// <para><b>It never hides the entry the caller NAMED.</b> The seed of an expansion is returned whatever
    /// its retrievability — asking to expand something buried must still return it, and only the walk OUT
    /// from it is filtered. Nor does it delete anything (<b>D41</b>): a floored neighbour stays stored and
    /// stays reachable by a recall that scores it.</para>
    ///
    /// <para><b>It costs recall, so it is off by default.</b> That gain gave back 1.5 points of current-fact
    /// hit rate. On a SEARCH workload it is cheaper still — LoCoMo loses 0.5 points at shot 2 and none at
    /// shot 3 while cutting context 17% — but which side a deployment wants is a property of its workload,
    /// and the value that BINDS depends on how decayed the store is rather than on the questions asked: 0.5
    /// excludes nothing on a freshly ingested one.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set outside [0, 1] or to a non-finite value.</exception>
    public double ExpansionRetrievabilityFloor
    {
        get;
        init => field = MemoryOption.Require(value, MemoryOptionRange.Closed(0, 1), nameof(GraphMemoryOptions),
            "retrievability is a probability in [0,1], so a floor outside it either admits everything or "
            + "nothing regardless of what the forgetting curve says");
    }

    /// <summary>Why the three knobs above take <see cref="MemoryOptionRange.Finite"/> and no domain beyond
    /// it. Shared as one constant because they share one failure mode; the guard itself is shared with every
    /// other memory options record through <see cref="MemoryOption"/>.
    ///
    /// <para>A NaN is not theoretical: <c>EffectiveEdgeWeight</c>'s <c>halfLife &lt;= 0</c> guard is false for
    /// it, so every edge weight becomes NaN and traversal stops discriminating, silently
    /// (<c>.claude/knowledge/pitfalls.md</c>, "a clamp is not a finiteness guard").</para>
    ///
    /// <para><b>Deliberately finiteness only, not a domain.</b> An out-of-range FINITE value merely
    /// exaggerates its knob and stays diagnosable; bounds the measurements have not justified would be
    /// invented.</para>
    /// </summary>
    private const string Disables =
        "a NaN or an infinity here does not exaggerate the knob, it silently disables every comparison that "
        + "reads it — an edge weight or a floor that compares false against everything, with nothing "
        + "reported.";

    /// <summary>Whether every reinforcement is logged — the pre-review state, the derived grade, and the
    /// post-review state (design spec §3) — so a future fitting task
    /// has something to read (<c>docs/DECISIONS.md</c> D51: a fit waits on a store recalled over time; D49
    /// rejected fitting against an invented corpus). <b>Default ON, deliberately opt-OUT rather than opt-in</b>: a consumer
    /// who never fits pays one small, capped write per reinforcement; a consumer who wants to fit later
    /// cannot recover history nobody logged. Set false to skip the write entirely — cheaper than logging and
    /// discarding, and the honest choice for a deployment that will genuinely never read this table.
    /// <para>DATA, never a decision: see <see cref="Lyntai.Memory.Engines.GraphMemoryEngine"/>'s own remarks
    /// on <c>ReinforceAsync</c> for the proof that nothing this engine does — recall, ranking, pruning — ever
    /// reads what this writes.</para></summary>
    public bool LogReviews { get; init; } = true;

    /// <summary>Which of reinforcement's two effects a recall applies to the entries it returned — the age
    /// reset, the stability growth, both (the default) or neither. See
    /// <see cref="MemoryReinforcementEffects"/> for what each effect does.
    /// <para>Governs <see cref="IMemoryGraphStore.TouchAsync"/> only. Co-activation edges and the review
    /// log are separate concerns with their own switches (<see cref="CoActivationCap"/>,
    /// <see cref="LogReviews"/>) and are unaffected by this — they record that a recall happened rather
    /// than changing what an entry is worth.</para>
    /// <para><b><see cref="MemoryReinforcementEffects.StabilityGrowth"/> on its own throws</b>, because the
    /// store applies the age reset as an inseparable part of the same write — so that combination would
    /// silently apply NEITHER effect. Refusing it at the line that configured it is the alternative to a
    /// deployment whose reinforcement quietly does nothing; see that member's own remarks for why the
    /// combination is not offered rather than implemented.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">The value requests growth without the age reset, or is
    /// not a combination of the defined flags.</exception>
    public MemoryReinforcementEffects Reinforcement
    {
        get;
        init
        {
            const MemoryReinforcementEffects all = MemoryReinforcementEffects.All;
            if ((value & ~all) != 0)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    $"{nameof(GraphMemoryOptions)}.{nameof(Reinforcement)} must be a combination of " +
                    $"{nameof(MemoryReinforcementEffects.AgeReset)} and " +
                    $"{nameof(MemoryReinforcementEffects.StabilityGrowth)}.");
            if (value == MemoryReinforcementEffects.StabilityGrowth)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    $"{nameof(GraphMemoryOptions)}.{nameof(Reinforcement)} cannot request " +
                    $"{nameof(MemoryReinforcementEffects.StabilityGrowth)} without " +
                    $"{nameof(MemoryReinforcementEffects.AgeReset)}: the store resets an entry's age as an " +
                    "inseparable part of the same write, so this combination would apply NEITHER effect. " +
                    $"Use {nameof(MemoryReinforcementEffects.All)} to keep both, or " +
                    $"{nameof(MemoryReinforcementEffects.AgeReset)} for the best-measured configuration.");
            field = value;
        }
    } = MemoryReinforcementEffects.All;

    /// <summary>Which CALLS reinforce what they touched — a recall, an expansion, both (the default) or
    /// neither. Composes with <see cref="Reinforcement"/>: the acts selected here apply the effects selected
    /// there. See <see cref="MemoryReinforcementActs"/> for why the two are separate types.</summary>
    public MemoryReinforcementActs ReinforceOn { get; init; } = MemoryReinforcementActs.All;

    /// <summary>How many of a recall's returned entries it reinforces, taken from the TOP of the ranking.
    /// <c>null</c> reinforces everything returned.
    ///
    /// <para><b>The graded form of <see cref="ReinforceOn"/>, and the reason the default could be tuned at
    /// all.</b> A recall returns a ranked list of GUESSES; reinforcing all of them treats the tenth hit as
    /// evidence equal to the first, which is where "the loop upvotes its own prior" does its damage. An
    /// all-or-nothing act gate could only answer this by switching recall reinforcement off entirely, which
    /// helps an application that expands and strands one that does not. A cap keeps the signal for every
    /// consumer and drops the tail that carries the noise.</para>
    ///
    /// <para>Applies to a RECALL only. An expansion is a single entry a caller explicitly paid for, so
    /// there is no ranked tail to trim.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set below zero.</exception>
    public int? RecallReinforceCap { get; init => field = MemoryOption.Require(value, 0, nameof(GraphMemoryOptions), ZeroIsOff); }

    /// <summary>Whether a registered <see cref="Lyntai.Memory.Verification.IMemoryVerificationPolicy"/> may
    /// also REMOVE results from what the caller sees, rather than only steering what gets reinforced.
    ///
    /// <para><b>Off by default, and the asymmetry is deliberate.</b> A mistaken judgement should cost a
    /// little learning, not a lost answer — those are very different failures, and only one of them is
    /// recoverable on the next recall. Filtering is the stronger promise and is opted into separately, the
    /// same way a suggested grade is.</para>
    ///
    /// <para><b>OFF does not mean "the verdict does nothing to the result", and reading it that way is what
    /// this paragraph exists to prevent.</b> With filtering off a verdict still PROMOTES every endorsed
    /// candidate to the front of the ordinary results, before the caller's limit is applied and over a
    /// candidate set <see cref="VerificationDepth"/> deep — so it reorders the page and can pull onto it an
    /// answer that never fitted. This option adds REMOVAL of everything unendorsed; it is not the switch
    /// that makes a judge visible. See <see cref="Lyntai.Memory.Verification.IMemoryVerificationPolicy"/>
    /// for the whole chain and the measurement that reads like a null result.</para>
    ///
    /// <para><see cref="MemoryGrade.Authoritative"/> material is exempt whatever the verdict: objective (1)
    /// does not defer to a judge (<c>docs/DECISIONS.md</c> <b>D56</b>).</para></summary>
    public bool VerificationFilters { get; init; }

    /// <summary>How an endorsed candidate reaches the page — promoted ahead of everything unendorsed
    /// (<see cref="Lyntai.Memory.Verification.MemoryVerdictCombination.Partition"/>, the default and the
    /// long-standing behaviour), or competing on rank with the ranking's own order
    /// (<see cref="Lyntai.Memory.Verification.MemoryVerdictCombination.Fuse"/>).
    ///
    /// <para><b>The partition is the only place this engine combines a signal by partition rather than by
    /// rank competition</b>, which is what makes it worth a knob: every other signal fuses
    /// (<c>docs/DECISIONS.md</c> <b>D82</b>, <b>D103</b>). Its cost is bounded by how MANY candidates a judge
    /// endorses — a set larger than the caller's limit replaces the page instead of refining it.</para>
    ///
    /// <para><b>Reach for <c>Fuse</c> when the judge is weak or <see cref="VerificationDepth"/> is deep.</b>
    /// Measured on LoCoMo with a real 4B judge at the shipped depth, the partition cost <b>10.5 points</b> of
    /// evidence-hit on one vector backend and <b>12.0</b> on a second, while fusing the same verdict from the same
    /// model recovered ALL of that loss on the first and <b>most</b> of it on the second — 9.5 of 12.0,
    /// landing 2.5 short of the unjudged base (<c>docs/memory-measurements.md</c> §5).
    /// <b>So expect it to remove most of a bad judge's cost, never to beat the base</b>: it is insurance
    /// rather than an improvement. How MUCH it recovers is vector backend-dependent; that it recovers most of it
    /// replicated. It is not the default because a silent reordering is a change no consumer could detect at
    /// compile time.</para>
    ///
    /// <para>Inert without a registered
    /// <see cref="Lyntai.Memory.Verification.IMemoryVerificationPolicy"/>, and orthogonal to
    /// <see cref="VerificationFilters"/> — this decides ORDER, that decides removal.</para></summary>
    public Lyntai.Memory.Verification.MemoryVerdictCombination VerdictCombination { get; init; }

    /// <summary>How many top-ranked candidates a
    /// <see cref="Lyntai.Memory.Verification.IMemoryVerificationPolicy"/> is shown. <c>null</c> takes
    /// <see cref="DefaultVerificationDepthFactor"/> times the recall's own limit — the MEASURED saturation
    /// point, not a round number.
    ///
    /// <para><b>This is the knob that decides whether verification is worth anything.</b> Every miss this
    /// subsystem has is a reachable candidate ranked below the limit, never an unreachable one — so a judge
    /// shown only the entries that already won can correct almost nothing, while one shown the next tier
    /// down can promote an answer that was there all along. Rescue depth SATURATES, and the default sits at
    /// the knee (<c>docs/DECISIONS.md</c> D59 has the sweep).</para>
    ///
    /// <para><b>That saturation was measured with a PERFECT judge, where depth is free because an oracle
    /// never endorses junk — and for a real one depth is a PRECISION trade, so the knee moves.</b> Measured
    /// on LoCoMo with one 4B judge (<c>docs/memory-measurements.md</c> §5): at 2× the limit it was level with no judge,
    /// and at this default's 4× it cost 10.5 points of evidence-hit, because its selectivity collapsed on
    /// the longer list and it endorsed more candidates than the page could hold. <b>The default is right for
    /// a strong judge and can be actively harmful for a weak one</b>; it did not move, because one model on
    /// one workload does not set a default.</para>
    ///
    /// <para>Bounded because judgement is not free: showing a model every candidate would cost more per
    /// recall than the recall itself. Depth trades that cost against how far down an answer may be rescued
    /// from. Values below the recall's limit are raised to it — a verifier that saw fewer candidates than
    /// are being returned could only ever demote.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set below zero.</exception>
    public int? VerificationDepth
    {
        get;
        init => field = MemoryOption.Require(value, 0, nameof(GraphMemoryOptions),
            "a negative depth shows a judge nothing; any value below the recall's limit is raised to it.");
    }

    /// <summary>What <see cref="VerificationDepth"/> defaults to, as a multiple of the recall's limit.
    /// The measured saturation point — <c>docs/DECISIONS.md</c> D59 has the sweep.</summary>
    public const int DefaultVerificationDepthFactor = 4;

    /// <summary>The most recent logged reviews <see cref="LogReviews"/> retains, PER ENGINE, before older
    /// ones are evicted — an unbounded log grows without limit on a busy engine, which is not shippable on
    /// its own (design spec §3). Bounded, not exact: see
    /// <see cref="Lyntai.Memory.MemoryReviewLogPacing"/> for the eviction strategy and its trade-off. Ignored
    /// entirely when <see cref="LogReviews"/> is false.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Set below zero.</exception>
    public int ReviewLogCap { get; init => field = MemoryOption.Require(value, 0, nameof(GraphMemoryOptions), ZeroIsOff); } = 10_000;

    /// <summary>How many entries a re-embed (<see cref="IReindexableMemory.ReindexAsync"/>) sends to the embedding
    /// backend per call. Larger batches mean fewer calls; a backend with a per-call limit wants it smaller.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Set below one.</exception>
    public int ReindexBatchSize
    {
        get;
        init => field = MemoryOption.Require(value, 1, nameof(GraphMemoryOptions),
            "a batch of no entries would re-embed nothing.");
    } = 32;

    /// <summary>Why a count whose zero already means "none" refuses a negative: it can only be a mistake,
    /// and reading it as zero would hide that.</summary>
    private const string ZeroIsOff = "zero already means none, so a negative count can only be a mistake.";
}
