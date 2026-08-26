using System.Collections.Concurrent;
using System.Data;
using Dapper;
using Lyntai.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Storage.Postgres;

/// <summary>PostgreSQL <see cref="IMemoryGraphStore"/> over <c>lyntai_memory_node</c> +
/// <c>lyntai_memory_edge</c>, with substring recall through the <c>pg_trgm</c> GIN index.
/// <para><b>The QUERIES here are this backend's own; the MATERIALIZATION is shared</b> — the row types, the
/// projection to <see cref="GraphNode"/> and the dialect-free statements live once, in
/// <see cref="MemoryNodeRow"/> and <see cref="MemoryGraphSql"/>, stated in full on the
/// <c>SqliteMemoryGraphStore</c> twin (<c>docs/DECISIONS.md</c> D77). What stays here is what the dialect
/// forces: <c>GREATEST</c> versus <c>MAX</c>, <c>ILIKE</c> over a GIN index versus an FTS5 virtual table and
/// its triggers, <c>= ANY(@ids)</c> versus <c>IN @ids</c>, the table reference in <c>DO UPDATE SET</c>.</para>
/// <para><b>Every connection is opened ASYNCHRONOUSLY.</b> <see cref="SeedAsync"/> runs on every recall, so a
/// synchronous open blocks a thread-pool thread for a whole TCP connect plus authentication — thread-pool
/// starvation on a cold pool rather than a slow query, and the cancellation token cannot reach the connect
/// either. The SQLite twin may open synchronously because its "connect" is a file handle.</para>
/// <para><b>Age is a subtraction, not a duration</b> — <c>lyntai_memory_position</c> holds a monotone position
/// per engine — and <b>the decay curve is never evaluated here</b>. <see cref="GraphNode.OrdinalAge"/> and
/// <see cref="GraphNode.VolumeAge"/> are the SAME shape of subtraction against two ADDITIONAL primitives the
/// table also tracks unconditionally (design doc §5.7); <see cref="GraphNode.ElapsedAge"/> is computed in
/// .NET, not SQL.</para>
/// <para>Recall matches the query term-wise through <see cref="SearchTerms"/> and orders by GRADE, then by
/// how many terms matched, then by recency; the RANKING within that is backend-specific (a term count here),
/// WHICH entries are found is not. <b>Grade leads</b> because authoritative material is admitted
/// unconditionally: a recency-led ordering would let the candidate <c>LIMIT</c> cut the quietest exact fact
/// before the engine ranked anything, so an admitted non-match lands near the HEAD of this store's row ORDER
/// — backend-specific by <see cref="IMemoryGraphStore.SeedAsync"/>, and a different thing from the
/// <see cref="GraphNode.Relevance"/> reported for it (<see cref="MemoryRelevance.ByRankPosition"/>).</para></summary>
/// <param name="factory">Connection factory.</param>
/// <param name="logger">Optional.</param>
/// <param name="clock">Time source for the audit timestamps; null takes the system clock.</param>
public sealed class PostgresMemoryGraphStore(
    IDbConnectionFactory factory,
    ILogger<PostgresMemoryGraphStore>? logger = null,
    Func<DateTimeOffset>? clock = null) : IMemoryGraphStore
{
    private readonly ILogger _logger = logger ?? NullLogger<PostgresMemoryGraphStore>.Instance;
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    // Per-engine review-write counters (Task 3, MemoryReviewLogPacing) — in-process only, never persisted;
    // see that type's own remarks for why a process restart resetting this is an acceptable trade-off for a
    // SOFT cap. Concurrent for the same reason as the SQLite twin: this store makes no single-writer
    // guarantee across calls.
    private readonly ConcurrentDictionary<string, long> _reviewCounters = new(StringComparer.Ordinal);

    // Degree/Strength are plain aggregates and Age/StrengthAge are plain subtractions against @position — the
    // store applies no decay; the policy does. No CAST: DOUBLE PRECISION binds to double directly.
    //
    // OrdinalAge/VolumeAge are the SAME shape of subtraction, against the two new policy-independent
    // primitives (design doc §5.7) — @ordinal and @chars advance unconditionally on every write, regardless of
    // which IMemoryAgePolicy the engine has installed. EncodingAt is read RAW; ElapsedAge is a .NET-side
    // subtraction in MemoryNodeRow.ToNode, matching the SQLite twin's own reasoning (avoiding a date-parsing round trip in
    // the database).
    // ProvenanceRetrievability/ProvenanceSalience are plain BIGINTs (design doc §5.7, Task 4), read exactly
    // like RecallCount/Degree — no computation, so nothing to cast.
    // Difficulty is DOUBLE PRECISION, same as Stability — no CAST
    // needed on this backend either (Npgsql binds DOUBLE PRECISION straight to double).
    // StrengthOrdinalAge/StrengthVolumeAge are the strength-side counterparts of OrdinalAge/VolumeAge, and
    // StrengthenedAt is read RAW for the reason EncodingAt is. Each takes its own MAX: all
    // four strengthening marks advance monotonically together from one write's totals, so the freshest edge
    // holds the maximum of every one. MAX over no rows is NULL, so StrengthenedAt alone is nullable — a node
    // with no edges has no strengthening to date and MemoryNodeRow.ToNode reports 0, matching the other two's COALESCE.
    private const string NodeColumns = """
        n.id AS "Id", n.engine AS "Engine", n.task_key AS "TaskKey", n.scope AS "Scope",
        n.headline AS "Headline", n.content AS "Content", n.grade AS "Grade",
        n.created_at AS "CreatedAt", n.recall_count AS "RecallCount",
        n.stability AS "Stability", n.metadata AS "Metadata", n.signals AS "Signals",
        n.difficulty AS "Difficulty",
        (@position - n.last_recalled_position) AS "Age",
        (@ordinal - n.encoding_ordinal) AS "OrdinalAge",
        (@chars - n.encoding_chars) AS "VolumeAge",
        n.encoding_at AS "EncodingAt",
        n.provenance_retrievability AS "ProvenanceRetrievability", n.provenance_salience AS "ProvenanceSalience",
        (SELECT COUNT(*) FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS "Degree",
        (SELECT COALESCE(SUM(e.weight), 0) FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS "Strength",
        (SELECT COALESCE(@position - MAX(e.strengthened_position), 0)
         FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS "StrengthAge",
        (SELECT COALESCE(@ordinal - MAX(e.strengthened_ordinal), 0)
         FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS "StrengthOrdinalAge",
        (SELECT COALESCE(@chars - MAX(e.strengthened_chars), 0)
         FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS "StrengthVolumeAge",
        (SELECT MAX(e.strengthened_at)
         FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS "StrengthenedAt"
        """;

    // GREATEST guards a zero stability: dividing by zero raises here rather than yielding NULL as it does
    // on SQLite, but the guard is what keeps both backends agreeing instead of one erroring.
    private const string AgeOverStability =
        "(@position - n.last_recalled_position) / GREATEST(n.stability, "
        + MemoryGraphSql.MinimumStability + ")";

    // Seeding applies NO faintness bound — decay buries by rank in the engine, never by excluding a row
    // here. AgeOverStability is PruneAsync's alone, where removing a memory is the explicit intent.

    // Bound as a parameter, never a literal: MemoryGrade is Inherit=0, Associative=1, Authoritative=2, so
    // a hand-written "grade = 1" silently means the OPPOSITE of what it reads like.
    private static readonly int Authoritative = (int)MemoryGrade.Authoritative;

    /// <inheritdoc />
    public async Task<long> UpsertAsync(GraphNodeWrite write, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        // MemoryContentKey.Of is the ONE dedup key — a backend hashing content its own way answers "is this
        // the same memory?" differently, which is a contract difference, not a storage detail.
        var hash = MemoryContentKey.Of(write.Content);
        var metadata = CuratedMetadataJson.Serialize(write.Metadata);
        // NULL, not "{}", for an empty bag — the NULL is what the DO UPDATE SET below reads as "no
        // opinion", keeping the stored signals. See GraphNodeWrite.Signals for why blanking would be
        // wrong.
        var signals = MemorySignalsJson.Serialize(write.Signals);
        // The column is the COERCED materialization of the bag's salience for the database to sort on; the
        // bag stays the source of truth and is stored verbatim, so the two are not byte-for-byte equal — a
        // bag holding 0.5 reads back as 0.5 while the column holds 1. What they cannot do is drift: both come
        // from the SAME write.Signals in this ONE statement, through MemorySignals.Salience, which is the ONE
        // definition of the coercion (below-1 → 1, non-finite → 1) that the InMemory store's ordering and the
        // engine's rank boost also read the value through. This second read bypasses
        // MemorySignalsJson.Serialize's own non-finite filter entirely, which is why it needs that guard at
        // all: Npgsql binds a NaN double without complaint, straight into a NOT NULL column every seed query
        // orders on — and Postgres sorts NaN ABOVE every real number, so the corruption is silent.
        var salience = MemorySignals.Salience(write.Signals);
        // The LIVE difficulty column — promoted from the bag, but
        // NOT on salience's own "bag is non-empty" trigger (see the SQLite twin's own
        // comment for why difficulty needs its own key — a second writer, the retrievability policy, means
        // a write that judged something else entirely must never reset what THAT policy has tracked).
        var hasDifficultySignal = write.Signals.Values.ContainsKey(MemorySignals.WellKnown.Difficulty);
        var difficulty = MemorySignals.Difficulty(write.Signals);
        var now = _clock();
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        // advance the engine's position AND the three policy-independent primitives FIRST and atomically, so
        // the new entry's own age is zero relative to everything it is stamped with. The primitives advance
        // UNCONDITIONALLY — one write, this write's own content length, this write's own timestamp — never
        // from write.Advance, which is this write's chosen IMemoryAgePolicy's business, not the store's.
        var totals = await conn.QuerySingleAsync<MemoryPositionRow>(new CommandDefinition("""
            INSERT INTO lyntai_memory_position (engine, position, ordinal, chars, encoded_at)
            VALUES (@engine, @advance, 1, @contentLength, @now)
            ON CONFLICT (engine) DO UPDATE SET
                position = lyntai_memory_position.position + @advance,
                ordinal = lyntai_memory_position.ordinal + 1,
                chars = lyntai_memory_position.chars + @contentLength,
                encoded_at = @now
            RETURNING position AS "Position", ordinal AS "Ordinal", chars AS "Chars", encoded_at AS "EncodedAt"
            """, new
            {
                engine = write.Engine, advance = Math.Max(0, write.Advance),
                contentLength = write.Content.Length, now,
            }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO lyntai_memory_node
                (engine, task_key, scope, headline, content, content_hash, grade, metadata, signals, salience,
                 created_at, last_recalled_position, recall_count, stability, difficulty,
                 encoding_ordinal, encoding_chars, encoding_at,
                 provenance_retrievability, provenance_salience)
            VALUES (@engine, @taskKey, @scope, @headline, @content, @hash, @grade, @metadata, @signals::jsonb, @salience,
                    @now, @position, 0, @stability, @difficulty, @ordinal, @chars, @now,
                    @provenanceRetrievability, @provenanceSalience)
            ON CONFLICT (engine, task_key, scope, content_hash)
                DO UPDATE SET last_recalled_position = @position,
                              -- an AUTHORED headline survives a refresh that does not restate it:
                              -- the engine derives one from the content when the caller supplies
                              -- none, and overwriting with that discards the caller's own text
                              headline = CASE WHEN @headlineStated THEN @headline ELSE lyntai_memory_node.headline END,
                              -- only a CALLER-NAMED grade overwrites; see the SQLite twin and
                              -- GraphNodeWrite.GradeStated for why "not stated" had to stop meaning
                              -- "stated as ordinary"
                              grade = CASE WHEN @gradeStated THEN @grade ELSE lyntai_memory_node.grade END,
                              signals = COALESCE(@signals::jsonb, lyntai_memory_node.signals),
                              -- metadata answers the same question as signals and now the same
                              -- way: an ABSENT bag is "no opinion" and keeps what is stored, a
                              -- supplied one REPLACES it. Absent from this SET until D91, which
                              -- silently ignored every correction a caller made.
                              metadata = COALESCE(@metadata, lyntai_memory_node.metadata),
                              salience = CASE WHEN @signals::jsonb IS NULL THEN lyntai_memory_node.salience ELSE @salience END,
                              -- difficulty overwrites ONLY when THIS write's bag names a difficulty signal
                              -- see the SQLite twin's own comment for why
                              difficulty = CASE WHEN @hasDifficultySignal THEN @difficulty ELSE lyntai_memory_node.difficulty END,
                              -- provenance_salience follows signals' own "empty incoming keeps what's
                              -- stored" rule exactly; provenance_retrievability is deliberately ABSENT from
                              -- this SET, same as stability itself: a plain re-remember never revisits either
                              provenance_salience = CASE WHEN @signals::jsonb IS NULL THEN lyntai_memory_node.provenance_salience
                                                          ELSE @provenanceSalience END,
                              encoding_ordinal = @ordinal, encoding_chars = @chars, encoding_at = @now
            RETURNING id
            """, new
        {
            engine = write.Engine, taskKey = write.TaskKey, scope = write.Scope,
            headline = write.Headline, content = write.Content, hash, grade = (int)write.Grade,
            metadata, signals, salience, difficulty, hasDifficultySignal, now, position = totals.Position,
            gradeStated = write.GradeStated, headlineStated = write.HeadlineStated,
            stability = write.InitialStability, ordinal = totals.Ordinal, chars = totals.Chars,
            provenanceRetrievability = write.ProvenanceRetrievability, provenanceSalience = write.ProvenanceSalience,
        }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        await tx.CommitAsync(ct).ConfigureAwait(false);
        return id;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphNode>> SeedAsync(string engine, string taskKey, string? scope,
        string? query, int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var totals = await TotalsAsync(conn, engine, ct).ConfigureAwait(false);
        var position = totals.Position;
        var ordinal = totals.Ordinal;
        var chars = totals.Chars;

        // Grade leads both orderings below, ahead of recency: the WHERE carve-out only keeps an exact fact
        // out of the filter, it says nothing about the LIMIT after it. A long-quiet exact fact has the
        // LOWEST last_recalled_position in its scope, so a recency-only order sorts it last and the LIMIT
        // cuts it before this method ever returns it.
        if (!string.IsNullOrWhiteSpace(query))
        {
            // Term-wise, via the shared split: a multi-word cue matches an entry carrying ANY of its terms,
            // exactly as SQLite's FTS path does. TWO PASSES, and the second is where the whole cost of CJK
            // correctness is contained.
            //
            // Pass 1 is INDEX-FRIENDLY: trigrams and whole words only, so every ILIKE pattern is at least
            // three characters and pg_trgm's GIN index can serve it. Pass 2 adds the two-character terms of a
            // spaceless script, which the index structurally CANNOT serve — such a pattern degrades to a
            // sequential scan. Running the wide clause unconditionally would make every Chinese recall pay
            // that; dropping the short terms instead would reintroduce the cross-backend divergence D55
            // exists to remove. So the scan is paid only when the fast pass found NOTHING — the same
            // zero-rows-then-widen shape SQLite already uses between FTS and LIKE.
            //
            // Most Chinese content words are exactly two characters (配偶, 客户), so pass 2 is not an edge case;
            // it is the reason a CJK consumer gets an answer at all.
            var narrow = await SeedByTermsAsync(conn, engine, taskKey, scope, query, limit, totals,
                includeShortTerms: false, ct).ConfigureAwait(false);

            // A row can be present purely by the grade carve-out, so "found something" must mean "matched
            // something": a seed carrying only admitted non-matches is exactly the miss pass 2 is for.
            if (narrow.Any(n => n.Relevance > 0)) return narrow;

            if (!SearchTerms.HasShortSpacelessTerms(query))
                return narrow;   // nothing to widen with (every ASCII query) — no second round trip

            return await SeedByTermsAsync(conn, engine, taskKey, scope, query, limit, totals,
                includeShortTerms: true, ct).ConfigureAwait(false);
        }

        return await QueryAsync(conn, $"""
            SELECT {NodeColumns}
            FROM lyntai_memory_node n
            WHERE n.engine = @engine AND n.task_key = @taskKey
              AND (@scope IS NULL OR n.scope = @scope)
            ORDER BY (n.grade = @authoritative) DESC, n.salience DESC, n.last_recalled_position DESC, n.id DESC
            LIMIT @limit
            """, new { engine, taskKey, scope, position, ordinal, chars, limit, authoritative = Authoritative },
            totals.EncodedAt, ct).ConfigureAwait(false);
    }

    /// <summary>One seeding pass over a term-wise clause. Extracted so the index-friendly and widened passes
    /// are the SAME query differing in exactly one argument, and so the grade carve-out cannot be kept by
    /// only one of them.
    /// <para><c>Matched</c> is selected ONLY here, and it is what makes an admitted-but-non-matching exact
    /// fact report <c>0</c> despite this backend's grade-first ORDER BY putting it at the HEAD of the
    /// gradient. <c>NodeRow.Matched</c> is nullable so the no-query path, which omits the column, keeps the
    /// plain gradient rather than zeroing every row.</para>
    /// <para>The term COUNT leads the ordering within a grade: an OR match is otherwise unranked, so a row
    /// brushing one term could displace a near-exact hit. It is this backend's coarse stand-in for the bm25
    /// SQLite ranks by.</para></summary>
    private async Task<IReadOnlyList<GraphNode>> SeedByTermsAsync(IDbConnection conn,
        string engine, string taskKey, string? scope, string? query, int limit, MemoryPositionRow totals,
        bool includeShortTerms, CancellationToken ct)
    {
        var kw = SearchTerms.LikeClause(query, ["n.content", "n.headline"], "ILIKE", includeShortTerms: includeShortTerms);
        var p = new DynamicParameters(new
        {
            engine, taskKey, scope,
            position = totals.Position, ordinal = totals.Ordinal, chars = totals.Chars,
            limit, authoritative = Authoritative,
        });
        foreach (var (name, value) in kw.Parameters) p.Add(name, value);

        return await QueryAsync(conn, $"""
            SELECT {NodeColumns},
                   {kw.Predicate} AS "Matched"
            FROM lyntai_memory_node n
            WHERE n.engine = @engine AND n.task_key = @taskKey
              AND (@scope IS NULL OR n.scope = @scope)
              AND (n.grade = @authoritative OR {kw.Predicate})
            ORDER BY (n.grade = @authoritative) DESC, {kw.MatchCount} DESC,
                     n.salience DESC, n.last_recalled_position DESC, n.id DESC
            LIMIT @limit
            """, p, totals.EncodedAt, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphNeighbour>> NeighboursAsync(string engine,
        IReadOnlyCollection<long> ids, int limit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ct.ThrowIfCancellationRequested();
        if (ids.Count == 0) return [];

        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var totals = await TotalsAsync(conn, engine, ct).ConfigureAwait(false);
        var position = totals.Position;
        var idArray = ids.ToArray();
        // = ANY / <> ALL over a bound array rather than Dapper's IN expansion: Npgsql binds an array
        // natively, so the statement text stays constant and stays plan-cacheable
        // One MemoryEdgeRow rather than five more positional splits — see the SQLite twin.
        var rows = await conn.QueryAsync<MemoryNodeRow, MemoryEdgeRow, GraphNeighbour>(
            new CommandDefinition($"""
                SELECT {NodeColumns}, x.w AS "EdgeWeight", (@position - x.at) AS "EdgeAge",
                       (@ordinal - x.ord) AS "EdgeOrdinalAge", (@chars - x.ch) AS "EdgeVolumeAge",
                       x.whn AS "EdgeStrengthenedAt"
                FROM (SELECT e.to_id AS id, MAX(e.weight) AS w, MAX(e.strengthened_position) AS at,
                             MAX(e.strengthened_ordinal) AS ord, MAX(e.strengthened_chars) AS ch,
                             MAX(e.strengthened_at) AS whn
                      FROM lyntai_memory_edge e
                      WHERE e.from_id = ANY(@idArray) AND e.to_id <> ALL(@idArray)
                      GROUP BY e.to_id) x
                JOIN lyntai_memory_node n ON n.id = x.id
                WHERE n.engine = @engine
                ORDER BY x.w DESC, n.id DESC
                LIMIT @limit
                """, new { idArray, engine, limit, position, ordinal = totals.Ordinal, chars = totals.Chars },
                cancellationToken: ct),
            (row, edge) => new GraphNeighbour(row.ToNode(totals.EncodedAt), edge.EdgeWeight, edge.EdgeAge,
                EdgeOrdinalAge: edge.EdgeOrdinalAge, EdgeVolumeAge: edge.EdgeVolumeAge,
                EdgeElapsedAge: edge.EdgeStrengthenedAt is { } at
                    ? (totals.EncodedAt - at).TotalDays
                    : 0),
            splitOn: "EdgeWeight").ConfigureAwait(false);
        return [.. rows];
    }

    /// <inheritdoc />
    public async Task<GraphNode?> GetAsync(string engine, long id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var totals = await TotalsAsync(conn, engine, ct).ConfigureAwait(false);
        var hits = await QueryAsync(conn,
            $"SELECT {NodeColumns} FROM lyntai_memory_node n WHERE n.id = @id AND n.engine = @engine",
            new { id, engine, position = totals.Position, ordinal = totals.Ordinal, chars = totals.Chars },
            totals.EncodedAt, ct).ConfigureAwait(false);
        return hits.Count == 0 ? null : hits[0];
    }

    /// <inheritdoc />
    public async Task TouchAsync(string engine, IReadOnlyCollection<GraphTouch> touches,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(touches);
        ct.ThrowIfCancellationRequested();
        if (touches.Count == 0) return;

        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        // a recall does NOT advance the position or any of the three primitives — it stamps the touched
        // node's own snapshot to wherever the engine already is, on every scale at once
        var totals = await TotalsAsync(conn, engine, ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE lyntai_memory_node
            SET last_recalled_position = @position, stability = @Stability, difficulty = @Difficulty,
                provenance_retrievability = @ProvenanceRetrievability,
                recall_count = lyntai_memory_node.recall_count + 1,
                encoding_ordinal = @ordinal, encoding_chars = @chars, encoding_at = @encodedAt
            WHERE id = @Id AND engine = @engine
            """, touches.Select(t => new
            {
                t.Id, t.Stability, t.Difficulty, t.ProvenanceRetrievability, engine,
                position = totals.Position, ordinal = totals.Ordinal, chars = totals.Chars,
                encodedAt = totals.EncodedAt,
            }),
            cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task LinkAsync(string engine, long from, long to, string? kind, double weight,
        bool symmetric, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (from == to) return; // a self-edge is never useful and would skew Degree

        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var totals = await TotalsAsync(conn, engine, ct).ConfigureAwait(false);
        await StrengthenAsync(conn, from, to, kind ?? "", weight, totals, ct).ConfigureAwait(false);
        if (symmetric)
            await StrengthenAsync(conn, to, from, kind ?? "", weight, totals, ct).ConfigureAwait(false);
    }

    // Postgres requires the table reference in the DO UPDATE SET expression, where SQLite takes a bare
    // column — one of the dialect differences that make sharing this text impossible.
    // Stamps all FOUR strengthening marks from one totals snapshot, exactly as the SQLite twin does.
    private static Task StrengthenAsync(IDbConnection conn, long from, long to, string kind, double weight,
        MemoryPositionRow totals, CancellationToken ct) =>
        conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO lyntai_memory_edge (from_id, to_id, kind, weight, strengthened_position,
                strengthened_ordinal, strengthened_chars, strengthened_at)
            VALUES (@from, @to, @kind, @weight, @position, @ordinal, @chars, @at)
            ON CONFLICT (from_id, to_id, kind)
                DO UPDATE SET weight = lyntai_memory_edge.weight + @weight,
                              strengthened_position = @position,
                              strengthened_ordinal = @ordinal,
                              strengthened_chars = @chars,
                              strengthened_at = @at
            """,
            new
            {
                from, to, kind, weight, position = totals.Position, ordinal = totals.Ordinal,
                chars = totals.Chars, at = totals.EncodedAt,
            }, cancellationToken: ct));

    /// <inheritdoc />
    public async Task<int> PruneAsync(string engine, string taskKey, string? scope,
        double? maxAgeOverStability, TimeSpan? olderThan, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var cut = maxAgeOverStability;
        var createdBefore = olderThan is null ? (DateTimeOffset?)null : _clock() - olderThan.Value;
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var position = (await TotalsAsync(conn, engine, ct).ConfigureAwait(false)).Position;
        // Shared with the SQLite twin apart from the zero-stability guard's spelling; edges follow the nodes
        // through ON DELETE CASCADE.
        return await conn.ExecuteAsync(new CommandDefinition(MemoryGraphSql.Prune(AgeOverStability),
            new { engine, taskKey, scope, cut, createdBefore, position, authoritative = Authoritative },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> DeleteAsync(string engine, IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ct.ThrowIfCancellationRequested();
        if (ids.Count == 0) return 0;
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteAsync(new CommandDefinition("""
            DELETE FROM lyntai_memory_node WHERE engine = @engine AND id = ANY(@ids)
            """, new { engine, ids = ids.ToArray() }, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ForgetAsync(string engine, string taskKey, string? scope,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(MemoryGraphSql.Forget,
            new { engine, taskKey, scope }, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RecordReviewsAsync(string engine, IReadOnlyCollection<MemoryReviewWrite> reviews,
        int cap, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        ct.ThrowIfCancellationRequested();
        if (reviews.Count == 0) return;

        var effectiveCap = Math.Max(0, cap);
        var now = _clock();
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(MemoryGraphSql.InsertReview,
            reviews.Select(r => new
            {
                engine, now, r.NodeId, batchId = r.BatchId.ToString(), r.PreAge, r.PreStability,
                r.PreDifficulty, r.PreStrength, r.PreStrengthAge, r.Grade, r.PostStability, r.PostDifficulty,
                r.ProvenanceRetrievability, r.Verified,
            }),
            cancellationToken: ct)).ConfigureAwait(false);

        // MemoryReviewLogPacing (Task 3) — same strategy and trade-off as the SQLite twin's own comment.
        var before = 0L;
        _reviewCounters.AddOrUpdate(engine, reviews.Count,
            (_, existing) => { before = existing; return existing + reviews.Count; });
        if (MemoryReviewLogPacing.CrossesBoundary(before, reviews.Count, effectiveCap))
            await conn.ExecuteAsync(new CommandDefinition(MemoryGraphSql.TrimReviews,
                new { engine, cap = effectiveCap }, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryReview>> ReviewsAsync(string engine, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var rows = (await conn.QueryAsync<ReviewRow>(new CommandDefinition("""
            SELECT id AS "Id", engine AS "Engine", node_id AS "NodeId", batch_id AS "BatchId",
                   created_at AS "CreatedAt", pre_age AS "PreAge", pre_stability AS "PreStability",
                   pre_difficulty AS "PreDifficulty", pre_strength AS "PreStrength",
                   pre_strength_age AS "PreStrengthAge", grade AS "Grade",
                   post_stability AS "PostStability", post_difficulty AS "PostDifficulty",
                   provenance_retrievability AS "ProvenanceRetrievability",
                   verified AS "Verified"
            FROM lyntai_memory_review WHERE engine = @engine ORDER BY id
            """, new { engine }, cancellationToken: ct)).ConfigureAwait(false)).AsList();
        // The native bool? binds straight through here; the other thirteen columns project through the
        // shared row, exactly as on the SQLite twin.
        return [.. rows.Select(r => r.ToReview(r.Verified))];
    }

    /// <inheritdoc />
    public async Task RecordSubjectsAsync(string engine, long nodeId, IReadOnlyCollection<string> subjects,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        ct.ThrowIfCancellationRequested();
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);

        // REPLACE, never accumulate — see the SQLite twin. The delete runs even for an empty set, which is
        // how an annotator that changes its mind to "no opinion" actually clears them.
        await conn.ExecuteAsync(new CommandDefinition(MemoryGraphSql.DeleteSubjects,
            new { engine, nodeId }, cancellationToken: ct)).ConfigureAwait(false);

        // MemorySubject.Canonicalize is the ONE normalization — see the SQLite twin.
        var rows = MemorySubject.Canonicalize(subjects);
        if (rows.Count == 0) return;

        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO lyntai_memory_subject (engine, node_id, task_key, scope, subject)
            SELECT @engine, n.id, n.task_key, n.scope, @subject
            FROM lyntai_memory_node n WHERE n.id = @nodeId AND n.engine = @engine
            ON CONFLICT (engine, node_id, subject) DO NOTHING
            """, rows.Select(subject => new { engine, nodeId, subject }), cancellationToken: ct))
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> KnownSubjectsAsync(string engine, string taskKey, string? scope,
        int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (limit <= 0) return [];

        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        // COLLATE "C" on the tie-break for the same reason the vector and curated stores carry it: the
        // contract is byte order (SQLite's BINARY, StringComparer.Ordinal in process), and under the
        // database's own locale a tied set would TRUNCATE differently here than on the other two backends.
        var rows = await conn.QueryAsync<string>(new CommandDefinition("""
            SELECT subject FROM lyntai_memory_subject
            WHERE engine = @engine AND task_key = @taskKey AND (@scope::text IS NULL OR scope = @scope)
            GROUP BY subject ORDER BY COUNT(*) DESC, subject COLLATE "C" LIMIT @limit
            """, new { engine, taskKey, scope, limit }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<long>> NodesBySubjectAsync(string engine, string taskKey, string? scope,
        string subject, int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!MemorySubject.IsUsable(subject) || limit <= 0) return [];

        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<long>(new CommandDefinition("""
            SELECT s.node_id
            FROM lyntai_memory_subject s
            JOIN lyntai_memory_node n ON n.id = s.node_id
            WHERE s.engine = @engine AND s.subject = @subject AND s.task_key = @taskKey
              AND (@scope::text IS NULL OR s.scope = @scope)
            ORDER BY s.node_id DESC
            LIMIT @limit
            """,
            // the same normalization the write applied, so a lookup can never miss a handle it stored
            new { engine, subject = MemorySubject.Normalize(subject), taskKey, scope, limit },
            cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows];
    }

    /// <summary>Where the engine currently stands, on every scale a store tracks: the legacy,
    /// <c>Advance</c>-driven <see cref="MemoryPositionRow.Position"/>, and the three primitives that advance
    /// unconditionally. All default to their zero value for an engine nothing has been written to — which is
    /// correct: nothing has happened in it, so nothing has aged.</summary>
    private static async Task<MemoryPositionRow> TotalsAsync(IDbConnection conn, string engine, CancellationToken ct) =>
        await conn.QuerySingleOrDefaultAsync<MemoryPositionRow>(new CommandDefinition("""
            SELECT position AS "Position", ordinal AS "Ordinal", chars AS "Chars", encoded_at AS "EncodedAt"
            FROM lyntai_memory_position WHERE engine = @engine
            """, new { engine }, cancellationToken: ct)).ConfigureAwait(false)
        ?? new MemoryPositionRow { EncodedAt = DateTimeOffset.UnixEpoch };

    private static async Task<IReadOnlyList<GraphNode>> QueryAsync(IDbConnection conn, string sql,
        object parameters, DateTimeOffset encodedAt, CancellationToken ct)
    {
        var rows = (await conn.QueryAsync<MemoryNodeRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct)).ConfigureAwait(false)).AsList();
        // MemoryRelevance.ByRankPosition is the ONE rule, shared with the SQLite twin, including its contract
        // clause: a row admitted by GRADE that the query never matched reports 0. Only the substring query
        // selects `Matched`; elsewhere it is null ("not asked") and the plain gradient applies.
        return [.. rows.Select((row, i) =>
            row.ToNode(encodedAt) with { Relevance = MemoryRelevance.ByRankPosition(i, rows.Count, row.Matched) })];
    }

    /// <summary>This backend's half of a review row: the thirteen shared columns come from
    /// <see cref="MemoryReviewRow"/>, and only <see cref="Verified"/> is declared here.
    /// <para>Postgres has a native BOOLEAN so this binds directly, where the SQLite twin must type the same
    /// column <c>long?</c> and convert. That is a real dialect difference rather than an accident, which is
    /// why the shared row declines to declare it — and it stays NULLABLE for a reason that is not dialect at
    /// all: "judged not relevant" and "never judged" are different observations (see
    /// <see cref="MemoryReviewWrite.Verified"/>).</para></summary>
    private sealed class ReviewRow : MemoryReviewRow
    {
        public bool? Verified { get; set; }
    }
}
