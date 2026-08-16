namespace Lyntai.Memory;

/// <summary>How the graph engine retrieves. Every value is defaulted; several are <b>unmeasured</b> and say
/// so — see the MEM-TUNE task before treating them as tuned.</summary>
public sealed record GraphMemoryOptions
{
    /// <summary>How far to spread from the seed set. Three or more hops reaches most of a connected graph,
    /// which defeats the purpose. Reasoned, not measured.</summary>
    public int Hops { get; init; } = 2;

    /// <summary>The character budget an expansion falls back to when the caller passes none — the "engine's
    /// configured budget" <see cref="IExpandableMemory.ExpandAsync"/> has always promised.
    /// <para>Null (the default) means UNBOUNDED, which is what the engine did before the parameter was
    /// honoured at all, so leaving it unset changes nothing. It bounds the NEIGHBOURS only: the expanded
    /// entry's own content is always returned whole, because that is what expansion is for.</para></summary>
    public int? ExpandCharBudget { get; init; }

    /// <summary>The retrievability below which <c>PruneAsync</c> may REAP an entry — "forgotten enough to
    /// delete".
    /// <para>Recall does not use it. Deleting is the only thing in this model that removes a memory, and it
    /// is always explicit; being faint hides an entry behind stronger ones
    /// (<see cref="Lyntai.Memory.Ranking.MultiplicativeRankingOptions.RelativeFloor"/>) and never removes
    /// it.</para>
    /// Chosen against the corpus in <c>MemoryDecaySimulationTests</c>, which pins the DYNAMICS — reuse
    /// outrunning interference — and not against production usage. A starting point, not a tuned value.</summary>
    public double MinRetrievability
    {
        get;
        init => field = Finite(value, nameof(MinRetrievability));
    } = 0.05;

    /// <summary>Length cap for a DERIVED headline; an authored one is used as given, and authoritative
    /// content is never shortened at all. Chosen against the corpus in <c>MemoryDecaySimulationTests</c>, which pins the DYNAMICS — reuse
    /// outrunning interference — and not against production usage. A starting point, not a tuned value.</summary>
    public int HeadlineChars { get; init; } = 120;

    /// <summary>How many of the returned nodes get co-activation edges. A ten-item recall would otherwise
    /// write forty-five edges every turn. Reasoned, not measured.</summary>
    public int CoActivationCap { get; init; } = 5;

    /// <summary>
    /// How many of a recall's slots <see cref="MemoryGrade.Authoritative"/> material may take. <c>null</c>
    /// (the default) means "as many as it needs". <b>Always capped by the recall's own limit</b>, whatever
    /// value is set here — this option can only ever REDUCE displacement, never raise the number of items a
    /// recall returns.
    ///
    /// <para><b>That cap is unconditional as of the 2026-08-14 review, and it was not before.</b> Only
    /// the <c>null</c> default carried it, so an explicit value larger than a caller's
    /// <see cref="MemoryQuery.Limit"/> overran the limit outright — measured at reserve <c>5</c> /
    /// <c>Limit: 2</c>, three items came back and not one ordinary hit. That is the ordinary case rather than
    /// a corner: this option is configured per ENGINE, sized against <see cref="DefaultLimit"/>, while the
    /// limit arrives per QUERY, so any caller trimming a prompt budget passes a smaller one.</para>
    ///
    /// <para><b>Why the default is unbounded.</b> Design §5.7.0's objective (1) — never lose an authoritative
    /// fact — is the only goal with no acceptable failure rate. Before this option existed, re-admitted exact
    /// facts were appended after the ranked set and then cut by the limit, and the first end-to-end
    /// measurement found ALL of them lost in ALL five languages. An authoritative entry displacing ordinary
    /// material is what marking a fact authoritative MEANS; it is the caller's explicit decision, not an
    /// accident.</para>
    ///
    /// <para><b>What a value buys.</b> Setting it bounds the displacement: with <c>2</c> and a limit of
    /// <c>10</c>, at most two slots go to re-admitted exact facts and eight remain for ordinary hits. Use it
    /// when a task marks many facts authoritative and ordinary recall still has to get through. <c>0</c>
    /// restores the pre-3.0 behaviour exactly — and re-breaks objective (1), which is why it is not the
    /// default.</para>
    ///
    /// <para><b>It counts EVERY authoritative candidate, not only the ones a ranking policy dropped</b> — and
    /// that distinction is the whole of why the reserve works at all. The first version of this mechanism
    /// reserved slots only for entries the policy had OMITTED and changed nothing measurable: a policy does
    /// not omit an exact fact, it RANKS it, and one the query did not match carries <c>Relevance 0</c>, so it
    /// sorted to the bottom and the limit cut it exactly as before. A reserved entry keeps the policy's own
    /// score where the policy produced one, so a fact that ranked on merit is not silently re-scored to zero
    /// by being reserved.</para>
    ///
    /// <para>The paragraph above said the OPPOSITE until 2026-08-14 — that an entry the policy returned on
    /// merit "is not counted against this" — which described the rejected first version, and sat three lines
    /// from the engine comment that says so in capitals. A public doc kept the wording of an implementation
    /// the measurement had already thrown out.</para>
    /// </summary>
    public int? AuthoritativeReserve { get; init; }

    /// <summary>How many recent entries an <see cref="Lyntai.Memory.Annotation.IMemoryAnnotationPolicy"/> is
    /// shown, so a pronoun in the fact being written is resolvable ("she works at a hospital" is about
    /// nobody without them). Zero shows none, which makes every annotation a judgement on the write alone.
    /// <para>Costs one no-query seed per write, and only when an annotator is registered — no annotator, no
    /// read.</para></summary>
    public int AnnotationContext { get; init; } = 8;

    /// <summary>How many existing entries each annotated SUBJECT links the new one to. Bounds the write:
    /// subjects × this many edges, so a fact about three entities cannot quietly become a hub. Reasoned, not
    /// measured — the mechanism is what this release establishes; the constant is a starting point.</summary>
    public int AnnotationLinkK { get; init; } = 3;

    /// <summary>How many already-used subjects an annotator is shown as REUSE candidates, most-used first.
    /// Zero shows none, which measurably degrades consistency: real models answered three facts about one
    /// person with three different-but-defensible handles, and nothing linked. Costs one grouped read per
    /// annotated write.</summary>
    public int AnnotationKnownSubjects { get; init; } = 24;

    // A `ReinforceOn` option lived here briefly on 2026-08-12 and was REVERTED before 3.0 froze, by the
    // measurement it existed to enable — recorded because the reason is a design fact, not an accident.
    // It gated "does this act reinforce" as ONE switch, but GraphMemoryEngine.ReinforceAsync does two
    // separable things: TouchAsync RESETS an entry's age primitives, and Reinforce GROWS its stability.
    // Those pull in OPPOSITE directions (TASKS.md Part 64) — the age reset is what keeps a rarely-queried
    // fact alive, while the growth is what entrenches whatever the ranker already favoured — so a single
    // gate cannot express the configuration the evidence actually favours. Freezing a seam cut at the wrong
    // joint costs a major to correct; the reshaped one is designed with the evidence rather than ahead of it.

    /// <summary>How many candidates to fetch per requested item. The store bounds the candidate set with
    /// plain arithmetic and the policy ranks it exactly afterwards, so a multiple above 1 is what keeps
    /// that ranking meaningful.</summary>
    public int CandidateMultiplier { get; init; } = 4;

    /// <summary>Items returned when the query names no limit.</summary>
    public int DefaultLimit { get; init; } = 10;

    /// <summary>How many semantically-similar entries a recall considers, in ADDITION to its lexical
    /// matches. <c>0</c> (the default) considers none, which is what every version before this did.</summary>
    public int SemanticSeedK { get; init; }

    /// <summary>How many near neighbours a new entry is linked to when similarity enrichment is wired (an
    /// <see cref="Lyntai.Embeddings.IEmbedder"/> and an <see cref="IVectorStore"/> are registered).
    /// Chosen against the corpus in <c>MemoryDecaySimulationTests</c>, which pins the DYNAMICS — reuse
    /// outrunning interference — and not against production usage. A starting point, not a tuned value.</summary>
    public int SimilarityK { get; init; } = 5;

    /// <summary>Cosine similarity below which enrichment does not link. Without a floor a new entry links
    /// to its <see cref="SimilarityK"/> nearest neighbours however unrelated they are, which in a small or
    /// young graph means linking to nearly everything. Chosen against the corpus in <c>MemoryDecaySimulationTests</c>, which pins the DYNAMICS — reuse
    /// outrunning interference — and not against production usage. A starting point, not a tuned value.</summary>
    public double MinSimilarity
    {
        get;
        init => field = Finite(value, nameof(MinSimilarity));
    } = 0.6;

    /// <summary>Half-life of a co-activation edge's WEIGHT — never the retrievability curve's own connection
    /// boost, which is a forgetting-curve concern and lives on <c>DsrOptions</c> instead. Without decay here,
    /// edges only ever grow: every pair that has ever co-occurred stays linked at a rising weight, the graph
    /// saturates, and spreading stops discriminating because everything reaches everything.
    /// <see cref="Lyntai.Memory.Engines.GraphMemoryEngine"/> reads this directly (<c>EffectiveEdgeWeight</c>),
    /// independent of whichever retrievability policy is registered — it is this ENGINE's own knob, not the
    /// curve's, which is why it lives here rather than on an options record a policy owns.
    /// <para><b>Moved here in 3.0</b> from the now-deleted <c>HalfLifeOptions.EdgeHalfLife</c>, which this
    /// record used to carry as its own <c>Decay</c> member (<c>docs/DECISIONS.md</c>) — the five OTHER fields
    /// that record carried (<c>InitialStability</c>, <c>ReinforceFactor</c>, <c>MaxStability</c>,
    /// <c>ConnectionBoost</c>, <c>MaxConnectionBoost</c>) governed the deleted exponential curve's own
    /// arithmetic and went with it; only this one was ever this engine's rather than that curve's, and it is
    /// the reason <c>Decay</c> could not simply disappear without a replacement.</para></summary>
    public double EdgeHalfLife
    {
        get;
        init => field = Finite(value, nameof(EdgeHalfLife));
    } = 100;

    /// <summary>Reject a non-finite value at the line that configured it.
    ///
    /// <para><b>NaN is the one that matters, and it is not theoretical here.</b> <c>EffectiveEdgeWeight</c>
    /// guards <see cref="EdgeHalfLife"/> with <c>halfLife &lt;= 0</c> — false for <c>NaN</c> — so a NaN
    /// falls through into <c>Math.Pow(2, -age / NaN)</c> and every edge weight in the graph becomes NaN.
    /// Traversal then orders by a value that compares false against everything: spreading silently stops
    /// discriminating, and nothing reports a problem. <c>DsrOptions.MaxStability</c> had the identical
    /// defect until 3.0, where a NaN was written back to the store and left an entry that neither ranked,
    /// nor pruned, nor surfaced as broken.</para>
    ///
    /// <para><b>Deliberately finiteness only, not a domain.</b> Each of these three knobs has a sensible
    /// range, but an out-of-range FINITE value merely exaggerates its effect and stays diagnosable, while a
    /// non-finite one makes every comparison downstream meaningless. Guarding the second without inventing
    /// bounds the measurements have not justified is the honest line — and the same one
    /// <c>MultiplicativeRankingOptions</c> draws when it rejects NaN and both infinities everywhere.</para>
    /// </summary>
    private static double Finite(double value, string name) =>
        double.IsFinite(value)
            ? value
            : throw new ArgumentOutOfRangeException(name, value,
                $"{nameof(GraphMemoryOptions)}.{name} must be a finite number. A NaN or an infinity here " +
                "does not exaggerate the knob, it silently disables every comparison that reads it — an " +
                "edge weight or a floor that compares false against everything, with nothing reported.");

    /// <summary>Whether every reinforcement is logged — the pre-review state, the derived grade, and the
    /// post-review state (design spec §3, 2026-08-11 fsrs-properly plan Task 3) — so a future fitting task
    /// has something to read (<c>TASKS.md</c> Part 56 FSRS-B; <c>docs/DECISIONS.md</c> D49 rejected fitting
    /// against an invented corpus). <b>Default ON, deliberately opt-OUT rather than opt-in</b>: a consumer
    /// who never fits pays one small, capped write per reinforcement; a consumer who wants to fit later
    /// cannot recover history nobody logged. Set false to skip the write entirely — cheaper than logging and
    /// discarding, and the honest choice for a deployment that will genuinely never read this table.
    /// <para>DATA, never a decision: see <see cref="Lyntai.Memory.Engines.GraphMemoryEngine"/>'s own remarks
    /// on <c>ReinforceAsync</c> for the proof that nothing this engine does — recall, ranking, pruning — ever
    /// reads what this writes.</para></summary>
    public bool LogReviews { get; init; } = true;

    /// <summary>Which of reinforcement's two effects a recall applies to the entries it returned — the age
    /// reset, the stability growth, both (the default) or neither. See
    /// <see cref="MemoryReinforcementEffects"/> for what each one measured and why the seam is cut at the
    /// effects rather than at the acts.
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
    public int? RecallReinforceCap { get; init; } = DefaultRecallReinforceCap;

    /// <summary>Whether a registered <see cref="Lyntai.Memory.Verification.IMemoryVerificationPolicy"/> may
    /// also REMOVE results from what the caller sees, rather than only steering what gets reinforced.
    ///
    /// <para><b>Off by default, and the asymmetry is deliberate.</b> A mistaken judgement should cost a
    /// little learning, not a lost answer — those are very different failures, and only one of them is
    /// recoverable on the next recall. Filtering is the stronger promise and is opted into separately, the
    /// same way a suggested grade is.</para>
    ///
    /// <para><see cref="MemoryGrade.Authoritative"/> material is exempt whatever the verdict: objective (1)
    /// does not defer to a judge (<c>docs/DECISIONS.md</c> <b>D56</b>).</para></summary>
    public bool VerificationFilters { get; init; }

    /// <summary>How many top-ranked candidates a
    /// <see cref="Lyntai.Memory.Verification.IMemoryVerificationPolicy"/> is shown. <c>null</c> takes
    /// <see cref="DefaultVerificationDepthFactor"/> times the recall's own limit — the MEASURED saturation
    /// point, not a round number.
    ///
    /// <para><b>The measurement that set it</b> (perfect-oracle judge, full corpus replay, limit 10):
    /// depth 10 (observe-only) recovered <c>-0.0857</c> of the miss rate; depth 20 <c>-0.2214</c>; depth 40
    /// <c>-0.2500</c>; and 80, 160 and 5000 all returned exactly the same as 40. Four times the limit is
    /// where rescuing stops paying, so it is what a consumer who says nothing gets.</para>
    ///
    /// <para><b>This is the knob that decides whether verification is worth anything.</b> Measured over a
    /// full corpus replay: of the relevant entries a recall failed to return, <b>100% were reachable
    /// candidates ranked below the limit</b> and none were unreachable. The miss rate is a RANKING failure
    /// end to end — so a judge shown only the entries that already won can correct almost nothing, while one
    /// shown the next tier down can promote an answer that was there all along.</para>
    ///
    /// <para>Bounded because judgement is not free: showing a model every candidate would cost more per
    /// recall than the recall itself. Depth trades that cost against how far down an answer may be rescued
    /// from. Values below the recall's limit are raised to it — a verifier that saw fewer candidates than
    /// are being returned could only ever demote.</para></summary>
    public int? VerificationDepth { get; init; }

    /// <summary>What <see cref="VerificationDepth"/> defaults to, as a multiple of the recall's limit.
    /// Measured saturation point — see that property's own remarks for the sweep behind it.</summary>
    public const int DefaultVerificationDepthFactor = 4;

    /// <summary>The most recent logged reviews <see cref="LogReviews"/> retains, PER ENGINE, before older
    /// ones are evicted — an unbounded log grows without limit on a busy engine, which is not shippable on
    /// its own (design spec §3). Bounded, not exact: see
    /// <see cref="Lyntai.Memory.MemoryReviewLogPacing"/> for the eviction strategy and its trade-off. Ignored
    /// entirely when <see cref="LogReviews"/> is false.</summary>
    public int ReviewLogCap { get; init; } = 10_000;

    /// <summary>The measured default for <see cref="RecallReinforceCap"/>. Set from measurement, never
    /// reasoned.</summary>
    private static readonly int? DefaultRecallReinforceCap = null;
}
