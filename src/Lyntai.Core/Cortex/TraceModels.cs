namespace Lyntai.Cortex;

/// <summary>One step on a run timeline: a phase, an LLM call, a tool invocation, an error, ….</summary>
public sealed record TraceStep
{
    /// <summary>Step kind: "phase" | "llm" | "tool" | "error" | free-form.</summary>
    public required string Kind { get; init; }

    public required string Label { get; init; }

    /// <summary>The step's 0-based position on the run timeline. A recorder (<see cref="ITraceRecorder"/>)
    /// stamps this at <see cref="ITraceRecorder.Record"/> time; the store persists it and orders by it, so
    /// the timeline no longer relies on the store's insertion order to be preserved.</summary>
    public long Sequence { get; init; }

    /// <summary>Milliseconds from the run's start (<see cref="RunTrace.StartedAt"/>) to when this step was
    /// recorded — the step's position on the wall-clock timeline. Stamped by the recorder; distinct from
    /// <see cref="DurationMs"/> (how long the step itself took). Zero for a hand-built step left unset.</summary>
    public long OffsetMs { get; init; }

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
