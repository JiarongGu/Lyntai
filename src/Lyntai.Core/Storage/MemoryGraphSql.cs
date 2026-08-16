using Lyntai.Memory;

namespace Lyntai.Storage;

/// <summary>
/// The dialect-NEUTRAL half of the relational <see cref="IMemoryGraphStore"/> backends — the statements that
/// carry no dialect at all, plus the row↔record mapping, so the SQLite and Postgres stores cannot drift on
/// them. The same shape, and the same reasoning, as <see cref="JobStoreSql"/>.
///
/// <para><b>Why this exists, given that the Postgres store's own doc once said the parallel was "NOT
/// duplication waiting to be extracted".</b> That claim was true of the SQL and was never about the rest.
/// The two backends genuinely differ by dialect necessity — <c>GREATEST</c> versus <c>MAX</c>, an
/// <c>ILIKE</c> over a GIN index versus an FTS5 virtual table and its triggers, <c>= ANY(@ids)</c> versus
/// <c>IN @ids</c>, <c>ON CONFLICT … DO NOTHING</c> versus <c>INSERT OR IGNORE</c>, and the table reference
/// Postgres requires in <c>DO UPDATE SET</c>. None of that reaches the MATERIALIZATION, which is pure CLR
/// and was byte-identical in both files. A premise that holds for one half of a file reads as a claim about
/// the whole of it — the shape <c>pitfalls.md</c> §"Copying a rule copies its assumptions" records.</para>
///
/// <para><b>What stays per-backend, said here rather than left to be discovered</b> (the rule
/// <c>check-links</c>' own header states: when something's scope is narrower than the defect, name the part
/// it does not cover). The seed queries, the neighbour queries, <c>UpsertAsync</c>'s two statements, the
/// by-id delete, the subject INSERT, and the two subject READS all stay in their own backend. The subject
/// reads are the only ones that could ALMOST be shared: they differ by a single <c>::text</c> cast Npgsql
/// needs on the scope parameter, and threading a cast token through a shared string is more obscure than
/// the two lines it would save.</para>
///
/// <para>Nothing here touches a connection, so this stays in the dependency-free core exactly as
/// <see cref="JobStoreSql"/> does — a micro-ORM call is the backend's own business.</para>
/// </summary>
public static class MemoryGraphSql
{
    /// <summary>The divide-by-zero floor under <c>age / stability</c>, as ONE number rather than one per
    /// dialect. Both backends still spell the guard themselves (<c>MAX</c> on SQLite, <c>GREATEST</c> on
    /// Postgres) because that spelling IS the dialect difference — but a floor that differed between them
    /// would make the same corpus prune differently on each, and two literals is how that happens.
    /// <para>Load-bearing on SQLite specifically: a zero stability divides to NULL there, and a NULL
    /// predicate excludes the row SILENTLY — losing a memory rather than erroring. Postgres raises instead,
    /// so the guard is what keeps the two agreeing rather than one of them failing.</para></summary>
    public const string MinimumStability = "0.000001";

    /// <summary>The same floor as a NUMBER, for a store that compares in process instead of in SQL.
    /// <para>Derived from the literal above rather than written a second time. The in-process graph store
    /// spelled its own <c>0.000001</c> and, worse, applied it as a SUBSTITUTION (<c>stability > 0 ? stability
    /// : 1e-6</c>) where SQL FLOORS (<c>MAX</c>/<c>GREATEST</c>) — the same for a zero stability and
    /// different for every value between zero and the floor, on the DELETE path. A stability of <c>1e-7</c>
    /// pruned in process and survived on both relational backends from the same call. That is precisely the
    /// drift the paragraph above says two literals cause, arriving through the third backend nobody counted
    /// as one.</para></summary>
    public static readonly double MinimumStabilityValue =
        double.Parse(MinimumStability, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Remove nodes past a retrievability cutoff or older than a real-time bound, with the
    /// <see cref="MemoryGrade.Authoritative"/> carve-out applied as a predicate.
    ///
    /// <para>The ONE fragment a backend supplies is <paramref name="ageOverStability"/>, because the
    /// zero-stability guard has no portable spelling. Everything else — and in particular the grade
    /// carve-out and the two independent cutoffs — is shared, which is the point: an authoritative entry
    /// being deletable on one backend and not the other is a contract difference, not a dialect one.</para>
    ///
    /// <para>Edges go with the nodes through <c>ON DELETE CASCADE</c>. On SQLite that only fires because the
    /// connection factory sets <c>foreign_keys=ON</c> per connection.</para></summary>
    /// <param name="ageOverStability">The backend's own guarded <c>age / stability</c> expression, written
    /// against the node alias <c>n</c> and the <c>@position</c> parameter.</param>
    public static string Prune(string ageOverStability) => $"""
        DELETE FROM lyntai_memory_node WHERE id IN (
            SELECT n.id FROM lyntai_memory_node n
            WHERE n.engine = @engine AND n.task_key = @taskKey
              AND (@scope IS NULL OR n.scope = @scope)
              AND n.grade <> @authoritative
              AND ( (@cut IS NOT NULL AND {ageOverStability} > @cut)
                    OR (@createdBefore IS NOT NULL AND n.created_at < @createdBefore) ))
        """;

    /// <summary>Drop a whole task/scope. Edges follow by cascade, as in <see cref="Prune"/>.</summary>
    public const string Forget = """
        DELETE FROM lyntai_memory_node
        WHERE engine = @engine AND task_key = @taskKey AND (@scope IS NULL OR scope = @scope)
        """;

    /// <summary>Append to the review log. Every column is bound by parameter, so the statement carries no
    /// dialect — including <c>verified</c>, whose TRI-STATE the two backends bind differently (SQLite has no
    /// boolean and takes <c>1</c>/<c>0</c>/NULL; Postgres binds a native <c>bool?</c>). The distinction
    /// between "judged not relevant" and "never judged" is load-bearing, so each backend converts at its own
    /// call site and <see cref="MemoryReviewRow"/> deliberately does not declare the column.</summary>
    public const string InsertReview = """
        INSERT INTO lyntai_memory_review
            (engine, node_id, batch_id, created_at, pre_age, pre_stability, pre_difficulty, pre_strength,
             pre_strength_age, grade, post_stability, post_difficulty, provenance_retrievability,
             verified)
        VALUES (@engine, @NodeId, @batchId, @now, @PreAge, @PreStability, @PreDifficulty, @PreStrength,
                @PreStrengthAge, @Grade, @PostStability, @PostDifficulty, @ProvenanceRetrievability,
                @Verified)
        """;

    /// <summary>Trim the review log to its soft cap, paced by <see cref="MemoryReviewLogPacing"/> so this —
    /// the one operation with a real cost — runs roughly once every interval rather than on every write.</summary>
    public const string TrimReviews = """
        DELETE FROM lyntai_memory_review
        WHERE engine = @engine AND id <= (
            SELECT id FROM lyntai_memory_review WHERE engine = @engine
            ORDER BY id DESC LIMIT 1 OFFSET @cap)
        """;

    /// <summary>Clear a node's subjects. REPLACE, never accumulate: a stale subject from an earlier
    /// annotation keeps linking future facts into the wrong cluster and nothing would ever surface it. This
    /// runs even when the incoming set is EMPTY, which is how an annotator that changes its mind to "no
    /// opinion" actually clears them.</summary>
    public const string DeleteSubjects =
        "DELETE FROM lyntai_memory_subject WHERE engine = @engine AND node_id = @nodeId";
}
