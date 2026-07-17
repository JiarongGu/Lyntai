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

    public async Task<IReadOnlyList<ScoredResult>> EvaluateAsync(ScoreContext ctx, CancellationToken ct = default)
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

        if (store is not null && results.Count > 0)
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
}
