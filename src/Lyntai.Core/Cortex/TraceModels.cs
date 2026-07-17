namespace Lyntai.Cortex;

/// <summary>One step on a run timeline: a phase, an LLM call, a tool invocation, an error, ….</summary>
public sealed record TraceStep
{
    /// <summary>Step kind: "phase" | "llm" | "tool" | "error" | free-form.</summary>
    public required string Kind { get; init; }

    public required string Label { get; init; }

    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public double CostUsd { get; init; }
    public long DurationMs { get; init; }
    public string? Detail { get; init; }
}

/// <summary>A whole run's timeline, with token/cost totals computed over the steps.</summary>
public sealed record RunTrace
{
    public required string SessionId { get; init; }
    public required string Mode { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }

    /// <summary>The W3C trace id (32-hex) of the ambient OpenTelemetry activity when the run began, or
    /// null if none. This is the join key between a persisted run trace and the distributed trace in
    /// an OTel backend — given one you can find the other.</summary>
    public string? TraceId { get; init; }

    public IReadOnlyList<TraceStep> Steps { get; init; } = [];

    public long TotalInputTokens => Steps.Sum(s => s.InputTokens);
    public long TotalOutputTokens => Steps.Sum(s => s.OutputTokens);
    public double TotalCostUsd => Steps.Sum(s => s.CostUsd);
}
