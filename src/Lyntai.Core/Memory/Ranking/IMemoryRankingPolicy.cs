namespace Lyntai.Memory.Ranking;

/// <summary>
/// Turns a set of recall candidates into a scored, best-first order — the seam that used to be a formula
/// hardcoded inside <see cref="Lyntai.Memory.Engines.GraphMemoryEngine"/>'s own recall path. Swappable and
/// DI-resolved, the same variation-point shape as
/// <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy"/>: a custom ranking is a class plus a
/// registration, never an edit to the engine.
/// <para><b>Set-based, not per-candidate.</b> <see cref="Rank"/> takes the whole candidate list at once
/// because a fusion policy cannot compute one candidate's contribution without seeing where every OTHER
/// candidate falls on the same signal — reciprocal rank fusion, this domain's second implementation, needs
/// each candidate's RANK POSITION within the set, which does not exist for a candidate scored alone.</para>
/// <para><b>The score's SCALE is the policy's own business, and no caller may compare scores across
/// policies.</b> A multiplicative policy's score is a bounded product of factors each roughly in [0,1]; a
/// rank-fusion policy's score is a sum of reciprocal ranks with no such bound and no probabilistic reading
/// at all. Treating either as if it meant the other — a probability, a confidence, anything portable — is
/// the one use this seam does not support. Only the ORDER a policy produces, and whether a returned
/// candidate cleared whatever floor produced that order, are contractual.</para>
/// </summary>
public interface IMemoryRankingPolicy
{
    /// <summary>
    /// Score and order <paramref name="candidates"/>, best first.
    /// <para><b>May floor, never invent.</b> A policy is free to drop candidates it judges too weak to
    /// surface, against its own best score — but every returned <see cref="RankedMemory"/> must wrap a
    /// candidate that was actually passed in, and none may repeat. The floor is the policy's own business,
    /// because floor semantics only mean something alongside the score scale that produced them — what the
    /// floor does NOT own is any EXEMPTION from it. A caller that must never lose a specific candidate (an
    /// authoritative fact, say) re-admits it itself, afterwards; trust in that guarantee has to hold against
    /// a policy that DROPS a candidate outright, including a third-party one that has never heard of the
    /// concept, so it cannot live here. See <see cref="Lyntai.Memory.Engines.GraphMemoryEngine"/>'s own
    /// recall for where that re-admission happens — and for why it can only ever check by identity: the
    /// re-admission there is keyed on <c>Node.Id</c> alone, which is what makes "never invent" above load-
    /// bearing rather than a nicety. A policy that returned a FABRICATED <see cref="RankedMemory"/> under an
    /// authoritative candidate's own id (rather than dropping it) would already violate "never invent" — but
    /// nothing downstream independently verifies that clause, so such a policy would ALSO silently defeat the
    /// re-admission promise above: the caller sees the id as "already returned" and never re-admits the real
    /// entry. The re-admission guarantee is therefore trustworthy against a merely forgetful or hostile-by-
    /// omission policy, never against one that actively fabricates.</para>
    /// <para><b>Deterministic.</b> Two candidates a policy scores identically still need a stable order —
    /// <c>List.Sort</c> is unstable, so a tiebreak is mandatory, not decorative — or the same query returns
    /// a different page on successive calls. An empty input ranks to an empty result. No returned score may
    /// be non-finite: a <see cref="double.NaN"/> compares false against every later threshold, so it would
    /// silently swallow whatever compared against it downstream rather than failing loudly.</para>
    /// </summary>
    /// <param name="candidates">Every candidate under consideration; never mutated.</param>
    /// <param name="context">What the caller ultimately wants alongside the candidates themselves — see
    /// <see cref="MemoryRankingContext"/>.</param>
    IReadOnlyList<RankedMemory> Rank(IReadOnlyList<MemoryCandidate> candidates, in MemoryRankingContext context);
}

/// <summary>
/// One recall candidate as the ranking seam sees it — everything a policy may read to score it, no more.
/// </summary>
/// <param name="Node">The stored entry. Its <see cref="GraphNode.Relevance"/> and
/// <see cref="GraphNode.Signals"/> are, alongside the two members below, the usual raw material a policy
/// scores from.</param>
/// <param name="Retrievability">This entry's current retrievability, in [0,1] — already evaluated by the
/// caller's own <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy"/>. A ranking policy never
/// evaluates decay itself; it only reads what has already been decided.</param>
/// <param name="Hop">How many edges out from the seed set this candidate was reached through — 0 for a
/// direct hit.</param>
public readonly record struct MemoryCandidate(GraphNode Node, double Retrievability, int Hop);

/// <summary>What the caller ultimately wants, passed alongside the candidates themselves rather than folded
/// into them because it describes the CALL, not any one candidate.</summary>
/// <param name="Limit">How many items the caller intends to keep once ranking (and anything it does
/// afterwards) is done. <b>Advisory, not a command a policy must enforce</b> —
/// <see cref="Lyntai.Memory.Engines.GraphMemoryEngine"/> applies the limit itself, AFTER re-admitting
/// anything it must not lose, so a policy that already truncated its own output to this count would make
/// that re-admission silently lossy. A policy may still read it — to bound its own work, for instance — so
/// long as it never returns fewer candidates than its own floor would otherwise keep.</param>
/// <param name="Engine">The calling engine's name. Diagnostic context only: no shipped policy's score
/// depends on it, and none should — a policy that behaved differently per engine name would make ranking
/// unpredictable from the option values alone.</param>
public readonly record struct MemoryRankingContext(int Limit, string Engine);

/// <summary>One candidate after scoring.</summary>
/// <param name="Candidate">The candidate this score belongs to, unchanged from what was passed in.</param>
/// <param name="Score">The policy's own scale — see <see cref="IMemoryRankingPolicy"/>'s remarks on why a
/// score is never comparable across policies.</param>
public readonly record struct RankedMemory(MemoryCandidate Candidate, double Score);
