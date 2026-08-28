namespace Lyntai.Memory.Ranking;

/// <summary>Constants of <see cref="MultiplicativeRankingPolicy"/>'s formula. Every default here, and every
/// incident referenced in the guards below, was first measured inside
/// <see cref="Lyntai.Memory.Engines.GraphMemoryEngine"/>'s own hardcoded rank projection — this record gives
/// those numbers a name and a construction-time guard; it does not change what any of them mean.</summary>
public sealed record MultiplicativeRankingOptions
{
    private readonly double _hopAttenuation = 0.5;
    private readonly double _relativeFloor = 0.02;
    private readonly double _salienceRankWeight = 0;

    /// <summary>Rank attenuation per hop, as the base of <c>HopAttenuation^hop</c>: material one hop out is
    /// worth this fraction of a direct match.
    /// <para><b>Must be FINITE and in <c>(0, 1]</c>.</b> At <c>1</c> hop distance stops mattering at all —
    /// degenerate, but a deliberate and safe setting, so it is the inclusive upper bound rather than an
    /// excluded one; above <c>1</c> a candidate FARTHER from the seed scores HIGHER than a direct hit, the
    /// exact inversion of what this knob promises ("worth this FRACTION of a direct match" stops meaning a
    /// fraction at all past 1). At exactly <c>0</c> every candidate beyond hop 0 scores precisely zero
    /// regardless of how well it matched or how retrievable it is — not "heavily attenuated", GONE, a much
    /// harsher claim than the name suggests, and with the shipped <see cref="RelativeFloor"/> above zero
    /// those candidates would be buried anyway with no signal that the CAUSE was hop distance rather than
    /// weakness. A NEGATIVE base does not merely weaken attenuation: <c>Math.Pow</c> alternates the result's
    /// SIGN by hop parity (positive at hop 0, negative at hop 1, positive at hop 2, …), so a hop-1 candidate
    /// would score negative while a hop-2 candidate scores positive again — the ordering stops being
    /// monotone in hop distance at all.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to zero, a negative value, a value above 1, or a
    /// non-finite value (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double HopAttenuation
    {
        get => _hopAttenuation;
        init => _hopAttenuation = MemoryOption.Require(value, MemoryOptionRange.ToInclusive(0, 1), nameof(MultiplicativeRankingOptions),
            "see the property's XML doc for why zero eliminates every candidate beyond the seed, why a "
            + "value above 1 makes a farther candidate outrank a direct hit, and why a negative value flips "
            + "the sign of the result by hop parity rather than merely weakening it.");
    }

    /// <summary>How far below the STRONGEST candidate's score an entry may fall before this policy drops
    /// it, as a fraction of that best score.
    /// <para><b>Must be FINITE and in <c>[0, 1)</c>.</b> <c>0</c> is allowed and means the floor is off —
    /// every candidate with a non-negative score survives, which is the deliberate "no burial" setting, not
    /// an error. At <c>1</c> or above the floor equals or exceeds the very score that defines it, so only
    /// candidates tied exactly with the maximum survive — "bury what falls far enough below the best"
    /// silently collapsing into "keep almost nothing", with no exception or empty-result signal pointing at
    /// the cause. A negative value would lower the floor below zero; the ranking loop below already
    /// tolerates that defensively (<c>Math.Max(0, RelativeFloor)</c>), but silently clamping a
    /// plainly-mistaken configuration forever, on every recall, is worse than refusing it once at the line
    /// that set it.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a negative value, a value of 1 or above, or a
    /// non-finite value (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double RelativeFloor
    {
        get => _relativeFloor;
        init => _relativeFloor = MemoryOption.Require(value, MemoryOptionRange.FromInclusive(0, 1), nameof(MultiplicativeRankingOptions),
            "see the property's XML doc for why 1 or above collapses the floor into \"keep almost "
            + "nothing\" with no signal anywhere, and why a negative value is refused rather than silently "
            + "clamped forever.");
    }

    /// <summary>How steeply salience lifts a candidate's score, as <c>1 + weight × ln(salience)</c>. See
    /// <c>docs/DECISIONS.md</c> D45 for the full reasoning: <c>0</c> by default because salience means
    /// "does not fade away", not "first priority", so ranking stays untouched unless a consumer opts in.
    /// <para><b>Must be FINITE and <c>&gt;= 0</c>.</b> Salience is coerced to <c>&gt;= 1</c> before this
    /// formula ever sees it (<see cref="MemorySignals.Salience"/>), so <c>ln(salience) &gt;= 0</c> always —
    /// a NEGATIVE weight therefore does not merely weaken the boost, it INVERTS it: the term drops BELOW 1
    /// as salience rises, so a MORE salient entry ranks LOWER than a neutral one, the exact opposite of
    /// "does not fade away". No upper bound: a larger weight only lifts a salient candidate further, which
    /// is a monotone strengthening of the same effect, never a reversal.</para>
    /// <para><b>That "no upper bound" is a claim about the ORDER, not about the arithmetic, and the
    /// difference is load-bearing.</b> <c>1 + weight × ln(salience)</c> is a product of two unbounded
    /// quantities, so a weight near <see cref="double.MaxValue"/> against a salience above <c>e</c> overflows
    /// <c>boost</c> to <c>+Infinity</c> and the whole score with it — from a weight this guard ACCEPTS and a
    /// salience <see cref="MemorySignals.Salience"/> is happy to report. It is not left to the caller to
    /// avoid: <see cref="MultiplicativeRankingPolicy"/> drops any candidate whose own score comes out
    /// non-finite, so an overflowing weight costs that candidate its place rather than emptying the recall
    /// around it. The bound stays absent deliberately — a legitimate large weight is a legitimate
    /// configuration, and refusing one at construction would trade a real use for a failure the ranking loop
    /// already contains.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a negative value or a non-finite value
    /// (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double SalienceRankWeight
    {
        get => _salienceRankWeight;
        init => _salienceRankWeight = MemoryOption.Require(value, MemoryOptionRange.NonNegative, nameof(MultiplicativeRankingOptions),
            "see the property's XML doc for why a negative value inverts the salience boost (a more "
            + "salient entry ranking BELOW a neutral one) rather than merely weakening it.");
    }
}

/// <summary>
/// This domain's first implementation, given a name and a swap point rather than changed: <c>Score =
/// Relevance × Retrievability × boost × HopAttenuation^Hop</c>, where <c>boost</c> is <c>1</c> unless
/// <see cref="MultiplicativeRankingOptions.SalienceRankWeight"/> is opted above its shipped 0, then floored
/// against its own best score.
/// <para><b>No longer the registered default as of 3.0</b> —
/// <see cref="Lyntai.Memory.Ranking.ReciprocalRankFusionPolicy"/> is (<c>docs/DECISIONS.md</c> D49). It ships
/// unchanged and is one line to restore:
/// <c>services.AddSingleton&lt;IMemoryRankingPolicy&gt;(new MultiplicativeRankingPolicy())</c>, before or
/// after <c>AddLyntai</c> — either direction wins.</para>
/// <para><b>Owns the floor, not the grade exemption.</b> This policy drops whatever falls below its own floor
/// without regard for <see cref="MemoryGrade"/>; re-admitting a dropped authoritative candidate is the
/// CALLER's job (<see cref="Lyntai.Memory.Engines.GraphMemoryEngine"/>, by id) — see
/// <see cref="IMemoryRankingPolicy"/>'s own remarks.</para>
/// <para><b>Non-finiteness is filtered TWICE — on the inputs and again on the SCORE — and neither alone is
/// enough.</b> <c>+Infinity</c> is the dangerous value, not <c>NaN</c>: <c>NaN</c> sorts to the bottom, while
/// <c>+Infinity</c> becomes <c>best</c>, makes <c>floor</c> infinite too, and leaves <c>Score &gt;= floor</c>
/// true for that candidate ALONE — recall collapses to the corrupted entry rather than merely losing it. The
/// input filter closes only the poisoned-INPUT class: a score overflows from wholly FINITE inputs too
/// (<c>1e308 × 1e308</c>, or an unbounded <see cref="MultiplicativeRankingOptions.SalienceRankWeight"/>, which
/// comes from no candidate at all), so a non-finite SCORE drops its own candidate and nobody else's. BYO-only
/// exposure: every shipped store reports <see cref="GraphNode.Relevance"/> in <c>(0,1]</c> and every shipped
/// policy clamps to <c>[0,1]</c>.</para>
/// </summary>
/// <param name="options">Constants; null takes the defaults.</param>
public sealed class MultiplicativeRankingPolicy(MultiplicativeRankingOptions? options = null) : IMemoryRankingPolicy
{
    private readonly MultiplicativeRankingOptions _options = options ?? new MultiplicativeRankingOptions();

    /// <inheritdoc />
    public IReadOnlyList<RankedMemory> Rank(IReadOnlyList<MemoryCandidate> candidates,
        in MemoryRankingContext context)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0) return [];

        // The contract's own input filter — see MemoryRankingContract.Rankable for the shape of the failure
        // it closes (one +Infinity candidate becomes `best` and cuts every healthy one).
        var rankable = MemoryRankingContract.Rankable(candidates);
        if (rankable.Count == 0) return [];

        var scored = new List<RankedMemory>(rankable.Count);
        foreach (var c in rankable)
        {
            // MemorySignals.Salience is the ONE definition, shared with all three graph stores. A bare
            // Math.Max(1, …) on the raw bag value is NOT enough: a stored value below 1 would REDUCE the
            // score (nothing in the model means "less findable than neutral"), and a non-finite one makes
            // `boost`, and so the whole product, NaN — Math.Max(1, NaN) is NaN by IEEE 754.
            var salience = MemorySignals.Salience(c.Node.Signals);
            var boost = _options.SalienceRankWeight <= 0
                ? 1
                : 1 + _options.SalienceRankWeight * Math.Log(salience);
            // A candidate nobody asked a relevance question of contributes NO relevance factor — 1, the
            // multiplicative identity — rather than its unearned score. Both alternatives are defects this
            // repository measured (D97): the stores used to report a fabricated 1 here, which let an
            // unscored candidate beat every scored one, and reporting 0 instead ANNIHILATES it, because a
            // zero factor deletes the product rather than ranking it low. A graph walk's own signal is
            // HopAttenuation below, which still applies — this omission neither credits nor destroys it.
            var relevance = c.Node.Matched is null ? 1 : c.Node.Relevance;
            var score = relevance * c.Retrievability * boost * Math.Pow(_options.HopAttenuation, c.Hop);
            // THE POST-HOC GUARD, and the input filter above genuinely cannot do its job: Relevance = 1e308
            // and Retrievability = 1e308 both pass `double.IsFinite` and multiply to +Infinity, and
            // SalienceRankWeight (validated finite and >= 0, deliberately unbounded above) overflows `boost`
            // the same way against a salience above e. See MemoryRankingContract.AddIfFinite.
            MemoryRankingContract.AddIfFinite(scored, c, score);
        }

        // The deterministic id tiebreak and the buried-not-cut floor, both contract obligations rather than
        // this policy's own taste — one definition, shared with every policy in this domain.
        return MemoryRankingContract.Finish(scored, _options.RelativeFloor);
    }
}
