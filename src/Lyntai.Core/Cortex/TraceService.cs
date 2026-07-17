using System.Diagnostics;
using Lyntai.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Cortex;

/// <summary>Default trace service. Steps accumulate in memory per recorder; CompleteAsync persists
/// through <see cref="ITraceStore"/> when one is wired (no store → tracing is a structured no-op).
/// The clock is injectable for deterministic tests.</summary>
public sealed class TraceService(
    ITraceStore? store = null,
    Func<DateTimeOffset>? clock = null,
    ILogger<TraceService>? logger = null) : ITraceService
{
    private readonly ITraceStore? _store = store;
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly ILogger _logger = logger ?? NullLogger<TraceService>.Instance;

    public ITraceRecorder Begin(string sessionId, string mode) =>
        // capture the ambient distributed-trace id NOW (at Begin), so the persisted run trace
        // cross-references the OTel trace even after the activity has ended
        new Recorder(this, sessionId, mode, _clock(), CurrentTraceId());

    private static string? CurrentTraceId()
    {
        var traceId = Activity.Current?.TraceId.ToString();
        return string.IsNullOrEmpty(traceId) || traceId == "00000000000000000000000000000000" ? null : traceId;
    }

    public async Task<RunTrace?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        if (_store is null) return null;
        return await _store.GetAsync(sessionId, ct).ConfigureAwait(false);
    }

    private sealed class Recorder(TraceService owner, string sessionId, string mode, DateTimeOffset startedAt, string? traceId) : ITraceRecorder
    {
        private readonly List<TraceStep> _steps = [];
        private readonly Lock _lock = new();

        public string SessionId => sessionId;

        public void Record(TraceStep step)
        {
            lock (_lock) _steps.Add(step);
        }

        public async Task CompleteAsync(CancellationToken ct = default)
        {
            if (owner._store is null) return; // fail-open: tracing without storage is a no-op

            RunTrace trace;
            lock (_lock)
            {
                trace = new RunTrace
                {
                    SessionId = sessionId,
                    Mode = mode,
                    StartedAt = startedAt,
                    EndedAt = owner._clock(),
                    TraceId = traceId,
                    Steps = [.. _steps],
                };
            }
            try
            {
                await owner._store.SaveAsync(trace, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                owner._logger.LogWarning(ex, "trace persistence failed for {Session} (fail-open)", sessionId);
            }
        }
    }
}
