using Lyntai.Memory;

namespace Lyntai.Storage;

/// <summary>
/// The statements the relational <see cref="IMemoryGraphStore"/> backends share — every one that runs
/// unchanged on SQLite and Postgres — so the two cannot drift on them (<c>docs/DECISIONS.md</c> D77). The same
/// shape as <see cref="JobStoreSql"/>. Nothing here touches a connection, so it stays in the dependency-free
/// core; a BYO relational backend can reuse it.
///
/// <para><b>Portable spellings, chosen on purpose.</b> <c>CAST(… AS DOUBLE PRECISION)</c> gives REAL affinity
/// on SQLite (the name contains <c>DOUB</c>) and is a no-op on Postgres; an <c>ON CONFLICT … DO UPDATE SET</c>
/// qualifies each column with its table, which Postgres requires and SQLite accepts; aliases are unquoted,
/// because Dapper matches a column to its property case-insensitively. What each backend still writes for
/// itself is genuine dialect: SQLite's FTS5 seed, the id lists (<c>IN @ids</c> against <c>= ANY(@ids)</c>),
/// the zero-stability guard (<c>MAX</c> against <c>GREATEST</c>), Postgres's <c>::jsonb</c> and
/// <c>COLLATE "C"</c>.</para>
/// </summary>
public static class MemoryGraphSql
{
    /// <summary>The divide-by-zero floor under <c>age / stability</c>, as ONE number rather than one per
    /// dialect. Each backend spells the guard itself (<c>MAX</c> on SQLite, <c>GREATEST</c> on Postgres), but
    /// a floor that differed between them would make the same corpus prune differently on each.
    /// <para>Load-bearing on SQLite specifically: a zero stability divides to NULL there, and a NULL
    /// predicate excludes the row SILENTLY — losing a memory rather than erroring.</para></summary>
    public const string MinimumStability = "0.000001";

    /// <summary>The same floor as a NUMBER, for a store that compares in process — a FLOOR
    /// (<c>Math.Max</c>), as SQL applies it, never a substitute for a value below it.</summary>
    public static readonly double MinimumStabilityValue =
        double.Parse(MinimumStability, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Every node column, aliased to <see cref="MemoryNodeRow"/>, with the age marks subtracted from
    /// where the engine stands (<c>@position</c>, <c>@ordinal</c>, <c>@chars</c>) — what each mark MEANS is
    /// <see cref="MemoryNodeRow"/>'s. Written against the node alias <c>n</c>. Each strength mark takes its
    /// OWN <c>MAX</c>, never "the primitives of whichever edge has the max position".</summary>
    public const string NodeColumns = """
        n.id AS Id, n.engine AS Engine, n.task_key AS TaskKey, n.scope AS Scope,
        n.headline AS Headline, n.content AS Content, n.grade AS Grade,
        n.created_at AS CreatedAt, n.recall_count AS RecallCount,
        CAST(n.stability AS DOUBLE PRECISION) AS Stability, n.metadata AS Metadata, n.signals AS Signals,
        CAST(n.difficulty AS DOUBLE PRECISION) AS Difficulty,
        CAST(@position - n.last_recalled_position AS DOUBLE PRECISION) AS Age,
        CAST(@ordinal - n.encoding_ordinal AS DOUBLE PRECISION) AS OrdinalAge,
        CAST(@chars - n.encoding_chars AS DOUBLE PRECISION) AS VolumeAge,
        n.encoding_at AS EncodingAt,
        n.provenance_retrievability AS ProvenanceRetrievability, n.provenance_salience AS ProvenanceSalience,
        (SELECT COUNT(*) FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS Degree,
        (SELECT CAST(COALESCE(SUM(e.weight), 0) AS DOUBLE PRECISION)
         FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS Strength,
        (SELECT CAST(COALESCE(@position - MAX(e.strengthened_position), 0) AS DOUBLE PRECISION)
         FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS StrengthAge,
        (SELECT CAST(COALESCE(@ordinal - MAX(e.strengthened_ordinal), 0) AS DOUBLE PRECISION)
         FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS StrengthOrdinalAge,
        (SELECT CAST(COALESCE(@chars - MAX(e.strengthened_chars), 0) AS DOUBLE PRECISION)
         FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS StrengthVolumeAge,
        (SELECT MAX(e.strengthened_at)
         FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS StrengthenedAt
        """;

    /// <summary>Where an engine stands on every scale — <see cref="MemoryPositionRow"/>, including why a
    /// missing row reads as zero.</summary>
    public const string Totals = """
        SELECT CAST(position AS DOUBLE PRECISION) AS Position, ordinal AS Ordinal, chars AS Chars,
               encoded_at AS EncodedAt
        FROM lyntai_memory_position WHERE engine = @engine
        """;

    /// <summary>Advance an engine's position and its three primitives together, in one statement, returning
    /// where it now stands — the ordering rule is <see cref="IMemoryGraphStore.UpsertAsync"/>'s.</summary>
    public const string AdvancePosition = """
        INSERT INTO lyntai_memory_position (engine, position, ordinal, chars, encoded_at)
        VALUES (@engine, @advance, 1, @contentLength, @now)
        ON CONFLICT (engine) DO UPDATE SET
            position = lyntai_memory_position.position + @advance,
            ordinal = lyntai_memory_position.ordinal + 1,
            chars = lyntai_memory_position.chars + @contentLength,
            encoded_at = @now
        RETURNING CAST(position AS DOUBLE PRECISION) AS Position, ordinal AS Ordinal, chars AS Chars,
                  encoded_at AS EncodedAt
        """;

    /// <summary>One node by id under its engine — projected, never scored.</summary>
    public const string NodeById =
        $"SELECT {NodeColumns} FROM lyntai_memory_node n WHERE n.id = @id AND n.engine = @engine";

    /// <summary>The query-less seed: exact facts first, then salience, then recency, so the limit cuts the
    /// freshest associative material rather than the quietest exact fact.</summary>
    public const string SeedRecent = $"""
        SELECT {NodeColumns}
        FROM lyntai_memory_node n
        WHERE n.engine = @engine AND n.task_key = @taskKey
          AND (@scope IS NULL OR n.scope = @scope)
        ORDER BY (n.grade = @authoritative) DESC, n.salience DESC, n.last_recalled_position DESC, n.id DESC
        LIMIT @limit
        """;

    /// <summary>The term-wise substring seed, over a clause from
    /// <see cref="SearchTerms.LikeClause(string, IReadOnlyList{string}, string, string, bool)"/>: exact facts
    /// are admitted whatever matched, and <c>Matched</c> says per row whether the query did — the grade-first
    /// order puts an admitted non-match at the HEAD, and only this column keeps it from reporting a match.
    /// Within a grade, the term COUNT leads, so a row brushing one term cannot displace a near-exact hit.</summary>
    /// <param name="kw">The clause over <c>n.content</c> and <c>n.headline</c>.</param>
    public static string SeedMatching(LikeTermClause kw)
    {
        ArgumentNullException.ThrowIfNull(kw);
        return $"""
            SELECT {NodeColumns},
                   {kw.Predicate} AS Matched
            FROM lyntai_memory_node n
            WHERE n.engine = @engine AND n.task_key = @taskKey
              AND (@scope IS NULL OR n.scope = @scope)
              AND (n.grade = @authoritative OR {kw.Predicate})
            ORDER BY (n.grade = @authoritative) DESC, {kw.MatchCount} DESC,
                     n.salience DESC, n.last_recalled_position DESC, n.id DESC
            LIMIT @limit
            """;
    }

    /// <summary>Record reinforcement: stamp each node to where the engine already stands, advancing nothing —
    /// <see cref="IMemoryGraphStore.TouchAsync"/>.</summary>
    public const string Touch = """
        UPDATE lyntai_memory_node
        SET last_recalled_position = @position, stability = @Stability, difficulty = @Difficulty,
            provenance_retrievability = @ProvenanceRetrievability,
            recall_count = recall_count + 1,
            encoding_ordinal = @ordinal, encoding_chars = @chars, encoding_at = @encodedAt
        WHERE id = @Id AND engine = @engine
        """;

    /// <summary>Remove nodes past a retrievability cutoff or older than a real-time bound, with the
    /// <see cref="MemoryGrade.Authoritative"/> carve-out applied as a predicate — an authoritative entry
    /// deletable on one backend and not the other would be a contract difference, not a dialect one. Edges
    /// go with the nodes through <c>ON DELETE CASCADE</c>, which on SQLite only fires because the connection
    /// factory sets <c>foreign_keys=ON</c> per connection.</summary>
    /// <param name="ageOverStability">The backend's own guarded <c>age / stability</c> expression, written
    /// against the node alias <c>n</c> and the <c>@position</c> parameter — the one fragment with no portable
    /// spelling.</param>
    public static string Prune(string ageOverStability) => $"""
        DELETE FROM lyntai_memory_node WHERE id IN (
            SELECT n.id FROM lyntai_memory_node n
            WHERE n.engine = @engine AND n.task_key = @taskKey
              AND (@scope IS NULL OR n.scope = @scope)
              AND n.grade <> @authoritative
              AND ( (@cut IS NOT NULL AND {ageOverStability} > @cut)
                    OR (@createdBefore IS NOT NULL AND n.created_at < @createdBefore) ))
        """;

    /// <summary>Strengthen one directed edge, stamping all four strengthening marks from one totals snapshot
    /// so they never disagree about when it was last strengthened. <b>An endpoint that no longer exists —
    /// deleted while a write-back was in flight — writes nothing</b>, on every backend, rather than a
    /// foreign-key violation part-way through a batch.</summary>
    public const string StrengthenEdge = """
        INSERT INTO lyntai_memory_edge (from_id, to_id, kind, weight, strengthened_position,
            strengthened_ordinal, strengthened_chars, strengthened_at)
        SELECT @from, @to, @kind, @weight, @position, @ordinal, @chars, @at
        WHERE EXISTS (SELECT 1 FROM lyntai_memory_node WHERE id = @from)
          AND EXISTS (SELECT 1 FROM lyntai_memory_node WHERE id = @to)
        ON CONFLICT (from_id, to_id, kind)
            DO UPDATE SET weight = lyntai_memory_edge.weight + @weight,
                          strengthened_position = @position, strengthened_ordinal = @ordinal,
                          strengthened_chars = @chars, strengthened_at = @at
        """;

    /// <summary>Drop a whole task/scope. Edges follow by cascade, as in <see cref="Prune"/>.</summary>
    public const string Forget = """
        DELETE FROM lyntai_memory_node
        WHERE engine = @engine AND task_key = @taskKey AND (@scope IS NULL OR scope = @scope)
        """;

    /// <summary>Append to the review log. Every column is bound by parameter — including <c>verified</c>,
    /// whose TRI-STATE each backend binds its own way (SQLite <c>1</c>/<c>0</c>/NULL, Postgres a native
    /// <c>bool?</c>), which is why <see cref="MemoryReviewRow"/> deliberately does not declare it.</summary>
    public const string InsertReview = """
        INSERT INTO lyntai_memory_review
            (engine, node_id, batch_id, created_at, pre_age, pre_stability, pre_difficulty, pre_strength,
             pre_strength_age, grade, post_stability, post_difficulty, provenance_retrievability,
             verified)
        VALUES (@engine, @NodeId, @batchId, @now, @PreAge, @PreStability, @PreDifficulty, @PreStrength,
                @PreStrengthAge, @ReviewGrade, @PostStability, @PostDifficulty, @ProvenanceRetrievability,
                @Verified)
        """;

    /// <summary>An engine's review log, oldest first — the thirteen shared columns plus <c>verified</c>, which
    /// each backend's own row type reads.</summary>
    public const string Reviews = """
        SELECT id AS Id, engine AS Engine, node_id AS NodeId, batch_id AS BatchId, created_at AS CreatedAt,
               CAST(pre_age AS DOUBLE PRECISION) AS PreAge,
               CAST(pre_stability AS DOUBLE PRECISION) AS PreStability,
               CAST(pre_difficulty AS DOUBLE PRECISION) AS PreDifficulty,
               CAST(pre_strength AS DOUBLE PRECISION) AS PreStrength,
               CAST(pre_strength_age AS DOUBLE PRECISION) AS PreStrengthAge,
               CAST(grade AS DOUBLE PRECISION) AS ReviewGrade,
               CAST(post_stability AS DOUBLE PRECISION) AS PostStability,
               CAST(post_difficulty AS DOUBLE PRECISION) AS PostDifficulty,
               provenance_retrievability AS ProvenanceRetrievability,
               verified AS Verified
        FROM lyntai_memory_review WHERE engine = @engine ORDER BY id
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

    /// <summary>Record one subject, denormalizing the node's task and scope so the lookup needs no join —
    /// and only on a node this engine holds.</summary>
    public const string InsertSubject = """
        INSERT INTO lyntai_memory_subject (engine, node_id, task_key, scope, subject)
        SELECT @engine, n.id, n.task_key, n.scope, @subject
        FROM lyntai_memory_node n WHERE n.id = @nodeId AND n.engine = @engine
        ON CONFLICT (engine, node_id, subject) DO NOTHING
        """;
}
