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
