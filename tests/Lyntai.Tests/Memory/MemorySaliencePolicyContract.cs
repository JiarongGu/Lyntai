using Lyntai.Memory;
using Lyntai.Memory.Salience;

namespace Lyntai.Tests.Memory;

/// <summary>Policy-agnostic facts every <see cref="IMemorySaliencePolicy"/> satisfies.
///
/// <para><b>Why this file exists.</b> Salience was one of FOUR policy seams with no contract while
/// <c>PolicyContractCoverageTests</c> made coverage structural for the other three — so a fifth
/// implementation could have shipped with no suite and every gate would have stayed green, which is the
/// exact shape that guard exists to prevent. Added 2026-08-17 by the pre-3.0 sweep (archive Part 86).</para>
///
/// <para>Salience is the seam that most deserves one. It is PLURAL, so implementations coexist and a
/// consumer's own policy runs beside the shipped default; what it writes is consumed by a retention policy
/// whose declared maximum widens <c>CandidateCutoff</c>; and that cutoff's only consumer is
/// <c>PruneAsync</c>, which DELETES. A policy that breaks one of these obligations does not fail a recall,
/// it destroys entries.</para>
///
/// <para>Every obligation below is the interface's OWN written promise, not an invented one: a bounded
/// signal, provenance that is exactly one bit and never <c>None</c>, and purity — the context "carries no
/// store and no clock, so a policy is pure and testable with a literal".</para></summary>
public static class MemorySaliencePolicyContract
{
    private static readonly MemoryWrite Write = new("t", "s", "some material of a stable length");

    private static readonly SalienceContext[] Contexts =
    [
        new("engine", 0, 0),            // an empty engine: nothing to compare against
        new("engine", 0, 100),
        new("engine", 1, 100),          // maximally novel, plenty of comparables
        new("engine", 0.5, 5),
        new("engine", double.NaN, 100), // a caller defect, which must not reach the stored signal
        new("engine", -5, 100),         // out of the documented 0..1 range, in both directions
        new("engine", 5, 100),
    ];

    /// <summary>Every value a policy writes is finite. <b>The measured reason this is a contract fact rather
    /// than an implementation detail:</b> <c>SalienceOptions.NoveltyWeight</c> was the one unguarded field of
    /// its record, and <see cref="Math.Clamp(double,double,double)"/> PROPAGATES <see cref="double.NaN"/>
    /// rather than clamping it — so a non-finite weight put a <c>NaN</c> salience into the signals bag, and
    /// three downstream readers happened to coerce it back. The guard belongs to the VALUE, which is here.
    /// <para>Non-finite CONTEXTS are included above on purpose: a caller defect must not become a stored
    /// one.</para></summary>
    public static void Every_signal_it_writes_is_finite(IMemorySaliencePolicy policy)
    {
        foreach (var context in Contexts)
        foreach (var (name, value) in policy.Signals(Write, context).Values)
            Assert.True(double.IsFinite(value),
                $"{policy.GetType().Name} wrote a non-finite '{name}' ({value}) for {context}");
    }

    /// <summary>Salience is never below the neutral <c>1</c>. The seam only ever LENGTHENS a half-life —
    /// "a factor below 1 is clamped away" on the retention side — so a policy reporting less is not asking
    /// for faster decay, it is writing a value no reader can honour.</summary>
    public static void A_reported_salience_is_at_least_neutral(IMemorySaliencePolicy policy)
    {
        foreach (var context in Contexts)
        {
            var signals = policy.Signals(Write, context);
            if (signals.Count == 0) continue;   // declining to judge is always allowed

            var salience = signals.Get(MemorySignals.WellKnown.Salience, fallback: 1);
            Assert.True(salience >= 1,
                $"{policy.GetType().Name} reported salience {salience} for {context}, below the neutral 1");
        }
    }

    /// <summary>Salience is BOUNDED by what a policy was configured with. Load-bearing rather than tidy:
    /// <c>ModulatedRetrievability</c> widens <c>CandidateCutoff</c> by exactly the retention policy's declared
    /// maximum, so a salience policy that reports more than that makes the cutoff too narrow — and the
    /// entries it then fails to cover are DELETED by <c>PruneAsync</c> while the modulated curve still rated
    /// them retrievable.</summary>
    /// <param name="policy">The policy under test.</param>
    /// <param name="ceiling">The <c>MaxSalience</c> it was constructed with.</param>
    public static void A_reported_salience_never_exceeds_its_configured_ceiling(
        IMemorySaliencePolicy policy, double ceiling)
    {
        foreach (var context in Contexts)
        {
            var salience = policy.Signals(Write, context).Get(MemorySignals.WellKnown.Salience, fallback: 1);
            Assert.True(salience <= ceiling,
                $"{policy.GetType().Name} reported salience {salience} for {context}, above its {ceiling} ceiling");
        }
    }

    /// <summary>Provenance is EXACTLY ONE BIT and never <see cref="MemorySalienceProvenance.None"/> — stated
    /// verbatim on the member. A policy answering <c>None</c> would be recorded as having contributed nothing
    /// when it ran; one answering several bits could never be distinguished from a different policy. Both
    /// break a later fitness check reading <c>GraphNode.ProvenanceSalience</c>.
    /// <para>Bit 63 is never set, for the reason <c>MemoryProvenance</c> gives.</para></summary>
    public static void Provenance_is_exactly_one_bit(IMemorySaliencePolicy policy)
    {
        var bits = (long)policy.Provenance;

        Assert.True(bits != 0, $"{policy.GetType().Name} declares MemorySalienceProvenance.None");
        Assert.True(long.PopCount(bits) == 1,
            $"{policy.GetType().Name} declares {long.PopCount(bits)} provenance bits ({policy.Provenance}); exactly one is required");
        Assert.True(bits > 0, $"{policy.GetType().Name} uses the reserved sign bit");
    }

    /// <summary>PURE: the same write and context yield the same answer however often they are asked. The
    /// interface's own words are that <see cref="SalienceContext"/> "carries no store and no clock, so a
    /// policy is pure and testable with a literal" — and the engine relies on it, calling
    /// <c>Signals</c> once per write with no memoization and no ordering guarantee against other
    /// policies.</summary>
    public static void It_is_a_pure_function_of_the_write_and_context(IMemorySaliencePolicy policy)
    {
        foreach (var context in Contexts)
        {
            var first = policy.Signals(Write, context);
            var second = policy.Signals(Write, context);

            Assert.Equal(first.Count, second.Count);
            foreach (var (name, value) in first.Values)
                Assert.Equal(value, second.Get(name), precision: 12);
        }
    }

    /// <summary>A policy that declines to judge returns an EMPTY bag rather than a neutral value, because the
    /// two are recorded differently: provenance is the OR of every policy that returned a NON-EMPTY result,
    /// "not of every policy that merely ran". A declining policy that returned a neutral <c>1</c> would be
    /// credited with computing a signal it never computed, and "never computed" would stop being
    /// distinguishable from "computed, and neutral".
    /// <para>Asserted as an implication rather than a requirement to decline: a policy that always judges is
    /// free to, and this fact then holds vacuously for it.</para></summary>
    public static void A_neutral_judgement_is_written_as_nothing_rather_than_as_one(
        IMemorySaliencePolicy policy)
    {
        foreach (var context in Contexts)
        {
            var signals = policy.Signals(Write, context);
            if (signals.Count == 0) continue;

            Assert.NotEqual(1, signals.Get(MemorySignals.WellKnown.Salience, fallback: 1));
        }
    }
}
