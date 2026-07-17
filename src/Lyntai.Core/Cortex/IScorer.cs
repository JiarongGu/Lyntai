namespace Lyntai.Cortex;

/// <summary>One eval dimension. Implementations are registered into the DI collection
/// (<c>builder.AddScorer&lt;T&gt;()</c>) and iterated by <see cref="IScoringService"/> — adding a
/// dimension is a new class + one registration, never a switch.</summary>
public interface IScorer
{
    string Id { get; }
    string Name { get; }

    /// <summary>Grouping key for aggregation/reporting (e.g. "deterministic", "llm").</summary>
    string Group { get; }

    /// <summary>True when scoring spends LLM tokens (callers may skip these in tight loops).</summary>
    bool IsLlm { get; }

    /// <summary>Score the context, or return null when this dimension doesn't apply.</summary>
    Task<ScoreResult?> ScoreAsync(ScoreContext ctx, CancellationToken ct);
}
