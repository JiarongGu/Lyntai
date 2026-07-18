namespace Lyntai.Cortex;

/// <summary>What a scorer evaluates: the session it belongs to, the exchange, and any extra
/// dimensions a custom scorer may need.</summary>
public sealed record ScoreContext
{
    public required string SessionId { get; init; }
    public string? Input { get; init; }
    public string? Output { get; init; }
    public IReadOnlyDictionary<string, string>? Extra { get; init; }
}

/// <summary>One scorer's verdict: a 0..1 score plus an optional human-readable reason.</summary>
public sealed record ScoreResult(double Score, string? Reason = null);

/// <summary>A <see cref="ScoreResult"/> stamped with the scorer that produced it — the persisted shape.</summary>
public sealed record ScoredResult(string ScorerId, string ScorerName, string Group, bool IsLlm, double Score, string? Reason = null);

/// <summary>A cross-session aggregate for one scorer — its mean score over <paramref name="Count"/> scored
/// sessions (for the eval/tuning dashboard).</summary>
public sealed record ScorerAggregate(string ScorerId, string ScorerName, double AverageScore, int Count);

/// <summary>One row of the bulk score export — a flat <c>(session, scorer, score)</c> tuple, for building a
/// tuning dataset across every session.</summary>
public sealed record ScoreExportRow(string SessionId, string ScorerId, double Score);
