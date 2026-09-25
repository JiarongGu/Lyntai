using System.Collections.Concurrent;
using System.Data;
using Dapper;
using Lyntai.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Storage.Postgres;

/// <summary>PostgreSQL <see cref="IMemoryGraphStore"/> over <c>lyntai_memory_node</c> +
/// <c>lyntai_memory_edge</c>, with substring recall through the <c>pg_trgm</c> GIN index.
/// <para><b>What the SQLite twin can share, it does</b>: the row types, the projection to
/// <see cref="GraphNode"/> and every portable statement live once in <see cref="MemoryNodeRow"/> and
/// <see cref="MemoryGraphSql"/> (<c>docs/DECISIONS.md</c> D77). What stays here is what the dialect forces:
/// <c>GREATEST</c>, <c>= ANY(@ids)</c>, <c>::jsonb</c>, <c>COLLATE "C"</c> and the two-pass <c>ILIKE</c> seed.</para>
/// <para><b>Age is a subtraction, not a duration</b>, and <b>the decay curve is never evaluated here</b>.
/// What the age marks mean is <see cref="MemoryNodeRow"/>'s, once.</para>
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
    // Per-engine review-write counters for MemoryReviewLogPacing — in-process only, never persisted (a SOFT
    // cap). Concurrent: this store makes no single-writer guarantee across calls.
    private readonly ConcurrentDictionary<string, long> _reviewCounters = new(StringComparer.Ordinal);

    // GREATEST guards a zero stability: dividing by zero raises here rather than yielding NULL as it does
    // on SQLite, but the guard is what keeps both backends agreeing instead of one erroring. PruneAsync's
    // alone: seeding applies no faintness bound at all (IMemoryGraphStore.SeedAsync).
    private const string AgeOverStability =
        "(@position - n.last_recalled_position) / GREATEST(n.stability, "
        + MemoryGraphSql.MinimumStability + ")";

    // Bound as a parameter, never a literal: MemoryGrade is Inherit=0, Associative=1, Authoritative=2, so
    // a hand-written "grade = 1" silently means the OPPOSITE of what it reads like.
    private static readonly int Authoritative = (int)MemoryGrade.Authoritative;

    /// <inheritdoc />
    public async Task<long> UpsertAsync(GraphNodeWrite write, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        var hash = MemoryContentKey.Of(write.Content);
        var metadata = CuratedMetadataJson.Serialize(write.Metadata);
        // NULL, not "{}", for an empty bag — the DO UPDATE SET below reads NULL as "no opinion" and keeps
        // the stored signals. See GraphNodeWrite.Signals.
        var signals = MemorySignalsJson.Serialize(write.Signals);
        // The COERCED salience the database sorts on — MemorySignals.Salience also carries the non-finite
        // guard, and on this backend an unguarded NaN is bound silently and then sorts ABOVE every number.
        var salience = MemorySignals.Salience(write.Signals);
        // Difficulty has a second writer (TouchAsync), so only a bag that NAMES one overwrites it —
        // MemoryDecayState.Difficulty.
        var hasDifficultySignal = write.Signals.Values.ContainsKey(MemorySignals.WellKnown.Difficulty);
        var difficulty = MemorySignals.Difficulty(write.Signals);
        var now = _clock();
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        // Advance FIRST and atomically, position and the three primitives together.
        var totals = await conn.QuerySingleAsync<MemoryPositionRow>(new CommandDefinition(
            MemoryGraphSql.AdvancePosition, new
            {
                engine = write.Engine, advance = Math.Max(0, write.Advance),
                contentLength = write.Content.Length, now,
            }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        // The SET clauses are the SQLite twin's, commented there; ::jsonb is this dialect's own (Npgsql binds
        // a string as text, which a jsonb column refuses).
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
                              headline = CASE WHEN @headlineStated THEN @headline ELSE lyntai_memory_node.headline END,
                              grade = CASE WHEN @gradeStated THEN @grade ELSE lyntai_memory_node.grade END,
                              signals = COALESCE(@signals::jsonb, lyntai_memory_node.signals),
                              metadata = COALESCE(@metadata, lyntai_memory_node.metadata),
                              salience = CASE WHEN @signals::jsonb IS NULL THEN lyntai_memory_node.salience ELSE @salience END,
                              difficulty = CASE WHEN @hasDifficultySignal THEN @difficulty ELSE lyntai_memory_node.difficulty END,
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
        if (limit <= 0) return []; // asks for nothing — never the dialect's opinion of a negative LIMIT
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var totals = await TotalsAsync(conn, engine, ct).ConfigureAwait(false);
        var args = new
        {
            engine, taskKey, scope, position = totals.Position, ordinal = totals.Ordinal, chars = totals.Chars,
            limit, authoritative = Authoritative,
        };

        if (string.IsNullOrWhiteSpace(query))
            return await SeedQueryAsync(conn, MemoryGraphSql.SeedRecent, args, totals.EncodedAt, ct).ConfigureAwait(false);

        // TWO PASSES, and the second is where the whole cost of CJK correctness is contained. Pass 1 keeps
        // every ILIKE pattern at three characters or more, so pg_trgm's GIN index can serve it; pass 2 adds
        // the two-character terms of a spaceless script, which the index cannot serve — a sequential scan,
        // paid only when the fast pass MATCHED nothing (D55). Most Chinese content words are two characters,
        // so pass 2 is the reason a CJK consumer gets an answer at all.
        var narrow = await SeedByTermsAsync(conn, query, args, totals, includeShortTerms: false, ct).ConfigureAwait(false);

        // a seed carrying only grade-admitted non-matches is exactly the miss pass 2 is for
        if (narrow.Any(n => n.Matched == true) || !SearchTerms.HasShortSpacelessTerms(query)) return narrow;

        return await SeedByTermsAsync(conn, query, args, totals, includeShortTerms: true, ct).ConfigureAwait(false);
    }

    // One pass of the shared term-wise seed, so both passes are the SAME query differing in one argument.
    private static async Task<IReadOnlyList<GraphNode>> SeedByTermsAsync(IDbConnection conn, string query,
        object args, MemoryPositionRow totals, bool includeShortTerms, CancellationToken ct)
    {
        var kw = SearchTerms.LikeClause(query, ["n.content", "n.headline"], "ILIKE", includeShortTerms: includeShortTerms);
        var p = new DynamicParameters(args);
        foreach (var (name, value) in kw.Parameters) p.Add(name, value);
        return await SeedQueryAsync(conn, MemoryGraphSql.SeedMatching(kw), p, totals.EncodedAt, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphNeighbour>> NeighboursAsync(string engine, string taskKey,
        IReadOnlyCollection<long> ids, int limit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ct.ThrowIfCancellationRequested();
        if (ids.Count == 0 || limit <= 0) return [];

        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var totals = await TotalsAsync(conn, engine, ct).ConfigureAwait(false);
        var idArray = ids.ToArray();
        // = ANY / <> ALL over a bound array rather than Dapper's IN expansion: Npgsql binds an array
        // natively, so the statement text stays constant and stays plan-cacheable. One MemoryEdgeRow, as on
        // the SQLite twin.
        var rows = await conn.QueryAsync<MemoryNodeRow, MemoryEdgeRow, GraphNeighbour>(
            new CommandDefinition($"""
                SELECT {MemoryGraphSql.NodeColumns}, x.w AS EdgeWeight, (@position - x.at) AS EdgeAge,
                       (@ordinal - x.ord) AS EdgeOrdinalAge, (@chars - x.ch) AS EdgeVolumeAge,
                       x.whn AS EdgeStrengthenedAt
                FROM (SELECT e.to_id AS id, MAX(e.weight) AS w, MAX(e.strengthened_position) AS at,
                             MAX(e.strengthened_ordinal) AS ord, MAX(e.strengthened_chars) AS ch,
                             MAX(e.strengthened_at) AS whn
                      FROM lyntai_memory_edge e
                      WHERE e.from_id = ANY(@idArray) AND e.to_id <> ALL(@idArray)
                      GROUP BY e.to_id) x
                JOIN lyntai_memory_node n ON n.id = x.id
                -- the walk stays inside the task: an edge that crosses one must not carry a
                -- recall across with it, or taskKey is a boundary for every read but this
                WHERE n.engine = @engine AND n.task_key = @taskKey
                ORDER BY x.w DESC, n.id DESC
                LIMIT @limit
                """, new { idArray, engine, taskKey, limit, position = totals.Position, ordinal = totals.Ordinal, chars = totals.Chars },
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
        // projected, never scored: a fetch by id asked no relevance question (D97)
        var row = await conn.QuerySingleOrDefaultAsync<MemoryNodeRow>(new CommandDefinition(MemoryGraphSql.NodeById,
            new { id, engine, position = totals.Position, ordinal = totals.Ordinal, chars = totals.Chars },
            cancellationToken: ct)).ConfigureAwait(false);
        return row?.ToNode(totals.EncodedAt);
    }

    /// <inheritdoc />
    public async Task TouchAsync(string engine, IReadOnlyCollection<GraphTouch> touches,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(touches);
        ct.ThrowIfCancellationRequested();
        if (touches.Count == 0) return;

        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var totals = await TotalsAsync(conn, engine, ct).ConfigureAwait(false);
        await TouchAsync(conn, engine, touches, totals, ct).ConfigureAwait(false);
    }

    // The statement itself, over a connection and a totals snapshot the caller owns — so the single-member
    // path and WriteBackAsync run identically and cannot drift into two spellings of one write.
    private static Task TouchAsync(IDbConnection conn, string engine,
        IReadOnlyCollection<GraphTouch> touches, MemoryPositionRow totals, CancellationToken ct) =>
        conn.ExecuteAsync(new CommandDefinition(MemoryGraphSql.Touch, touches.Select(t => new
            {
                t.Id, t.Stability, t.Difficulty, t.ProvenanceRetrievability, engine,
                position = totals.Position, ordinal = totals.Ordinal, chars = totals.Chars,
                encodedAt = totals.EncodedAt,
            }),
            cancellationToken: ct));

    /// <inheritdoc />
    public async Task LinkAsync(string engine, long from, long to, string? kind, double weight,
        bool symmetric, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (from == to) return; // a self-edge is never useful and would skew Degree

        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var totals = await TotalsAsync(conn, engine, ct).ConfigureAwait(false);
        await LinkManyAsync(conn, [new GraphEdgeWrite(from, to, kind, weight, symmetric)], totals, ct)
            .ConfigureAwait(false);
    }

    /// <summary>The batched form: ONE connection and ONE position-totals read for every edge, where the
    /// single-edge path pays both per call. Why it is worth overriding, and what an override must keep, is
    /// <see cref="IMemoryGraphStore.LinkManyAsync"/>'s own doc.</summary>
    public async Task LinkManyAsync(string engine, IReadOnlyList<GraphEdgeWrite> edges,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edges);
        ct.ThrowIfCancellationRequested();
        if (edges.Count == 0) return;

        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        // ONE snapshot for the whole batch — the correctness half of IMemoryGraphStore.LinkManyAsync.
        var totals = await TotalsAsync(conn, engine, ct).ConfigureAwait(false);
        await LinkManyAsync(conn, edges, totals, ct).ConfigureAwait(false);
    }

    // As with TouchAsync above: the loop over a caller-owned connection and snapshot, so WriteBackAsync
    // reuses it rather than restating it.
    private static async Task LinkManyAsync(IDbConnection conn, IReadOnlyList<GraphEdgeWrite> edges,
        MemoryPositionRow totals, CancellationToken ct)
    {
        foreach (var e in edges)
        {
            ct.ThrowIfCancellationRequested();
            if (e.From == e.To) continue;   // a self-edge is never useful and would skew Degree
            await StrengthenAsync(conn, e.From, e.To, e.Kind ?? "", e.Weight, totals, ct).ConfigureAwait(false);
            if (e.Symmetric)
                await StrengthenAsync(conn, e.To, e.From, e.Kind ?? "", e.Weight, totals, ct).ConfigureAwait(false);
        }
    }

    private static Task StrengthenAsync(IDbConnection conn, long from, long to, string kind,
        double weight, MemoryPositionRow totals, CancellationToken ct) =>
        conn.ExecuteAsync(new CommandDefinition(MemoryGraphSql.StrengthenEdge,
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

        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await RecordReviewsAsync(conn, engine, reviews, cap, ct).ConfigureAwait(false);
    }

    // ONE call site on purpose: the pacing counter is INSTANCE state, so a second copy of the AddOrUpdate
    // would advance it twice and trim at double the cadence MemoryReviewLogPacing sets.
    private async Task RecordReviewsAsync(IDbConnection conn, string engine,
        IReadOnlyCollection<MemoryReviewWrite> reviews, int cap, CancellationToken ct)
    {
        var effectiveCap = Math.Max(0, cap);
        var now = _clock();
        await conn.ExecuteAsync(new CommandDefinition(MemoryGraphSql.InsertReview,
            reviews.Select(r => new
            {
                engine, now, r.NodeId, batchId = r.BatchId.ToString(), r.PreAge, r.PreStability,
                r.PreDifficulty, r.PreStrength, r.PreStrengthAge, r.ReviewGrade, r.PostStability, r.PostDifficulty,
                r.ProvenanceRetrievability, r.Verified,
            }),
            cancellationToken: ct)).ConfigureAwait(false);

        var before = 0L;
        _reviewCounters.AddOrUpdate(engine, reviews.Count,
            (_, existing) => { before = existing; return existing + reviews.Count; });
        if (MemoryReviewLogPacing.CrossesBoundary(before, reviews.Count, effectiveCap))
            await conn.ExecuteAsync(new CommandDefinition(MemoryGraphSql.TrimReviews,
                new { engine, cap = effectiveCap }, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>The whole write-back on ONE connection and ONE position-totals read, where the three
    /// separate calls paid three opens and two reads — and on a networked backend an open is the expensive
    /// one. Each part's SQL is the same statement its own member runs.</summary>
    public async Task WriteBackAsync(string engine, GraphWriteBack work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        ct.ThrowIfCancellationRequested();
        if (work.Touches.Count == 0 && work.Edges.Count == 0 && work.Reviews.Count == 0) return;

        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        // Scoped rather than hoisted: these two are the only parts that read the position, so a
        // reviews-only write-back pays for no totals read at all.
        if (work.Touches.Count > 0 || work.Edges.Count > 0)
        {
            var totals = await TotalsAsync(conn, engine, ct).ConfigureAwait(false);
            if (work.Touches.Count > 0)
                await TouchAsync(conn, engine, work.Touches, totals, ct).ConfigureAwait(false);
            if (work.Edges.Count > 0)
                await LinkManyAsync(conn, work.Edges, totals, ct).ConfigureAwait(false);
        }

        // LAST by contract — see IMemoryGraphStore.WriteBackAsync: a broken log must cost neither of the above
        if (work.Reviews.Count > 0)
            await RecordReviewsAsync(conn, engine, work.Reviews, work.ReviewLogCap, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryReview>> ReviewsAsync(string engine, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var rows = (await conn.QueryAsync<ReviewRow>(new CommandDefinition(MemoryGraphSql.Reviews,
            new { engine }, cancellationToken: ct)).ConfigureAwait(false)).AsList();
        return [.. rows.Select(r => r.ToReview(r.Verified))];
    }

    /// <inheritdoc />
    public async Task RecordSubjectsAsync(string engine, long nodeId, IReadOnlyCollection<string> subjects,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        ct.ThrowIfCancellationRequested();
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);

        await conn.ExecuteAsync(new CommandDefinition(MemoryGraphSql.DeleteSubjects,
            new { engine, nodeId }, cancellationToken: ct)).ConfigureAwait(false);

        // MemorySubject.Canonicalize is the ONE normalization, shared with every backend and any BYO store
        var rows = MemorySubject.Canonicalize(subjects);
        if (rows.Count == 0) return;

        await conn.ExecuteAsync(new CommandDefinition(MemoryGraphSql.InsertSubject,
            rows.Select(subject => new { engine, nodeId, subject }), cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> KnownSubjectsAsync(string engine, string taskKey, string? scope,
        int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (limit <= 0) return [];

        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        // COLLATE "C" on the tie-break: the contract is byte order, and under the database's own locale a
        // tied set would TRUNCATE differently here than on the other backends.
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

    private static async Task<MemoryPositionRow> TotalsAsync(IDbConnection conn, string engine, CancellationToken ct) =>
        await conn.QuerySingleOrDefaultAsync<MemoryPositionRow>(new CommandDefinition(MemoryGraphSql.Totals,
            new { engine }, cancellationToken: ct)).ConfigureAwait(false)
        ?? new MemoryPositionRow { EncodedAt = DateTimeOffset.UnixEpoch };

    // Every caller is a SEED, which asked: the substring query selects `Matched` per row, and a query that
    // does not select it matched everything it returned (D97).
    private static async Task<IReadOnlyList<GraphNode>> SeedQueryAsync(IDbConnection conn, string sql,
        object parameters, DateTimeOffset encodedAt, CancellationToken ct)
    {
        var rows = (await conn.QueryAsync<MemoryNodeRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct)).ConfigureAwait(false)).AsList();
        return [.. rows.Select((row, i) => row.ToNode(encodedAt) with
        {
            Relevance = MemoryRelevance.ByRankPosition(i, rows.Count, row.Matched),
            Matched = row.Matched ?? true,
        })];
    }

    /// <summary>This backend's half of a review row: Postgres binds the <c>verified</c> tri-state as a native
    /// <c>bool?</c>, where the SQLite twin must type the same column <c>long?</c>. NULLABLE because "judged not
    /// relevant" and "never judged" are different observations (<see cref="MemoryReviewWrite.Verified"/>).</summary>
    private sealed class ReviewRow : MemoryReviewRow
    {
        public bool? Verified { get; set; }
    }
}
