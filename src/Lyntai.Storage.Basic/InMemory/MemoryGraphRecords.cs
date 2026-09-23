using Lyntai.Memory;

namespace Lyntai.Storage.InMemory;

/// <summary>A memory's identity and text — what a person reads, and all a file store writes to the memory's
/// own file. Nothing here changes on a recall.</summary>
internal sealed record GraphNodeRecord(
    long Id, string Engine, string TaskKey, string Scope, string Headline, string Content,
    MemoryGrade Grade, DateTimeOffset CreatedAt, IReadOnlyDictionary<string, string>? Metadata);

/// <summary>A memory's decay bookkeeping — what the engine's arithmetic reads and writes, and what a file store
/// journals. <c>LastRecalledPosition</c> is the <c>Advance</c>-driven mark; the three <c>Encoding*</c> stamps are
/// the policy-independent primitives beside it (design §5.7).</summary>
internal sealed record GraphNodeState(
    int RecallCount, double Stability, double LastRecalledPosition, MemorySignals Signals,
    long EncodingOrdinal, long EncodingChars, DateTimeOffset EncodingAt,
    long ProvenanceRetrievability, long ProvenanceSalience, double Difficulty);

/// <summary>One stored memory: its record and its state.</summary>
internal sealed record GraphRow(GraphNodeRecord Record, GraphNodeState State)
{
    public long Id => Record.Id;
}

/// <summary>A directed edge's identity; an untyped association has the empty <see cref="Kind"/>.</summary>
internal readonly record struct GraphEdgeKey(long From, long To, string Kind);

/// <summary>An edge's RAW weight, which only grows, and the four marks of when it was last strengthened.</summary>
internal readonly record struct GraphEdgeState(double Weight, double Position, long Ordinal, long Chars, DateTimeOffset At);

/// <summary>One engine's position on every scale: the <c>Advance</c>-driven one and the three primitives that
/// advance unconditionally. Frozen between writes — a recall moves none of them.</summary>
internal readonly record struct GraphTotals(double Position, long Ordinal, long Chars, DateTimeOffset EncodedAt);

/// <summary>What one mutation WILL do, computed without doing it — so a durable store can write it first.
/// Every entry is a full resulting state, never a delta.</summary>
internal sealed class GraphChange
{
    public List<(string Engine, GraphTotals Totals)> Totals { get; } = [];

    /// <summary>Each memory written: its prior row (null when new) and its row after.</summary>
    public List<(GraphRow? Before, GraphRow After)> Nodes { get; } = [];

    public List<(GraphEdgeKey Key, GraphEdgeState Edge)> Edges { get; } = [];

    /// <summary>A memory's subjects as one engine recorded them; an empty list clears them.</summary>
    public List<(string Engine, long NodeId, IReadOnlyList<string> Subjects)> Subjects { get; } = [];

    public List<long> Removed { get; } = [];

    public List<MemoryReview> Reviews { get; } = [];

    public List<long> TrimmedReviews { get; } = [];

    public List<(string Engine, long Count)> ReviewCounters { get; } = [];
}
