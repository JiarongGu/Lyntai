using Lyntai.Cortex;

namespace Lyntai.Storage;

/// <summary>Persisted run traces + steps, keyed by session. Saving a session again replaces its trace.
/// <para><b>A step's stored ordinal is its <see cref="TraceStep.Sequence"/> where a recorder set one, and
/// its LIST POSITION otherwise</b>, and a read returns the steps in that order. The fallback is what makes a
/// hand-built trace — every <c>Sequence</c> left at its default — persist in a monotonic, distinct order
/// rather than collapsing onto one ordinal. It is decided per step, so a trace mixing recorded and unset
/// sequences can store two steps at one ordinal, which keep their list order.
/// <see cref="TraceOrdinals.Stored"/> is the rule, for every backend.</para>
/// <para><b>An ordinal is 32-bit</b>: <see cref="SaveAsync"/> throws <see cref="ArgumentException"/> for a
/// <see cref="TraceStep.Sequence"/> outside <see cref="int"/>'s range and writes nothing, on every backend.</para>
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
    /// <exception cref="ArgumentException">A step's <see cref="TraceStep.Sequence"/> is outside <see cref="int"/>'s
    /// range, which Postgres's <c>INTEGER</c> ordinal cannot hold. Call this before writing anything.</exception>
    public static IReadOnlyList<TraceStep> Stored(IReadOnlyList<TraceStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.FirstOrDefault(s => s.Sequence is > int.MaxValue or < int.MinValue) is { } past)
            throw new ArgumentException(
                $"Step '{past.Label}' has Sequence {past.Sequence}, outside the 32-bit range a trace store holds.", nameof(steps));
        return [.. steps
            .Select((step, index) => (Step: step with { Sequence = step.Sequence != 0 ? step.Sequence : index }, Index: index))
            .OrderBy(s => s.Step.Sequence)
            .ThenBy(s => s.Index)
            .Select(s => s.Step)];
    }
}
