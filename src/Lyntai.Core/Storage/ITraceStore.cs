using Lyntai.Cortex;

namespace Lyntai.Storage;

/// <summary>Persisted run traces + steps, keyed by session. Saving a session again replaces its trace.
/// <para><b>A step's stored ordinal is its <see cref="TraceStep.Sequence"/> where a recorder set one, and
/// its LIST POSITION otherwise.</b> The fallback is what makes a hand-built trace — every <c>Sequence</c>
/// left at its default — persist in a monotonic, distinct order rather than collapsing onto one
/// ordinal.</para>
/// </summary>
public interface ITraceStore
{
    Task SaveAsync(RunTrace trace, CancellationToken ct = default);

    Task<RunTrace?> GetAsync(string sessionId, CancellationToken ct = default);
}
