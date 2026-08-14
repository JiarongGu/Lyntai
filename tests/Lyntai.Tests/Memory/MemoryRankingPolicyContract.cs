using Lyntai.Memory;
using Lyntai.Memory.Ranking;

namespace Lyntai.Tests.Memory;

/// <summary>Facts every <see cref="IMemoryRankingPolicy"/> satisfies, run against each shipped
/// implementation so a custom policy cannot quietly break what
/// <see cref="Lyntai.Memory.Engines.GraphMemoryEngine"/> relies on.
/// <para>A policy sees candidates, never a store or a clock: everything here is built from
/// <see cref="MemoryCandidate"/> directly, with no engine or recall involved.</para></summary>
public static class MemoryRankingPolicyContract
{
    private static GraphNode Node(long id, double relevance = 1, MemorySignals signals = default) =>
        new(id, "e", "t", "s", $"headline {id}", $"content {id}", MemoryGrade.Associative,
            DateTimeOffset.UnixEpoch, RecallCount: 0, Stability: 20, Age: 0, Relevance: relevance,
            Degree: 0, Metadata: null, Signals: signals);

    private static MemoryCandidate Candidate(long id, double relevance = 1, double retrievability = 1,
        int hop = 0, MemorySignals signals = default) =>
        new(Node(id, relevance, signals), retrievability, hop);

    private static readonly MemoryRankingContext Context = new(Limit: 10, Engine: "contract");

    /// <summary>Ties must not wobble between runs: two candidates a policy scores identically still need a
    /// stable order, or the same query returns a different page on successive calls.
    /// <para>Calling <c>Rank</c> twice on the IDENTICAL input list would pass even with a dropped tiebreak —
    /// <c>List.Sort</c>'s introspective sort is itself a deterministic function of (input order,
    /// comparator), so the same input sorted the same way twice gives the same output regardless of whether
    /// ties are broken. What actually exposes an unstable sort is feeding the SAME set of tied candidates in
    /// a DIFFERENT input order (forward vs. reversed) and checking the output order is unaffected —
    /// which is what a real caller experiences, since nothing guarantees a store returns the same candidate
    /// set in the same order twice.</para></summary>
    public static void Ordering_is_deterministic(IMemoryRankingPolicy policy)
    {
        // four candidates, tied on everything a shipped policy reads (relevance, retrievability, hop, no
        // signals) — the only thing that CAN order them is a tiebreak.
        var forwardInput = new[] { Candidate(1), Candidate(2), Candidate(3), Candidate(4) };
        var reversedInput = forwardInput.Reverse().ToArray();

        var forward = policy.Rank(forwardInput, in Context).Select(r => r.Candidate.Node.Id).ToArray();
        var reversed = policy.Rank(reversedInput, in Context).Select(r => r.Candidate.Node.Id).ToArray();

        Assert.Equal(forward, reversed);
    }

    /// <summary>Best-first is the contract every caller reads. A policy free to return its own order would
    /// make the engine's <c>Take(limit)</c> silently keep the WORST candidates instead of the best
    /// ones.</summary>
    public static void Scores_are_ordered_best_first(IMemoryRankingPolicy policy)
    {
        var candidates = new[]
        {
            Candidate(1, relevance: 0.2),
            Candidate(2, relevance: 0.9),
            Candidate(3, relevance: 0.5),
        };

        var ranked = policy.Rank(candidates, in Context);

        Assert.NotEmpty(ranked);
        for (var i = 1; i < ranked.Count; i++)
            Assert.True(ranked[i - 1].Score >= ranked[i].Score,
                $"position {i - 1} (score {ranked[i - 1].Score}) fell below position {i} " +
                $"(score {ranked[i].Score}) — not best-first");
    }

    /// <summary>THE load-bearing one. A policy may floor, but never invent: every returned candidate must be
    /// one that was passed in, and none may appear twice.</summary>
    public static void It_returns_a_subset_without_duplicates(IMemoryRankingPolicy policy)
    {
        var candidates = new[] { Candidate(1), Candidate(2), Candidate(3), Candidate(4), Candidate(5) };
        var validIds = candidates.Select(c => c.Node.Id).ToHashSet();

        var ranked = policy.Rank(candidates, in Context);
        var returnedIds = ranked.Select(r => r.Candidate.Node.Id).ToList();

        Assert.All(returnedIds, id => Assert.Contains(id, validIds));
        Assert.Equal(returnedIds.Count, returnedIds.Distinct().Count());
    }

    /// <summary>An empty set in, an empty set out — the engine calls this before checking for hits, so a
    /// policy that threw or returned something non-empty here would break that check.</summary>
    public static void An_empty_candidate_set_ranks_to_empty(IMemoryRankingPolicy policy)
    {
        var ranked = policy.Rank([], in Context);

        Assert.Empty(ranked);
    }

    /// <summary>A non-finite score must never reach the caller: <see cref="double.NaN"/> compares false
    /// against every threshold, so one poisoned candidate that reached the whole set's own best/floor
    /// computation could empty an otherwise-healthy recall entirely — not by returning a bad score, but by
    /// silently returning NOTHING.
    /// <para>A single poisoned candidate mixed with healthy ones can't expose that failure by itself: a lone
    /// NaN score always sorts BELOW every finite one (<see cref="double.CompareTo(double)"/> treats NaN as
    /// the smallest value), so it never becomes the set's own best and never poisons anyone else's floor —
    /// it just quietly fails its own floor check and drops out, same as any other weak candidate would. What
    /// this fact actually exercises is candidates that ALL carry the same corrupted signal, which is what
    /// makes `best` itself turn non-finite: correct coercion keeps the whole group finite and returned;
    /// losing it collapses the WHOLE group to empty, not just the poisoned entries.</para></summary>
    public static void No_returned_score_is_non_finite(IMemoryRankingPolicy policy)
    {
        var naNSignals = MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, double.NaN);
        // a candidate a correct policy must NEVER return regardless of coercion: its own retrievability is
        // already NaN, so no salience handling can rescue its score.
        var unrecoverable = Candidate(1, retrievability: double.NaN, signals: naNSignals);
        // three otherwise-healthy, differently-scored candidates that ALL carry the identical poisoned
        // salience signal — see the class doc above for why this shape, not a single poisoned candidate, is
        // what actually discriminates a missing coercion.
        var poisonedButRetrievable = new[]
        {
            Candidate(2, retrievability: 1.0, signals: naNSignals),
            Candidate(3, retrievability: 0.8, signals: naNSignals),
            Candidate(4, retrievability: 0.6, signals: naNSignals),
        };

        var ranked = policy.Rank([unrecoverable, .. poisonedButRetrievable], in Context);

        Assert.All(ranked, r => Assert.True(double.IsFinite(r.Score),
            $"non-finite score returned for candidate {r.Candidate.Node.Id}"));
        Assert.DoesNotContain(ranked, r => r.Candidate.Node.Id == 1);
        // the group with recoverable retrievability must still come back — losing the coercion would empty
        // this too, not merely the one candidate that was unrecoverable regardless
        Assert.NotEmpty(ranked);
    }

    /// <summary><c>+Infinity</c> is a SHARPER version of the fact above, not a redundant one: <c>NaN</c>
    /// compares false against every threshold and so reliably sorts to the BOTTOM
    /// (<see cref="double.CompareTo(double)"/> treats it as the smallest value), which is why the previous
    /// fact's poisoned candidate never becomes the set's own <c>best</c> and never poisons anyone else's
    /// floor. <c>+Infinity</c> does the OPPOSITE: <c>Infinity.CompareTo(anything finite)</c> is
    /// <c>1</c>, so it SURVIVES ordering as the undisputed best. A policy whose exclusion mechanism relies on
    /// "a poisoned score sorts to the bottom and fails its own floor" — true of NaN, false of Infinity — is
    /// defeated outright: the poisoned candidate becomes <c>best</c>, and every HEALTHY candidate's own floor
    /// check is then measured against an infinite <c>best</c> and fails. <b>Neither policy shipped in this
    /// library was actually safe against this until it was found</b> — <see cref="ReciprocalRankFusionPolicy"/>
    /// never multiplies a raw signal into its score, so this was academic there, but
    /// <see cref="MultiplicativeRankingPolicy"/>'s product propagates <c>+Infinity</c> exactly as described,
    /// and its own NaN-shaped exclusion (relying on the product turning NaN) does nothing for a value that
    /// stays a well-formed, orderable <c>+Infinity</c> the whole way through.
    /// <para><b>This fact covers the poisoned-INPUT class ONLY, and saying so is the correction that matters:
    /// as written it read as though it had closed the whole "a score can be non-finite" question, and it had
    /// not.</b> A score can overflow from inputs that are every one of them finite, which no filter over the
    /// inputs can see. That is
    /// <see cref="A_finite_input_whose_score_overflows_does_not_empty_a_healthy_recall"/>, below — and it
    /// is not academic for reciprocal rank fusion either.</para></summary>
    public static void A_non_finite_relevance_that_would_otherwise_be_best_does_not_empty_a_healthy_recall(
        IMemoryRankingPolicy policy)
    {
        // +Infinity relevance: the WORST case, because it is not merely poisoned, it is poisoned in the
        // direction that makes it `best` — every one of the three healthy candidates below is a genuinely
        // strong match, chosen so a working exclusion is the ONLY thing standing between them and being
        // wiped out by an infinite floor.
        var poisoned = Candidate(1, relevance: double.PositiveInfinity);
        var healthy = new[]
        {
            Candidate(2, relevance: 0.9, retrievability: 0.9),
            Candidate(3, relevance: 0.5, retrievability: 0.5),
            Candidate(4, relevance: 0.1, retrievability: 0.1),
        };

        var ranked = policy.Rank([poisoned, .. healthy], in Context);

        Assert.All(ranked, r => Assert.True(double.IsFinite(r.Score),
            $"non-finite score returned for candidate {r.Candidate.Node.Id}"));
        Assert.DoesNotContain(ranked, r => r.Candidate.Node.Id == 1);
        Assert.NotEmpty(ranked); // THE assertion an infinite floor would violate — every healthy candidate cut
    }

    /// <summary>The fact above closes the poisoned-INPUT class; this one closes the poisoned-PRODUCT class,
    /// and an input filter alone cannot reach it. <c>1e308</c> is a perfectly FINITE
    /// <see cref="GraphNode.Relevance"/> and a perfectly finite <see cref="MemoryCandidate.Retrievability"/> —
    /// both pass every <c>double.IsFinite</c> check a policy applies to its INPUTS — and their product is
    /// <c>+Infinity</c> all the same. The failure that follows is the identical one the input filter was built
    /// for: the overflowed score becomes <c>best</c>, <c>floor</c> becomes <c>+Infinity</c> (or <c>NaN</c>,
    /// where the floor fraction is <c>0</c> — <c>Infinity × 0</c> is <c>NaN</c> by IEEE 754, and every
    /// comparison against it is false), and the whole healthy set is cut. So the guarantee has to be enforced
    /// on the SCORE, after it is computed, not only on what went into it.
    /// <para><b>Note what this fact deliberately does NOT assert: that the overflowing candidate is
    /// dropped.</b> Whether a huge-but-finite input is returned is the policy's own business —
    /// <see cref="ReciprocalRankFusionPolicy"/> ranks by POSITION, so a <c>1e308</c> relevance is simply the
    /// best relevance rank and produces an ordinary finite score, and dropping it would be wrong. What is
    /// contractual is only that no returned score is non-finite and that a healthy candidate is never cut by
    /// somebody else's overflow.</para></summary>
    public static void A_finite_input_whose_score_overflows_does_not_empty_a_healthy_recall(
        IMemoryRankingPolicy policy)
    {
        // FINITE on both members, and their product is +Infinity — the whole point of this fact.
        var overflowing = Candidate(1, relevance: 1e308, retrievability: 1e308);
        // Three healthy candidates spread narrowly enough that NO shipped relative floor buries any of them
        // on its own (worst/best = 0.49/0.81 ≈ 0.60, far above Multiplicative's 0.02 default), so a missing
        // candidate below can only be the overflow's doing.
        var healthy = new[]
        {
            Candidate(2, relevance: 0.9, retrievability: 0.9),
            Candidate(3, relevance: 0.8, retrievability: 0.8),
            Candidate(4, relevance: 0.7, retrievability: 0.7),
        };

        var ranked = policy.Rank([overflowing, .. healthy], in Context);

        Assert.All(ranked, r => Assert.True(double.IsFinite(r.Score),
            $"non-finite score returned for candidate {r.Candidate.Node.Id}"));
        foreach (var id in new long[] { 2, 3, 4 })
            Assert.Contains(ranked, r => r.Candidate.Node.Id == id);
    }
}
