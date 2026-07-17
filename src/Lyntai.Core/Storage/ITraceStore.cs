using Lyntai.Cortex;

namespace Lyntai.Storage;

/// <summary>Persisted run traces + steps, keyed by session. Saving a session again replaces its trace.</summary>
public interface ITraceStore
{
    Task SaveAsync(RunTrace trace, CancellationToken ct = default);

    Task<RunTrace?> GetAsync(string sessionId, CancellationToken ct = default);
}
