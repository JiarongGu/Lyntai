namespace Lyntai.Tests.Memory.Corpus;

/// <summary>
/// The measuring instrument that turns a live recall into two numbers a policy sweep can compare across
/// runs: how much of what mattered got left out (<see cref="MissRate"/>), and how much of the page the
/// caller actually asked for was spent on things that did not matter (<see cref="PollutionRate"/>). Both
/// are fractions of a FIXED reference rather than of whatever <c>recalled</c> happened to contain, so a
/// number from one corpus shape means the same thing as the same number from a different one — the whole
/// point of a sweep table that compares columns.
/// <para><b>Miss rate</b> — the fraction of the support an ANSWER needs that is absent from the recalled
/// ids: the whole declared-relevant set, or the <c>supportNeeded</c> threshold where a query's truth is a
/// frequency. <b>Pollution rate</b> — the fraction of the <c>limit</c>-sized window occupied by ids
/// outside the relevant set, which <c>supportNeeded</c> does not touch.</para>
/// <para><b>Four awkward cases are decided here, because the choice shifts every arm's number in the sweep
/// this instrument feeds — not just the one test case that exercises it:</b></para>
/// <list type="bullet">
/// <item><b>The relevant set is larger than <c>limit</c>.</b> The page cannot physically hold every
/// relevant entry, so miss rate floors at <c>max(0, needed - limit) / needed</c> — which is
/// <c>(|relevant| - limit) / |relevant|</c> under the default all-of scoring, and <b>0</b> whenever
/// <c>supportNeeded</c> fits inside the page, as the routine class's first query does. A reader of the
/// sweep table must read a near-floor miss rate in a large-<c>CandidateCount</c> arm as "as good as this
/// corpus shape allows", not as a failing policy, and must never compare two columns whose floors were
/// set by different thresholds.</item>
/// <item><b>The relevant set is smaller than <c>limit</c>.</b> A real recall engine ranks-and-cuts at
/// <c>limit</c>: whenever the store holds at least <c>limit</c> candidates (the normal case in this
/// harness's sweep), the returned page fills to <c>limit</c> regardless of how few of those candidates are
/// actually relevant. The slots beyond <c>|relevant|</c> are then unavoidably non-relevant, and this
/// instrument counts every one of them as pollution — deliberately: they ARE ids outside the relevant set
/// occupying a returned slot. So a pollution rate near the floor <c>(limit - |relevant|) / limit</c>
/// reflects the corpus shape, not the policy — the mirror image of the miss-rate floor above.</item>
/// <item><b>Empty recall.</b> Nothing came back. Every needed id is missed (<c>MissRate = 1</c> whenever
/// anything was needed) and nothing occupies any slot, so nothing can be pollution
/// (<c>PollutionRate = 0</c>) — no division by zero, because pollution's denominator is <c>limit</c>, never
/// the (possibly empty) recalled count.</item>
/// <item><b>Empty relevant set.</b> Nothing was ever declared relevant to this query — the corpus's
/// hot-ephemeral class reaches this legitimately once a round's window has closed. There is nothing to
/// miss, so <c>MissRate = 0</c> by convention rather than an undefined <c>0/0</c>, and that holds however
/// much support was asked for. Every recalled id is then, by definition, outside the (empty) relevant set,
/// so <c>PollutionRate</c> is NOT suppressed to 0 the way <c>MissRate</c> is; the two conventions are
/// asymmetric on purpose.</item>
/// </list>
/// </summary>
/// <param name="MissRate">Fraction of the support an answer needs that is absent from <c>recalled</c>, in
/// [0, 1] — the whole relevant set unless <c>supportNeeded</c> named a smaller threshold. 0 when nothing
/// was needed.</param>
/// <param name="PollutionRate">Fraction of the <c>limit</c>-sized window occupied by ids outside
/// <c>relevant</c>, in [0, 1]. 0 when <c>limit</c> is non-positive (no window to occupy).</param>
public readonly record struct RecallQuality(double MissRate, double PollutionRate)
{
    /// <summary>
    /// Measures one recall against its ground truth as of one point in a <see cref="MemoryCorpus"/>
    /// timeline. <paramref name="limit"/> MUST be the query's own requested count (the resolved
    /// <c>MemoryQuery.Limit</c>) — never a fixed constant. Hardcoding it would silently decouple the
    /// pollution rate from the very <c>CandidateCount</c> axis a sweep varies: the number would stop
    /// meaning "fraction of the window the caller actually asked for" and start meaning "fraction of some
    /// other window nobody requested."
    /// </summary>
    /// <param name="recalled">The ids actually returned. Order does not affect either metric — both are
    /// set-membership counts, not rank-position scores.</param>
    /// <param name="relevant">The ids declared relevant to this query AS OF THIS STEP in the corpus
    /// timeline (<see cref="CorpusQuery.RelevantIds"/>) — never a globally relevant set; the same id can be
    /// relevant at one step and not at a later one.</param>
    /// <param name="limit">The query's own requested count — the window pollution is measured against.
    /// </param>
    /// <param name="supportNeeded">How many of <paramref name="relevant"/> constitute an ANSWER, for a query
    /// whose truth is a FREQUENCY rather than a fact. <c>0</c> (the default) is the strict all-of scoring
    /// every other class uses and every published figure was measured under.
    /// <para><b>Why a threshold rather than any-of.</b> "What do you usually eat" is not answered by one
    /// episode — that says "on Tuesday I had noodles", not "I usually have noodles" — and it cannot be
    /// answered by all 300 either, since no window holds them. Both ends score a constant; the threshold is
    /// the only setting under which the metric can move.</para>
    /// <para>Clamped to <c>relevant.Count</c>: a fixture asking for more support than exists must not make
    /// its own query unanswerable, because that fixture bug would read as a system failure.</para></param>
    public static RecallQuality Measure(
        IReadOnlyList<string> recalled, IReadOnlyList<string> relevant, int limit, int supportNeeded = 0)
    {
        var relevantSet = new HashSet<string>(relevant, StringComparer.Ordinal);
        var recalledSet = new HashSet<string>(recalled, StringComparer.Ordinal);

        var found = relevantSet.Count(recalledSet.Contains);
        // 0 means "all of them", which is the shipped convention; anything else is clamped to what exists.
        var needed = supportNeeded <= 0
            ? relevantSet.Count
            : Math.Min(supportNeeded, relevantSet.Count);

        var missRate = needed == 0 ? 0.0 : Math.Max(0, needed - found) / (double)needed;

        var pollutionRate = limit <= 0
            ? 0.0
            : recalled.Count(id => !relevantSet.Contains(id)) / (double)limit;

        return new RecallQuality(missRate, pollutionRate);
    }
}
