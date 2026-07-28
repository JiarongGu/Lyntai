namespace Lyntai.Cortex;

/// <summary>One eval dimension. Implementations are registered into the DI collection
/// (<c>builder.AddScorer&lt;T&gt;()</c>) and iterated by <see cref="IScoringService"/> — adding a
/// dimension is a new class + one registration, never a switch.</summary>
public interface IScorer
{
    string Id { get; }
    string Name { get; }

    /// <summary>An optional human-readable description of what this dimension measures — for an admin
    /// "list scorers" view. Defaults to empty (opt in by overriding).</summary>
    string Description => "";

    /// <summary>Grouping key for aggregation/reporting (e.g. "deterministic", "llm").</summary>
    string Group { get; }

    /// <summary>True when scoring spends LLM tokens (callers may skip these in tight loops).</summary>
    bool IsLlm { get; }

    /// <summary>Whether this scorer runs for the given context at all. Default true; override to gate a
    /// dimension declaratively (checked by IScoringService BEFORE ScoreAsync, so a skipped scorer spends no
    /// work). Distinct from ScoreAsync returning null, which means "ran, but produced no score for this
    /// context" — both are skipped and neither persists.</summary>
    bool Applies(ScoreContext ctx) => true;

    /// <summary>Score the context. Return null when the scorer RAN but produced no score for this context
    /// (not applicable) — the result is skipped and not persisted. To gate the dimension declaratively
    /// (skip it before any work), override <see cref="Applies"/> instead.</summary>
    Task<ScoreResult?> ScoreAsync(ScoreContext ctx, CancellationToken ct = default);
}
