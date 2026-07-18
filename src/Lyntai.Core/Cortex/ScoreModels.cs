namespace Lyntai.Cortex;

/// <summary>What a scorer evaluates: the session it belongs to, the exchange, and any extra
/// dimensions a custom scorer may need.</summary>
public sealed record ScoreContext
{
    public required string SessionId { get; init; }
    public string? Input { get; init; }
    public string? Output { get; init; }

    /// <summary>The extension point for DOMAIN dimensions a custom <see cref="IScorer"/> reads beyond
    /// input/output — e.g. <c>phase</c>, <c>mode</c>, <c>changed_files</c>. Deliberately stringly-typed so
    /// the library stays domain-agnostic: a scorer defines its own keys and parses the values it needs.
    /// NON-scalar values (lists, objects) must be SERIALIZED by the caller (JSON, or a delimiter the scorer
    /// splits on) — this is a flat string map, not a typed bag. The app owns the key catalog; Lyntai just
    /// carries it through to the scorers.</summary>
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
