namespace Lyntai.Cortex;

/// <summary>Accumulates steps for one run; <see cref="CompleteAsync"/> stamps the end time and
/// persists the trace (a no-op when no trace store is wired — tracing is fail-open).</summary>
public interface ITraceRecorder
{
    string SessionId { get; }

    void Record(TraceStep step);

    Task CompleteAsync(CancellationToken ct = default);
}

/// <summary>Run-trace timeline service: begin a recorder per run, read a persisted trace back.
/// <para><b>This is a BYO / app-driven recording API</b> — the app calls <see cref="Begin"/> and
/// <see cref="ITraceRecorder.Record"/> to build a queryable, persisted <see cref="RunTrace"/> timeline in
/// its <see cref="Lyntai.Storage.ITraceStore"/> (SQLite/Postgres/InMemory). Lyntai's batteries-included
/// flows (<c>ChatOrchestrator</c>, <c>ToolLoop</c>, the agent session) deliberately do NOT auto-populate it:
/// the AUTOMATIC observability path is the OpenTelemetry <c>Activity</c> spans they already emit on the
/// <c>Lyntai.Llm</c> / <c>Lyntai.Agents</c> sources (see <see cref="Lyntai.Diagnostics.LyntaiDiagnostics"/>).
/// Use OTel for live tracing/metrics; use <see cref="ITraceService"/> when you want your OWN durable,
/// step-shaped run history keyed by your session id. Fail-open — no trace store wired → a no-op recorder.</para></summary>
public interface ITraceService
{
    ITraceRecorder Begin(string sessionId, string mode);

    Task<RunTrace?> GetAsync(string sessionId, CancellationToken ct = default);
}
