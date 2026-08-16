using Lyntai.Memory;

namespace Lyntai.Storage;

/// <summary>The materialization row for <c>lyntai_memory_node</c>, projected to <see cref="GraphNode"/> by
/// <see cref="ToNode"/> — shared by the relational backends so the column↔record mapping cannot drift.
///
/// <para><b>Settable properties, never a positional record.</b> Dapper will not bind a SQLite INTEGER to a
/// positional record's constructor parameter, and a property-mapped row sidesteps its exact-type matching
/// entirely. The Postgres store had reached the same shape for its own reasons.</para>
///
/// <para><b>Why this is shared when the QUERIES are not.</b> Every backend aliases each column explicitly,
/// because a name mismatch here is a SILENT null rather than an error — and two independent copies of a
/// 25-property mapping is two places for that silence to appear. The queries genuinely differ by dialect;
/// the property names they alias to do not, and this type is what makes that a fact instead of a
/// coincidence.</para></summary>
public class MemoryNodeRow
{
    public long Id { get; set; }
    public string Engine { get; set; } = "";
    public string TaskKey { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Headline { get; set; } = "";
    public string Content { get; set; } = "";
    public int Grade { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int RecallCount { get; set; }
    public double Stability { get; set; }
    public double Difficulty { get; set; }
    public double Age { get; set; }
    public string? Metadata { get; set; }
    public string? Signals { get; set; }
    public int Degree { get; set; }
    public double Strength { get; set; }
    public double StrengthAge { get; set; }
    public double StrengthOrdinalAge { get; set; }
    public double StrengthVolumeAge { get; set; }
    public DateTimeOffset? StrengthenedAt { get; set; }
    public double OrdinalAge { get; set; }
    public double VolumeAge { get; set; }
    public DateTimeOffset EncodingAt { get; set; }
    public long ProvenanceRetrievability { get; set; }
    public long ProvenanceSalience { get; set; }

    /// <summary>Whether the seeding query actually matched this row — selected ONLY by the query paths that
    /// admit a row on GRADE alone. <b>NULLABLE on purpose</b>: every other query omits the column, leaving
    /// this null, which <see cref="MemoryRelevance.ByRankPosition"/> reads as "not asked". A non-nullable
    /// <c>bool</c> would default to <c>false</c> there and zero every row's relevance.</summary>
    public bool? Matched { get; set; }

    /// <summary>Project to the contract type.
    ///
    /// <para><see cref="GraphNode.ElapsedAge"/> is a .NET-side subtraction, never a SQL one. Both backends
    /// deliberately keep date arithmetic out of their queries — on SQLite because <c>julianday</c> returns
    /// NULL for an unparseable timestamp and a NULL predicate then excludes the row silently — and
    /// <see cref="EncodingAt"/> and <paramref name="encodedAt"/> are both plain
    /// <see cref="DateTimeOffset"/> values the micro-ORM has already parsed, so the subtraction is exact and
    /// needs no date-string handling at all.</para>
    ///
    /// <para><see cref="StrengthenedAt"/> is the one nullable mark: <c>MAX</c> over no rows is NULL, so a
    /// node with no edges has no strengthening to date and reports <c>0</c> — matching the <c>COALESCE</c>
    /// the ordinal and volume marks apply in SQL.</para>
    ///
    /// <para><see cref="GraphNode.Relevance"/> is deliberately NOT set here. It is the caller's own rank
    /// position, and it goes through <see cref="MemoryRelevance.ByRankPosition"/> at the query site where
    /// that position is known.</para></summary>
    /// <param name="encodedAt">Where the engine's own elapsed-time primitive currently stands.</param>
    public GraphNode ToNode(DateTimeOffset encodedAt) => new(
        Id, Engine, TaskKey, Scope, Headline, Content, (MemoryGrade)Grade,
        CreatedAt, RecallCount, Stability, Age, 1, Degree,
        CuratedMetadataJson.Deserialize(Metadata), Strength, StrengthAge,
        MemorySignalsJson.Deserialize(Signals),
        OrdinalAge: OrdinalAge, VolumeAge: VolumeAge,
        ElapsedAge: (encodedAt - EncodingAt).TotalDays,
        ProvenanceRetrievability: ProvenanceRetrievability, ProvenanceSalience: ProvenanceSalience,
        Difficulty: Difficulty,
        StrengthOrdinalAge: StrengthOrdinalAge, StrengthVolumeAge: StrengthVolumeAge,
        StrengthElapsedAge: StrengthenedAt is { } at ? (encodedAt - at).TotalDays : 0);
}

/// <summary>The connecting edge's half of a neighbour row — settable properties, matching
/// <see cref="MemoryNodeRow"/>'s own reasoning. <see cref="EdgeStrengthenedAt"/> is nullable because
/// <c>MAX</c> over no rows is NULL, exactly as the node's own <see cref="MemoryNodeRow.StrengthenedAt"/>
/// is.</summary>
public class MemoryEdgeRow
{
    public double EdgeWeight { get; set; }
    public double EdgeAge { get; set; }
    public double EdgeOrdinalAge { get; set; }
    public double EdgeVolumeAge { get; set; }
    public DateTimeOffset? EdgeStrengthenedAt { get; set; }
}

/// <summary>Where an engine currently stands, on every scale a store tracks: the <c>Advance</c>-driven
/// <see cref="Position"/>, and the three primitives that advance unconditionally on every write regardless
/// of which age policy is installed.
///
/// <para>Every member defaults to its zero value for an engine nothing has been written to, which is
/// correct rather than merely convenient: nothing has happened in it, so nothing has aged. Both backends
/// substitute <see cref="DateTimeOffset.UnixEpoch"/> for the missing row.</para></summary>
public class MemoryPositionRow
{
    public double Position { get; set; }
    public long Ordinal { get; set; }
    public long Chars { get; set; }
    public DateTimeOffset EncodedAt { get; set; }
}

/// <summary>The materialization row for <c>lyntai_memory_review</c> — settable properties, matching
/// <see cref="MemoryNodeRow"/>'s own reasoning.
///
/// <para><see cref="BatchId"/> stays a <see cref="string"/> and is parsed to <see cref="Guid"/> only in
/// <see cref="ToReview"/>, the same pattern <see cref="JobRow"/> uses for its own TEXT-stored id: Dapper
/// does not bind a SQLite TEXT column to a <see cref="Guid"/> property, and the column is plain <c>TEXT</c>
/// on BOTH backends, so both read it the same way even though Npgsql could bind a native <c>uuid</c>.</para>
///
/// <para><b><c>verified</c> is deliberately NOT declared here, and this type is not sealed for that
/// reason.</b> SQLite has no boolean, so the tri-state arrives as <c>1</c>/<c>0</c>/NULL and must be typed
/// <c>long?</c> there; Postgres binds a native <c>bool?</c>. That difference is a real dialect fact rather
/// than an accident, so each backend derives from this type, adds its own column, and hands the resolved
/// value to <see cref="ToReview"/>. Sharing the other thirteen columns is what this is for; forcing the
/// fourteenth into one shape would be inventing a portability that the storage engines do not have.</para>
///
/// <para>The distinction the tri-state carries is load-bearing and is pinned by the store contract:
/// <c>false</c> is an observed failure, <c>null</c> is no judgement, and they are not interchangeable (see
/// <see cref="MemoryReviewWrite.Verified"/>).</para></summary>
public class MemoryReviewRow
{
    public long Id { get; set; }
    public string Engine { get; set; } = "";
    public long NodeId { get; set; }
    public string BatchId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public double PreAge { get; set; }
    public double PreStability { get; set; }
    public double PreDifficulty { get; set; }
    public double PreStrength { get; set; }
    public double PreStrengthAge { get; set; }
    public double? Grade { get; set; }
    public double PostStability { get; set; }
    public double PostDifficulty { get; set; }
    public long ProvenanceRetrievability { get; set; }

    /// <summary>Project to the contract type, with the backend's own resolved tri-state.</summary>
    /// <param name="verified">Whether a judge found this entry actually answered the query — <c>false</c>
    /// is an observed failure and <c>null</c> is no judgement, never the same thing.</param>
    public MemoryReview ToReview(bool? verified) => new(
        Id, Engine, NodeId, Guid.Parse(BatchId), CreatedAt, PreAge, PreStability, PreDifficulty,
        PreStrength, PreStrengthAge, Grade, PostStability, PostDifficulty, ProvenanceRetrievability,
        verified);
}
