using System.Runtime.CompilerServices;

namespace Lyntai.Memory;

/// <summary>How far a walk goes and what it expands.</summary>
public sealed record MemoryWalkOptions
{
    /// <summary>How many of a step's new arrivals the DEFAULT selector expands. Configures
    /// <see cref="SeedSelector"/>'s default and nothing else — a caller supplying their own bounds it
    /// themselves.</summary>
    public int SeedsPerStep { get; init; } = 3;

    /// <summary>Edge depth INSIDE one expansion, passed straight to
    /// <see cref="IExpandableMemory.ExpandAsync"/>. Not the number of steps: a step is the outer unit, a hop
    /// is an edge.</summary>
    public int Hops { get; init; } = 1;

    /// <summary>Most distinct entries the walk may hold. Null derives twice what the first step returned,
    /// rather than asserting a constant nobody measured.
    /// <para>It is what makes the sequence FINITE: once it is reached nothing new can be discovered, so the
    /// walk ends whether or not the caller breaks.</para></summary>
    public int? MaxItems { get; init; }

    /// <summary>Which entries a step expands. Null takes the newly-discovered ones in arrival order, capped
    /// at <see cref="SeedsPerStep"/>.
    /// <para><b>It REPLACES the default outright, count included</b> — a supplied selector is not truncated
    /// to <see cref="SeedsPerStep"/>, so a caller wanting ten seeds sets only this.</para>
    /// <para><b>Do not order by <see cref="MemoryItem.Relevance"/>.</b> An expanded neighbour carries a
    /// relevance from a read that never asked one; <c>docs/DECISIONS.md</c> D97 is why that matters.
    /// <see cref="MemoryItem.Retrievability"/> and <see cref="MemoryItem.Degree"/> are meaningful.</para>
    /// </summary>
    public Func<MemoryWalkStep, IEnumerable<MemoryItem>>? SeedSelector { get; init; }
}

/// <summary>One step of a walk: what is held after it, and what it changed.</summary>
/// <param name="Number">1 is the recall; 2 and up are expansions.</param>
/// <param name="Items">Everything held so far, merged and capped, in arrival order.</param>
/// <param name="NewItems">What THIS step newly held — the default seed set for the next one.</param>
/// <param name="UpgradedCount">How many already-held entries this step raised from a headline to full content.
/// A recall returns headlines by default, so upgrading is half of what a step buys and a count of new entries
/// alone cannot see it.
/// <para>A walk whose query asked for <see cref="MemoryDetail.Full"/> has nothing to upgrade — every entry
/// arrives whole — so this is 0 throughout and a step's whole value is what it DISCOVERS. The walk is
/// otherwise unaffected: it still expands, still discovers, and still ends when a step moves nothing.</para>
/// </param>
/// <param name="Ran">Which tiers produced this step, so an empty tier stays distinguishable from an absent
/// one.</param>
public sealed record MemoryWalkStep(
    int Number,
    IReadOnlyList<MemoryItem> Items,
    IReadOnlyList<MemoryItem> NewItems,
    int UpgradedCount,
    MemorySources Ran);

/// <summary>Walks an engine: a recall, then expansions outward from what it turned up — the mode a graph
/// engine is built for, since a recall returns headlines by default and detail is bought per entry.
/// <para>A caller that wants the entries WHOLE up front asks for <see cref="MemoryDetail.Full"/> on the
/// query instead (<b>D104</b>); expansion then buys discovery alone, which is a different question from
/// detail and the one a walk is actually for.</para>
/// <para>This composes <see cref="IMemoryEngine.RecallAsync"/> and
/// <see cref="IExpandableMemory.ExpandAsync"/> and adds nothing to either, the same way
/// <see cref="MemoryComposition"/> composes a recall and a rendering.</para></summary>
public static class MemoryWalk
{
    /// <summary>Walk <paramref name="engine"/>, yielding one step per round.
    /// <para>The caller's <c>break</c> is the stop condition, because the useful depth is a property of the
    /// QUESTION rather than a constant (<c>docs/DECISIONS.md</c> D100). The sequence is FINITE either way —
    /// it ends when a step moves nothing, or when <see cref="MemoryWalkOptions.MaxItems"/> is reached — so
    /// an <c>await foreach</c> with no break terminates on its own.</para>
    /// <para><b>Fails open</b>, like every recall-shaped read here. The first step is ALWAYS yielded, empty
    /// and reporting <see cref="MemorySources.None"/> when the recall faults. A later step that faults is
    /// not yielded and the walk ends with the last good one. Only
    /// <see cref="OperationCanceledException"/> propagates.</para>
    /// <para><b>A walk MUTATES, once per step</b> — recall reinforces what it returns and expansion
    /// reinforces what it walks, so <see cref="IMemoryEngine.RecallAsync"/>'s warning to anyone MEASURING
    /// applies per step rather than per call.</para>
    /// <para>An engine that is not <see cref="IExpandableMemory"/> yields exactly one step.</para></summary>
    /// <param name="engine">The engine to walk.</param>
    /// <param name="query">The first step's recall.</param>
    /// <param name="options">Depth, seeds and bound; null takes the defaults.</param>
    /// <param name="ct">Cancellation, which is never swallowed.</param>
    public static IAsyncEnumerable<MemoryWalkStep> WalkAsync(this IMemoryEngine engine, MemoryQuery query,
        MemoryWalkOptions? options = null, CancellationToken ct = default)
    {
        // Validation is EAGER, which is what the split below buys: an iterator's body does not run until the
        // first MoveNextAsync, so a null argument would otherwise surface at the caller's `await foreach`
        // rather than where the mistake was made.
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(query);
        return Walk(engine, query, options ?? new MemoryWalkOptions(), ct);
    }

    private static async IAsyncEnumerable<MemoryWalkStep> Walk(IMemoryEngine engine, MemoryQuery query,
        MemoryWalkOptions opts, [EnumeratorCancellation] CancellationToken ct)
    {
        MemoryRecall recall;
        try
        {
            recall = await engine.RecallAsync(query, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            recall = MemoryRecall.Empty;
        }

        // Derived, not chosen: twice what the first step actually returned reproduces the benchmark
        // harnesses' `2 * RecallLimit` whenever step 1 fills its limit, and invents no constant.
        var state = new MemoryWalkState(opts.MaxItems ?? recall.Items.Count * 2);

        state.BeginStep();
        foreach (var item in recall.Items) state.Hold(item);
        var step = new MemoryWalkStep(1, [.. state.Items], [.. state.NewItems], state.UpgradedCount, recall.Ran);
        yield return step;

        if (engine is not IExpandableMemory expandable) yield break;
        var select = opts.SeedSelector ?? (s => s.NewItems.Take(opts.SeedsPerStep));

        for (var number = 2; ; number++)
        {
            var seeds = select(step).ToList();
            if (seeds.Count == 0) yield break;

            state.BeginStep();
            var ran = MemorySources.None;
            var faulted = false;

            foreach (var seed in seeds)
            {
                ct.ThrowIfCancellationRequested();
                MemoryRecall near;
                // `yield return` may not sit inside a try/catch, so the fault is carried out as a flag
                try
                {
                    near = await expandable.ExpandAsync(seed.Reference, opts.Hops, null, ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    faulted = true;
                    break;
                }

                ran |= near.Ran;
                foreach (var item in near.Items) state.Hold(item);
            }

            // A half-built step would misreport NewItems in the one direction a caller cannot detect, so
            // the last GOOD step stands instead.
            if (faulted) yield break;

            step = new MemoryWalkStep(number, [.. state.Items], [.. state.NewItems], state.UpgradedCount, ran);
            if (step.NewItems.Count == 0 && step.UpgradedCount == 0) yield break;
            yield return step;
        }
    }
}

/// <summary>A walk's accumulated entries. Holds the merge rule, which is the part a hand-rolled loop gets
/// wrong: a step both DISCOVERS entries and UPGRADES held ones from a headline to full content.
/// <para>Implementation detail, not consumer surface; the tests reach it through
/// <c>InternalsVisibleTo</c>.</para></summary>
internal sealed class MemoryWalkState(int maxEntries)
{
    private readonly Dictionary<MemoryRef, int> _at = [];
    private readonly List<MemoryItem> _held = [];
    private readonly List<MemoryItem> _new = [];

    public IReadOnlyList<MemoryItem> Items => _held;

    public IReadOnlyList<MemoryItem> NewItems => _new;

    public int UpgradedCount { get; private set; }

    /// <summary>Start a step's bookkeeping. <see cref="Items"/> accumulates across steps;
    /// <see cref="NewItems"/> and <see cref="UpgradedCount"/> describe one step only.</summary>
    public void BeginStep()
    {
        _new.Clear();
        UpgradedCount = 0;
    }

    public void Hold(MemoryItem item)
    {
        // Keyed on the whole MemoryRef: a composite's members can each own id "1", and collapsing those
        // loses a fact with nothing reporting it.
        if (_at.TryGetValue(item.Reference, out var at))
        {
            // The upgrade is SEMANTIC, not by length — an authored headline can outrun its own content.
            if (_held[at].Content is null && item.Content is not null)
            {
                _held[at] = item;
                UpgradedCount++;
            }
            return;
        }

        if (_held.Count >= maxEntries) return;   // gates DISCOVERIES only; the branch above already returned
        _at[item.Reference] = _held.Count;
        _held.Add(item);
        _new.Add(item);
    }
}
