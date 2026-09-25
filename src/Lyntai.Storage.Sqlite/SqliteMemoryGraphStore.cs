using System.Collections.Concurrent;
using System.Data;
using Dapper;
using Lyntai.Memory;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Storage.Sqlite;

/// <summary>SQLite <see cref="IMemoryGraphStore"/> over <c>lyntai_memory_node</c> +
/// <c>lyntai_memory_edge</c>, with recall through the <c>lyntai_memory_node_fts</c> trigram index
/// (bm25-ranked, LIKE fallback) — the same machinery as <see cref="SqliteMemoryStore"/>.
/// <para><b>Seeding orders by GRADE first, then recency</b> — authoritative material is admitted
/// unconditionally, and leading with recency would let the candidate <c>LIMIT</c> cut the quietest exact
/// fact before the engine ranked anything (the same guarantee, reached by ordering rather than by predicate).</para>
/// <para><b>The FTS path is TWO queries merged</b>, alone among the backends: an FTS5 <c>MATCH</c> predicate
/// cannot carry the grade carve-out and keep its bm25 ordering, so the scope's exact facts are fetched
/// separately and combined by <c>Merge</c>, which reserves capacity for them rather than appending.</para>
/// <para><b>Age is a subtraction, not a duration</b>, and no date arithmetic appears in any query here — on
/// this backend that also avoids <c>julianday</c> returning NULL for an unparseable timestamp and silently
/// excluding every row. What the age marks mean is <see cref="MemoryNodeRow"/>'s, once.</para>
/// <para><b>What the Postgres twin can share, it does</b>: the row types, the projection to
/// <see cref="GraphNode"/> and every portable statement live once in <see cref="MemoryNodeRow"/> and
/// <see cref="MemoryGraphSql"/> (<c>docs/DECISIONS.md</c> D77); the FTS5 seed and this dialect's id lists stay
/// here.</para>
/// <para><b>The decay curve is never evaluated here</b> — the only faintness bound is <see cref="PruneAsync"/>'s
/// plain <c>age / stability &lt;= @cut</c>, supplied by <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy.CandidateCutoff"/>.
/// And <see cref="GraphNode.Relevance"/> is <b>this backend's own rank position, normalized</b> to the
/// contractual 0..1: <c>bm25()</c> returns an unbounded negative score, so rather than invent a
/// normalization the store reports a monotone transform of its own ordering.</para></summary>
/// <param name="factory">Connection factory — the ONLY way to open a connection here, because it applies
/// <c>foreign_keys=ON</c> per connection and the edge cascade depends on it.</param>
/// <param name="logger">Optional.</param>
/// <param name="clock">Time source for the audit timestamps; null takes the system clock.</param>
public sealed class SqliteMemoryGraphStore(
    IDbConnectionFactory factory,
    ILogger<SqliteMemoryGraphStore>? logger = null,
    Func<DateTimeOffset>? clock = null) : IMemoryGraphStore
{
    private readonly ILogger _logger = logger ?? NullLogger<SqliteMemoryGraphStore>.Instance;
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    // Per-engine review-write counters for MemoryReviewLogPacing — in-process only, never persisted (a SOFT
    // cap; see that type). Concurrent because, unlike every other member here, this one is mutated OUTSIDE a
    // per-call connection/transaction and this store itself makes no single-writer guarantee.
    private readonly ConcurrentDictionary<string, long> _reviewCounters = new(StringComparer.Ordinal);

    // The MAX() guard is load-bearing: a zero stability divides by zero, SQLite evaluates that to NULL, and
    // a NULL predicate excludes the row SILENTLY — losing a memory rather than erroring. PruneAsync's alone:
    // seeding applies no faintness bound at all (IMemoryGraphStore.SeedAsync).
    private const string AgeOverStability =
        "(@position - n.last_recalled_position) / MAX(CAST(n.stability AS REAL), "
        + MemoryGraphSql.MinimumStability + ")";

    // Bound as a PARAMETER rather than written as a literal: MemoryGrade is Inherit=0, Associative=1,
    // Authoritative=2, so a hand-written "grade = 1" silently means the OPPOSITE of what it reads like.
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
        // The column is the COERCED salience the database sorts on; the bag above is stored VERBATIM, so the
        // two are deliberately not byte-for-byte equal — a bag holding 0.5 reads back 0.5 while the column
        // holds 1. They cannot DRIFT: one write.Signals, one statement, and MemorySignals.Salience is the
        // single definition of the coercion (and of the non-finite guard).
        var salience = MemorySignals.Salience(write.Signals);
        // The LIVE difficulty column — promoted from the bag, but NOT on the trigger salience uses: it has a
        // second writer (TouchAsync), so only a bag that NAMES a difficulty overwrites it. See
        // MemoryDecayState.Difficulty for the full rule.
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

        // atomic on ux_lyntai_memory_node_dedup: identical content REFRESHES rather than duplicating.
        // RETURNING id, never last_insert_rowid() — that is per-connection and returns 0 on another
        // pooled connection.
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO lyntai_memory_node
                (engine, task_key, scope, headline, content, content_hash, grade, metadata, signals, salience,
                 created_at, last_recalled_position, recall_count, stability, difficulty,
                 encoding_ordinal, encoding_chars, encoding_at,
                 provenance_retrievability, provenance_salience)
            VALUES (@engine, @taskKey, @scope, @headline, @content, @hash, @grade, @metadata, @signals, @salience,
                    @now, @position, 0, @stability, @difficulty, @ordinal, @chars, @now,
                    @provenanceRetrievability, @provenanceSalience)
            ON CONFLICT(engine, task_key, scope, content_hash)
                DO UPDATE SET last_recalled_position = @position,
                              -- an AUTHORED headline survives a refresh that does not restate it:
                              -- the engine derives one from the content when the caller supplies
                              -- none, and overwriting with that discards the caller's own text
                              headline = CASE WHEN @headlineStated THEN @headline ELSE lyntai_memory_node.headline END,
                              -- only a CALLER-NAMED grade overwrites: MemoryGrade.Inherit resolves to
                              -- the engine's role, so without this a re-remember that did not restate
                              -- the grade silently demoted an authoritative fact (GraphNodeWrite.GradeStated)
                              grade = CASE WHEN @gradeStated THEN @grade ELSE lyntai_memory_node.grade END,
                              signals = COALESCE(@signals, lyntai_memory_node.signals),
                              -- an ABSENT metadata bag is "no opinion" and keeps what is stored; a supplied
                              -- one REPLACES it (D91)
                              metadata = COALESCE(@metadata, lyntai_memory_node.metadata),
                              salience = CASE WHEN @signals IS NULL THEN lyntai_memory_node.salience ELSE @salience END,
                              difficulty = CASE WHEN @hasDifficultySignal THEN @difficulty ELSE lyntai_memory_node.difficulty END,
                              -- provenance_salience follows signals' own "empty incoming keeps what's
                              -- stored" rule exactly; provenance_retrievability is deliberately ABSENT from
                              -- this SET, same as stability itself: a plain re-remember never revisits either
                              provenance_salience = CASE WHEN @signals IS NULL THEN lyntai_memory_node.provenance_salience
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

        // UNCONFINED, matching either indexed column: this FTS table declares `headline, content`, and an
        // authored headline is a summary a caller wrote so the entry could be found by it — words that may
        // appear nowhere in the content. Never confine this to `content` to make the backends agree: the
        // portable guarantee is a MINIMUM, and the other two match headline too.
        var match = FtsQuery.Build(query);
        if (match is not null)
        {
            try
            {
                // bm25 leads here, unlike every other branch: everything in this result already matched the
                // query, so match quality decides first — salience is only a TIEBREAK between comparably
                // good matches. Letting salience outrank bm25 would let a salient POOR match displace a
                // strong one; rank contribution belongs to the engine's ranking policy, bounded there.
                var fts = new DynamicParameters(args);
                fts.Add("match", match);
                var hits = await SeedQueryAsync(conn, $"""
                    SELECT {MemoryGraphSql.NodeColumns}
                    FROM lyntai_memory_node_fts f JOIN lyntai_memory_node n ON n.id = f.rowid
                    WHERE f.lyntai_memory_node_fts MATCH @match
                      AND n.engine = @engine AND n.task_key = @taskKey
                      AND (@scope IS NULL OR n.scope = @scope)
                    ORDER BY bm25(f.lyntai_memory_node_fts), n.salience DESC, n.id DESC
                    LIMIT @limit
                    """, fts, totals.EncodedAt, ct).ConfigureAwait(false);

                // The FTS predicate cannot carry the grade carve-out and keep bm25 ordering, so exact facts
                // are fetched separately and merged. Only when the trigram index matched something: with no
                // match we must still fall through to LIKE, and returning exact facts here would skip it.
                if (hits.Count > 0)
                {
                    var exact = await SeedQueryAsync(conn, $"""
                        SELECT {MemoryGraphSql.NodeColumns}
                        FROM lyntai_memory_node n
                        WHERE n.engine = @engine AND n.task_key = @taskKey
                          AND (@scope IS NULL OR n.scope = @scope)
                          AND n.grade = @authoritative
                        ORDER BY n.salience DESC, n.last_recalled_position DESC, n.id DESC
                        LIMIT @limit
                        """, args, totals.EncodedAt, ct).ConfigureAwait(false);

                    return Merge(hits, exact, limit);
                }
                // no trigram hit → fall through to LIKE (covers punctuation-heavy and short queries)
            }
            catch (SqliteException ex)
            {
                _logger.LogWarning(ex, "graph FTS seed failed for {Engine}/{Task}; falling back to LIKE",
                    engine, taskKey);
            }
        }

        if (string.IsNullOrWhiteSpace(query))
            return await SeedQueryAsync(conn, MemoryGraphSql.SeedRecent, args, totals.EncodedAt, ct).ConfigureAwait(false);

        // Term-wise, via the same split the FTS path above uses — so falling back to LIKE degrades the
        // RANKING (a term count instead of bm25) without changing which entries are found.
        var kw = SearchTerms.LikeClause(query, ["n.content", "n.headline"]);
        var p = new DynamicParameters(args);
        foreach (var (name, value) in kw.Parameters) p.Add(name, value);
        return await SeedQueryAsync(conn, MemoryGraphSql.SeedMatching(kw), p, totals.EncodedAt, ct).ConfigureAwait(false);
    }

    /// <summary>Combine the query's matches with the scope's exact facts, without duplicating a node that is
    /// both.
    /// <para><b>Exact facts get RESERVED capacity, not an append.</b> <paramref name="matched"/> is itself
    /// <c>LIMIT</c>-bound, so appending exact facts after a full page of matches and truncating the tail
    /// would drop every one of them.</para>
    /// <para><b>Only exact facts the query did NOT match are ordered last, and the gradient is renormalized
    /// over the matches alone.</b> <see cref="GraphNode.Relevance"/> is a per-QUERY rank position, so each of
    /// the two queries arrives with its own 1.0-topped gradient; splicing them would report a fact that
    /// matched NOTHING as the best hit. A non-matching exact fact reports <b>0</b>, the contract's number on
    /// every backend (<see cref="IMemoryGraphStore.SeedAsync"/>).</para>
    /// <para><b>An exact fact that IS among the matches keeps the position bm25 earned it.</b> Re-appending it
    /// at the tail would hand the fact that directly answers the query the bottom of the gradient, which
    /// the engine multiplies into its rank — and can drop it from recall outright.</para></summary>
    private static IReadOnlyList<GraphNode> Merge(IReadOnlyList<GraphNode> matched,
        IReadOnlyList<GraphNode> exact, int limit)
    {
        var matchedIds = matched.Select(n => n.Id).ToHashSet();
        var unmatched = exact.Where(n => !matchedIds.Contains(n.Id)).ToList();
        var keepExact = Math.Min(unmatched.Count, limit);
        var kept = matched.Take(limit - keepExact).ToList();

        // Matched rides beside Relevance on both branches: this read DID ask, so it may answer (D97)
        return
        [
            .. kept.Select((node, i) => node with
            {
                Relevance = MemoryRelevance.ByRankPosition(i, kept.Count),
                Matched = true,
            }),
            .. unmatched.Take(keepExact).Select(node => node with
            {
                Relevance = MemoryRelevance.ByRankPosition(0, 1, matched: false),
                Matched = false,
            }),
        ];
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
        var idList = ids.ToList();
        // RAW weight and staleness — the engine applies the decay curve and re-ranks; ordering here is a
        // cheap pre-sort only. One MemoryEdgeRow rather than five more positional splits: Dapper's multi-map
        // arity is finite and a five-double signature is exactly the shape whose slots get silently transposed.
        var rows = await conn.QueryAsync<MemoryNodeRow, MemoryEdgeRow, GraphNeighbour>(
            new CommandDefinition($"""
                SELECT {MemoryGraphSql.NodeColumns},
                       CAST(x.w AS REAL) AS EdgeWeight,
                       CAST(@position - x.at AS REAL) AS EdgeAge,
                       CAST(@ordinal - x.ord AS REAL) AS EdgeOrdinalAge,
                       CAST(@chars - x.ch AS REAL) AS EdgeVolumeAge,
                       x.whn AS EdgeStrengthenedAt
                FROM (SELECT e.to_id AS id, MAX(e.weight) AS w, MAX(e.strengthened_position) AS at,
                             MAX(e.strengthened_ordinal) AS ord, MAX(e.strengthened_chars) AS ch,
                             MAX(e.strengthened_at) AS whn
                      FROM lyntai_memory_edge e
                      WHERE e.from_id IN @idList AND e.to_id NOT IN @idList
                      GROUP BY e.to_id) x
                JOIN lyntai_memory_node n ON n.id = x.id
                -- the walk stays inside the task: an edge that crosses one must not carry a
                -- recall across with it, or taskKey is a boundary for every read but this
                WHERE n.engine = @engine AND n.task_key = @taskKey
                ORDER BY x.w DESC, n.id DESC
                LIMIT @limit
                """, new { idList, engine, taskKey, limit, position = totals.Position, ordinal = totals.Ordinal, chars = totals.Chars },
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

    /// <summary>The whole write-back on ONE connection and ONE position-totals read, where the three
    /// separate calls paid three opens and two reads. Each part's SQL is the same statement its own member
    /// runs — the saving is the fixed costs, never the writes.</summary>
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
        // edges go with the nodes through ON DELETE CASCADE, as MemoryGraphSql.Prune says
        return await conn.ExecuteAsync(new CommandDefinition("""
            DELETE FROM lyntai_memory_node WHERE engine = @engine AND id IN @ids
            """, new { engine, ids }, cancellationToken: ct)).ConfigureAwait(false);
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
                r.ProvenanceRetrievability,
                // a nullable bool must reach SQLite as 1/0/NULL — the three states are load-bearing, so
                // this is written out rather than left to the mapper's default handling
                Verified = r.Verified is null ? (long?)null : r.Verified.Value ? 1L : 0L,
            }),
            cancellationToken: ct)).ConfigureAwait(false);

        // the in-process counter alone decides whether THIS batch trims (MemoryReviewLogPacing), so the trim —
        // the one operation with a real cost — runs roughly once every TrimInterval rows
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
        var rows = (await conn.QueryAsync<ReviewRow>(new CommandDefinition(MemoryGraphSql.Reviews,
            new { engine }, cancellationToken: ct)).ConfigureAwait(false)).AsList();
        return [.. rows.Select(r => r.ToReview(r.Verified is null ? null : r.Verified != 0))];
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
        // most-used first: a handle several facts already share is the one worth reusing, and it is what a
        // bounded list should spend its room on
        var rows = await conn.QueryAsync<string>(new CommandDefinition("""
            SELECT subject FROM lyntai_memory_subject
            WHERE engine = @engine AND task_key = @taskKey AND (@scope IS NULL OR scope = @scope)
            GROUP BY subject ORDER BY COUNT(*) DESC, subject LIMIT @limit
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
              AND (@scope IS NULL OR s.scope = @scope)
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
    // does not select it matched everything it returned (D97). MemoryRelevance.ByRankPosition is the ONE
    // gradient rule, shared with the Postgres twin and with Merge.
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

    /// <summary>This backend's half of a review row: the thirteen shared columns come from
    /// <see cref="MemoryReviewRow"/>, and only <see cref="Verified"/> is declared here — SQLite has no
    /// boolean, so the tri-state arrives as <c>1</c>/<c>0</c>/NULL and is typed <c>long?</c>. The distinction
    /// between "judged not relevant" and "never judged" is load-bearing (see
    /// <see cref="MemoryReviewWrite.Verified"/>).</summary>
    private sealed class ReviewRow : MemoryReviewRow
    {
        public long? Verified { get; set; }
    }
}
