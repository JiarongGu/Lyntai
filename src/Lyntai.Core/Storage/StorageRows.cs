using Lyntai.Cortex;

namespace Lyntai.Storage;

// The materialization rows the relational backends share: a column↔property mismatch is a SILENT null, so
// one copy per row (docs/DECISIONS.md D80). SETTABLE PROPERTIES, never positional records — Dapper will not
// bind a SQLite INTEGER to a positional record's constructor parameter.

/// <summary>A <c>lyntai_curated_memory</c> row.</summary>
public sealed class CuratedMemoryRow
{
    public long Id { get; set; }
    public string Kind { get; set; } = "";
    public string Content { get; set; } = "";
    public bool Enabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? TaskKey { get; set; }
    public string? Scope { get; set; }
    public string? Metadata { get; set; }

    /// <summary>Project to the contract type.</summary>
    public CuratedMemory ToRecord() => new(Id, Kind, Content, Enabled, CreatedAt, UpdatedAt,
        TaskKey, Scope, CuratedMetadataJson.Deserialize(Metadata));
}

/// <summary>A <c>lyntai_prompt_version</c> row.</summary>
public sealed class PromptVersionRow
{
    public string Name { get; set; } = "";
    public int Version { get; set; }
    public string Template { get; set; } = "";
    public string? Author { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Project to the contract type.</summary>
    public PromptVersion ToRecord() => new(Name, Version, Template, Author, CreatedAt, IsActive);
}

/// <summary>One scorer's result within a session.</summary>
public sealed class ScoreResultRow
{
    public string ScorerId { get; set; } = "";
    public string ScorerName { get; set; } = "";
    public string ScoreGroup { get; set; } = "";
    public bool IsLlm { get; set; }
    public double Score { get; set; }
    public string? Reason { get; set; }

    /// <summary>Project to the contract type.</summary>
    public ScoredResult ToRecord() => new(ScorerId, ScorerName, ScoreGroup, IsLlm, Score, Reason);
}

/// <summary>A scorer's average and sample count over a window.</summary>
public sealed class ScoreAggregateRow
{
    public string ScorerId { get; set; } = "";
    public string ScorerName { get; set; } = "";
    public double AverageScore { get; set; }
    public long Count { get; set; }

    /// <summary>Project to the contract type.</summary>
    public ScorerAggregate ToRecord() => new(ScorerId, ScorerName, AverageScore, (int)Count);
}

/// <summary>One (session, scorer, score) triple, for a flat export — the materialization of
/// <see cref="ScoreExportEntry"/>, which is a positional record Dapper will not bind
/// into.</summary>
public sealed class ScoreExportRow
{
    public string SessionId { get; set; } = "";
    public string ScorerId { get; set; } = "";
    public double Score { get; set; }

    /// <summary>Project to the contract type.</summary>
    public ScoreExportEntry ToRecord() => new(SessionId, ScorerId, Score);
}

/// <summary>The session half of a trace read.</summary>
public sealed class TraceSessionRow
{
    public string SessionId { get; set; } = "";
    public string Mode { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string? TraceId { get; set; }

    /// <summary>Project to the contract type, with the session's steps in the order they were read.</summary>
    /// <param name="steps">The session's step rows.</param>
    public RunTrace ToRecord(IEnumerable<TraceStepRow> steps) => new()
    {
        SessionId = SessionId,
        Mode = Mode,
        StartedAt = StartedAt,
        EndedAt = EndedAt,
        TraceId = TraceId,
        Steps = [.. steps.Select(s => s.ToRecord())],
    };
}

/// <summary>One step within a traced session.</summary>
public sealed class TraceStepRow
{
    public string Kind { get; set; } = "";
    public string Label { get; set; } = "";
    public long Sequence { get; set; }
    public long OffsetMs { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public double CostUsd { get; set; }
    public long DurationMs { get; set; }
    public string? Detail { get; set; }

    /// <summary>Project to the contract type.</summary>
    public TraceStep ToRecord() => new()
    {
        Kind = Kind,
        Label = Label,
        Sequence = Sequence,
        OffsetMs = OffsetMs,
        InputTokens = InputTokens,
        OutputTokens = OutputTokens,
        CostUsd = CostUsd,
        DurationMs = DurationMs,
        Detail = Detail,
    };
}

/// <summary>Totals for one consumer's usage window.</summary>
public sealed class UsageTotalsRow
{
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public double CostUsd { get; set; }
    public long Calls { get; set; }
}

/// <summary>The eviction metadata a cap/TTL sweep reads, projected to <see cref="MemoryEviction.Row"/>.
/// <para>Materialized into settable properties rather than straight into that record struct, because a
/// positional record struct maps less predictably through the <see cref="DateTimeOffset"/> type
/// handler.</para></summary>
public sealed class MemoryEvictionCandidateRow
{
    public long Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastAccessedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public int Length { get; set; }

    /// <summary>Project to the shape <see cref="MemoryEviction.Survivors"/> reads.</summary>
    public MemoryEviction.Row ToRow() => new(Id, CreatedAt, LastAccessedAt, ExpiresAt, Length);
}
