namespace Lyntai.Cortex;

/// <summary>Accumulates steps for one run; <see cref="CompleteAsync"/> stamps the end time and
/// persists the trace (a no-op when no trace store is wired — tracing is fail-open).</summary>
public interface ITraceRecorder
{
    string SessionId { get; }

    void Record(TraceStep step);

    Task CompleteAsync(CancellationToken ct = default);
}

/// <summary>Run-trace timeline service: begin a recorder per run, read a persisted trace back.</summary>
public interface ITraceService
{
    ITraceRecorder Begin(string sessionId, string mode);

    Task<RunTrace?> GetAsync(string sessionId, CancellationToken ct = default);
}
