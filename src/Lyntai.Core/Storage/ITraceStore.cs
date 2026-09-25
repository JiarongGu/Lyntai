using Lyntai.Cortex;

namespace Lyntai.Storage;

/// <summary>Persisted run traces + steps, keyed by session. Saving a session again replaces its trace.
/// <para><b>A step's stored ordinal is its <see cref="TraceStep.Sequence"/> where a recorder set one, and
/// its LIST POSITION otherwise</b>, and a read returns the steps in that order. The fallback is what makes a
/// hand-built trace — every <c>Sequence</c> left at its default — persist in a monotonic, distinct order
/// rather than collapsing onto one ordinal. <see cref="TraceOrdinals.Stored"/> is the rule, for every
/// backend.</para>
/// </summary>
public interface ITraceStore
{
    Task SaveAsync(RunTrace trace, CancellationToken ct = default);

    Task<RunTrace?> GetAsync(string sessionId, CancellationToken ct = default);
}

/// <summary>The ONE spelling of <see cref="ITraceStore"/>'s ordinal rule — call it from any backend, a BYO one
/// included, so a trace reads back the same wherever it was saved.</summary>
public static class TraceOrdinals
{
    /// <summary>The steps exactly as a store returns them: each one's <see cref="TraceStep.Sequence"/> is its
    /// stored ordinal — its own where set, its list position otherwise — and the list is in stored order, list
    /// position breaking a tie.</summary>
    /// <param name="steps">The steps as the caller handed them to <see cref="ITraceStore.SaveAsync"/>.</param>
    public static IReadOnlyList<TraceStep> Stored(IReadOnlyList<TraceStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        return [.. steps
            .Select((step, index) => (Step: step with { Sequence = step.Sequence != 0 ? step.Sequence : index }, Index: index))
            .OrderBy(s => s.Step.Sequence)
            .ThenBy(s => s.Index)
            .Select(s => s.Step)];
    }
}
