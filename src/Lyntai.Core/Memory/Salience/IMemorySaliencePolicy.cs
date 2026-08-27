using Lyntai.Memory.Modulation;

namespace Lyntai.Memory.Salience;

/// <summary>What a salience policy may judge against. Carries no store and no clock, so a policy is pure and
/// testable with a literal.</summary>
/// <param name="Engine">The engine being written to, for a policy that varies by purpose.</param>
/// <param name="Novelty">How unlike anything already stored this write is: 1 when nothing resembles it,
/// 0 when it is an exact re-remember. Supplied by the engine, which is what lets the default policy use
/// prediction error without issuing a query of its own.</param>
/// <param name="ComparableCount">How many stored entries this write could actually be compared against —
/// the neighbours the engine's similarity probe found. <b>Not a count of the whole scope</b>, which no
/// engine can supply without a second query. With too few comparables, novelty carries no information and
/// a policy should decline to judge.</param>
/// <param name="SimilarCount">How many stored entries actually RESEMBLE this write — the neighbours scoring
/// at or above <see cref="Lyntai.Memory.GraphMemoryOptions.MinSimilarity"/>, after the same self-exclusion
/// <paramref name="Novelty"/> uses.
/// <para><b>Distinct from <paramref name="ComparableCount"/>, which cannot answer this.</b> That one is the
/// raw return of a search asking for <c>SimilarityK + 1</c> with NO floor applied, so it saturates once the
/// store holds more than <c>SimilarityK</c> and reports the same number for a write resembling one thing and
/// a write resembling many. It answers "was there enough to judge against"; this answers "how much of it was
/// actually close".</para>
/// <para><b>Bounded by however many neighbours the write's own similarity search returned, so it is a
/// floor on density and never a census.</b> A write resembling far more than that reports only as many as
/// were fetched, and a policy must read it as "at least this many" rather than as a count of the
/// scope.</para>
/// <para><c>0</c> when no similarity search ran — no embedder, no vector store, or
/// <c>SimilarityK &lt;= 0</c>. Nothing to compare against is no information, exactly as
/// <paramref name="Novelty"/> reports it.</para></param>
public readonly record struct SalienceContext(
    string Engine, double Novelty, int ComparableCount, int SimilarCount = 0);

/// <summary>
/// Which salience policies PRODUCED this entry's stored signals — read through
/// <see cref="Lyntai.Memory.MemoryProvenance"/>, never compared or combined directly (design doc §5.7).
/// Salience needs provenance for the same reason retrievability does: it WRITES persisted state (the
/// signals bag) a later policy might need and not find.
/// <para>Salience is PLURAL — several policies may coexist — so a write's provenance is the OR of every
/// policy that actually returned a non-empty result, not of every policy that merely ran: one that
/// declined to judge (too few comparables, a caught failure) contributed nothing to the composed bag and
/// must not be credited with computing it.</para>
/// <para><b>Bits 0-31 are reserved for this library; never allocate above bit 31 here.</b> Bits 32-62 are a
/// consumer's own range — <see cref="IMemorySaliencePolicy"/> is public and a third-party implementation
/// must be able to carry provenance too, so this enum stays open to an unnamed member: cast any single bit
/// in 32-62 to this type, exactly as the named library member below is. Bit 63 is NEVER set — see
/// <see cref="Lyntai.Memory.MemoryProvenance"/> for why.</para></summary>
[Flags]
public enum MemorySalienceProvenance : long
{
    /// <summary>No salience policy produced a signal for this entry — every row from before this domain
    /// existed, and any write every registered policy declined to judge.</summary>
    None = 0x0000_0000,

    /// <summary><see cref="StructuralSaliencePolicy"/>.</summary>
    Structural = 0x0000_0001,
}

/// <summary>
/// Decides how strongly a write is encoded. <b>Salience means "this memory does not fade away" — decay
/// resistance AND store admission priority — NOT "first priority"</b> (2026-08-09 —
/// <c>docs/DECISIONS.md</c> D45, corrected same day by D45): it lengthens a half-life, and orders admission
/// in the store when a candidate set overflows its budget, so a salient memory is always found even when it
/// matches a query poorly — both on by default. It can ALSO lift rank in
/// <see cref="Lyntai.Memory.Engines.GraphMemoryEngine"/> by a bounded logarithm
/// (<see cref="Lyntai.Memory.Ranking.MultiplicativeRankingOptions.SalienceRankWeight"/>), letting a salient
/// memory jump the queue ahead of a
/// better textual match — but that is a stronger, separate claim admission does not already make, and it
/// defaults OFF; a consumer opts in explicitly. This interface itself only reports a signal — it decides
/// none of that; see <see cref="SalienceRetentionPolicy"/> for the decay half and
/// <c>GraphMemoryEngine.RecallAsync</c> for the (opt-in) ranking half.
/// <para>A registered default ships (<see cref="StructuralSaliencePolicy"/>), so nothing has to be
/// implemented to get the model. An application wanting real judgement — affect, self-relevance — registers
/// its own, which is where a model belongs; the memory path itself stays model-free.</para>
/// <para><b>To turn salience OFF, register <see cref="NeutralSaliencePolicy"/> — registering NOTHING does
/// not do it.</b> An empty collection means "take the shipped default", the same convention the age seam
/// uses, so the intuitive way to disable this is the one way that silently does not
/// (<c>TASKS.md</c> Part 69).</para>
/// </summary>
public interface IMemorySaliencePolicy
{
    /// <summary>Signals for this write. Must be bounded: whatever it writes for
    /// <see cref="MemorySignals.WellKnown.Salience"/> is consumed by a retention policy whose declared maximum
    /// widens <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy.CandidateCutoff"/>.</summary>
    /// <param name="write">The material being remembered.</param>
    /// <param name="context">What it can be judged against.</param>
    MemorySignals Signals(MemoryWrite write, in SalienceContext context);

    /// <summary>This policy's own bit in <see cref="MemorySalienceProvenance"/> — what a write OR's into
    /// <c>GraphNode.ProvenanceSalience</c> whenever <see cref="Signals"/> returns a non-empty result.
    /// Exactly one bit; never <see cref="MemorySalienceProvenance.None"/> and never more than one bit, or a
    /// fitness check reading it would report this policy as having contributed when it never ran (or never
    /// distinguish it from a different one).</summary>
    MemorySalienceProvenance Provenance { get; }
}

/// <summary>Constants of the default salience policy and <see cref="SalienceRetentionPolicy"/>. Both read
/// the same options, so the ceiling salience reports and the bound retention declares cannot drift apart.
/// <para><b>DI registration is the configuration path.</b> Unlike <see cref="GraphMemoryOptions"/>, which is
/// passed by value through <c>UseGraph(...)</c>, this record has no builder surface — the same shape
/// <see cref="Lyntai.Memory.Forgetting.DsrOptions"/> takes: <c>services.AddSingleton(new SalienceOptions { … })</c>
/// before <c>AddLyntai</c> is what reaches both types, because <c>AddMemoryEngine</c> resolves each out of the
/// container with its own <c>sp.GetService&lt;…&gt;()</c>. A
/// hand-built <see cref="StructuralSaliencePolicy"/> or <see cref="SalienceRetentionPolicy"/> takes it as a
/// constructor argument instead — but then it is the caller's job to pass the SAME instance to both.</para></summary>
public sealed record SalienceOptions
{
    private readonly double _maxSalience = 4;
    private readonly int _minimumComparables = 3;

    /// <summary>The largest salience a policy may report, and therefore the most a half-life may be
    /// lengthened by this dimension. <b>Load-bearing</b>: <c>ModulatedRetrievability</c> widens
    /// <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy.CandidateCutoff"/> by exactly this, so a salience policy reporting
    /// more than the retention policy declares would make the cutoff too narrow — and that cutoff's only consumer is
    /// <see cref="IMemoryGraphStore.PruneAsync"/>, which DELETES, so the entries it fails to cover are
    /// permanently destroyed while the modulated curve still rated them retrievable. <b>Unmeasured</b> — a
    /// starting point.
    /// <para>Must be at least 1: a bound below the neutral value is not a smaller ceiling, it is a
    /// contradiction, and it would otherwise surface as an <c>ArgumentException</c> from a clamp deep in the
    /// recall path rather than at the line that configured it.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set below 1.</exception>
    public double MaxSalience
    {
        get => _maxSalience;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _maxSalience = value;
        }
    }

    /// <summary>How steeply novelty raises salience, as <c>1 + factor × novelty</c>. <b>Unmeasured</b>.
    /// <para>Finiteness only — a negative weight legitimately inverts the effect, so the guard rejects just
    /// <see cref="double.NaN"/> and the infinities. It was the ONE unguarded field of this record while both
    /// its siblings validated: <c>StructuralSaliencePolicy</c> feeds it to
    /// <see cref="Math.Clamp(double,double,double)"/>, which PROPAGATES <c>NaN</c> rather than clamping it,
    /// so a non-finite weight put a <c>NaN</c> salience into the signals bag. Three downstream readers
    /// happened to coerce it back, which is the shape <c>pitfalls.md</c> warns about — the guard belongs to
    /// the VALUE, not to whoever reads it last.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a non-finite value.</exception>
    public double NoveltyWeight
    {
        get;
        init => field = MemoryOption.Require(value, MemoryOptionRange.Finite, nameof(SalienceOptions),
            "novelty scales salience multiplicatively, and a non-finite scale reaches the stored signal");
    } = 1.5;

    /// <summary>How many comparable entries must exist before novelty means anything. Below this the
    /// default policy reports nothing, because in a nearly-empty engine everything looks novel and scoring it
    /// would mark a whole first session as maximally important. <b>Unmeasured</b>.
    /// <para><b>Know the achievable ceiling before raising this.</b>
    /// <see cref="SalienceContext.ComparableCount"/> is what the engine's similarity probe found, which is
    /// bounded by <see cref="GraphMemoryOptions.SimilarityK"/> + 1 (6 at the defaults) — <b>not</b> by how
    /// much the engine holds. A value above that bound therefore means the policy NEVER judges anything,
    /// ever, with no error and no log: the feature is simply off. Raise
    /// <see cref="GraphMemoryOptions.SimilarityK"/> alongside it, or leave this at its default.</para>
    /// <para>Must be at least 1: zero or negative cannot express "wait for comparables", since
    /// <see cref="SalienceContext.ComparableCount"/> is never below 0 — it reads as a disabled guard while
    /// silently admitting the empty-engine case the guard exists to prevent.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set below 1.</exception>
    public int MinimumComparables
    {
        get => _minimumComparables;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _minimumComparables = value;
        }
    }
}
