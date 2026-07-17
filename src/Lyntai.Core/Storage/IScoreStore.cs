using Lyntai.Cortex;

namespace Lyntai.Storage;

/// <summary>Persisted scorer results, grouped by session.</summary>
public interface IScoreStore
{
    Task SaveAsync(string sessionId, IReadOnlyList<ScoredResult> results, CancellationToken ct = default);

    Task<IReadOnlyList<ScoredResult>> GetAsync(string sessionId, CancellationToken ct = default);
}
