using Lyntai.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Cortex;

/// <summary>Iterates the DI collection of scorers (never an if/else over kinds), skips null/faulted
/// results, and persists the aggregate when a score store is wired.</summary>
public sealed class ScoringService(
    IEnumerable<IScorer> scorers,
    IScoreStore? store = null,
    ILogger<ScoringService>? logger = null) : IScoringService
{
    private readonly ILogger _logger = logger ?? NullLogger<ScoringService>.Instance;

    public Task<IReadOnlyList<ScoredResult>> EvaluateAsync(ScoreContext ctx, CancellationToken ct = default) =>
        EvaluateAsync(ctx, persist: true, ct);

    public async Task<IReadOnlyList<ScoredResult>> EvaluateAsync(ScoreContext ctx, bool persist, CancellationToken ct = default)
    {
        var results = new List<ScoredResult>();
        foreach (var scorer in scorers)
        {
            try
            {
                var r = await scorer.ScoreAsync(ctx, ct).ConfigureAwait(false);
                if (r is null) continue; // not applicable to this context
                results.Add(new ScoredResult(scorer.Id, scorer.Name, scorer.Group, scorer.IsLlm, r.Score, r.Reason));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "scorer {Scorer} faulted; skipped (fail-open)", scorer.Id);
            }
        }

        if (persist && store is not null && results.Count > 0)
        {
            try
            {
                await store.SaveAsync(ctx.SessionId, results, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "score persistence failed for {Session}; results still returned", ctx.SessionId);
            }
        }
        return results;
    }

    // read/aggregate/export through the service seam (a dashboard injects IScoringService, not IScoreStore);
    // empty when no store is wired, mirroring how ITraceService wraps its store.
    public Task<IReadOnlyList<ScoredResult>> GetAsync(string sessionId, CancellationToken ct = default) =>
        store is null ? Task.FromResult<IReadOnlyList<ScoredResult>>([]) : store.GetAsync(sessionId, ct);

    public Task<IReadOnlyList<ScorerAggregate>> AggregateAsync(CancellationToken ct = default) =>
        store is null ? Task.FromResult<IReadOnlyList<ScorerAggregate>>([]) : store.AggregateAsync(ct);

    public Task<IReadOnlyList<ScoreExportRow>> ExportAsync(CancellationToken ct = default) =>
        store is null ? Task.FromResult<IReadOnlyList<ScoreExportRow>>([]) : store.ExportAsync(ct);
}
