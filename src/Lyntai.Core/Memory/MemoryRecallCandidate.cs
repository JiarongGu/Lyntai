using Lyntai.Memory.Seeding;

namespace Lyntai.Memory;

/// <summary>Why a recall candidate is, or is not, in <see cref="MemoryRecall.Items"/> — one value per cut
/// a recall actually makes, so a candidate is never reported cut by a step that did not run.</summary>
public enum MemoryCandidateOutcome
{
    /// <summary>It is in <see cref="MemoryRecall.Items"/>.</summary>
    Returned = 0,

    /// <summary>The ranking policy dropped it under its own floor, and nothing re-admitted it.</summary>
    Floored = 1,

    /// <summary>It ranked, and fell below the query's <see cref="MemoryQuery.Limit"/> — in a blend, possibly
    /// below the blend's own cut after its member returned it.</summary>
    CutByLimit = 2,

    /// <summary>A verifier did not endorse it, and <c>GraphMemoryOptions.VerificationFilters</c> removed
    /// it.</summary>
    FilteredByVerifier = 3,

    /// <summary>It fitted the limit and not the query's <see cref="MemoryQuery.CharBudget"/>.</summary>
    CutByBudget = 4,
}

/// <summary>One candidate a recall considered and what became of it — an entry of
/// <see cref="MemoryRecall.Candidates"/>, attached only when <see cref="MemoryQuery.Explain"/> asked.</summary>
/// <param name="Reference">The entry's address, naming the engine (or blend member) that considered it.</param>
/// <param name="Outcome">Whether it was returned, and if not, which cut removed it.</param>
/// <param name="Ranks">Which seed channels MATCHED it and at what 1-based rank within each; empty for a hop
/// neighbour or a candidate no channel ranked (<see cref="Lyntai.Memory.Ranking.MemoryCandidate.Ranks"/>).</param>
/// <param name="Hop">Edges out from the seed set it was reached through; 0 for a direct hit.</param>
/// <param name="Score">The ranking policy's score, on that policy's own scale and comparable within this one
/// recall only; null when the policy produced none for it.</param>
/// <param name="Position">1-based position in the order the limit was applied to — returned material first,
/// so a candidate cut by the limit says how far below the page it fell; null when it was never placed.</param>
/// <param name="Verified">The verifier's verdict: <c>true</c> endorsed, <c>false</c> shown and not endorsed,
/// <c>null</c> when no judgement happened or it lay beyond <c>GraphMemoryOptions.VerificationDepth</c>.</param>
public sealed record MemoryRecallCandidate(
    MemoryRef Reference,
    MemoryCandidateOutcome Outcome,
    MemorySeedRanks Ranks,
    int Hop,
    double? Score,
    int? Position,
    bool? Verified);
