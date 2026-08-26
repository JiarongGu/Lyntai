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
/// <para><b>Age is a subtraction, not a duration.</b> <c>lyntai_memory_position</c> holds a monotone position
/// per engine, so no date arithmetic appears in any query — which also avoids <c>julianday</c> returning NULL
/// on an unparseable timestamp and silently excluding every row. <see cref="GraphNode.OrdinalAge"/> and
/// <see cref="GraphNode.VolumeAge"/> are the same shape of subtraction against two more primitives the table
/// tracks unconditionally (design §5.7); <see cref="GraphNode.ElapsedAge"/> is the one exception, computed in
/// .NET for the identical reason — see <see cref="MemoryNodeRow.ToNode"/>.</para>
/// <para><b>The QUERIES here are this backend's own; the MATERIALIZATION is shared</b> with the Postgres
/// twin: every row type, the projection to <see cref="GraphNode"/> and the dialect-free statements live once
/// in <see cref="MemoryNodeRow"/> and <see cref="MemoryGraphSql"/> — a column↔property mismatch is a SILENT
/// null rather than an error, so the 25-property mapping gets ONE copy (<c>docs/DECISIONS.md</c> D77).</para>
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
    // Per-engine review-write counters (Task 3, MemoryReviewLogPacing) — in-process only, never persisted;
    // see that type's own remarks on why a process restart resetting this is an acceptable trade-off for a
    // SOFT cap. Concurrent because, unlike every other member here, this one is mutated OUTSIDE a
    // per-call connection/transaction and this store itself makes no single-writer guarantee.
    private readonly ConcurrentDictionary<string, long> _reviewCounters = new(StringComparer.Ordinal);

    // Alias every column explicitly: a name mismatch is a SILENT null, not an error. Degree/Strength are
    // plain aggregates, and Age/StrengthAge are plain subtractions against @position — the store applies no
    // decay; the policy does.
    //
    // OrdinalAge/VolumeAge are the SAME shape of subtraction, against the two new policy-independent
    // primitives (design doc §5.7) — @ordinal and @chars advance unconditionally on every write, regardless of
    // which IMemoryAgePolicy the engine has installed, so they can never be corrupted by swapping it.
    // EncodingAt is read RAW rather than diffed in SQL: this store deliberately does no date arithmetic (see
    // the type doc's remark on `julianday`), so ElapsedAge is computed in .NET, in MemoryNodeRow.ToNode.
    // ProvenanceRetrievability/ProvenanceSalience are plain integers (design doc §5.7, Task 4) — unlike
    // Age/OrdinalAge/VolumeAge above they are never computed here, so no affinity-trap CAST applies; they
    // are read exactly like RecallCount/Degree.
    // Difficulty IS a 0..1-shaped affinity trap like Stability, not
    // like the provenance ints above: SQLite can store 1.0 as an INTEGER and 0.5 as a REAL in the SAME
    // column, so it needs the same CAST(... AS REAL) Stability already gets.
    // StrengthOrdinalAge/StrengthVolumeAge are the strength-side counterparts of OrdinalAge/VolumeAge, and
    // StrengthenedAt is read RAW for exactly the reason EncodingAt is. Each takes its own
    // MAX rather than "the primitives of whichever edge has the max position": all four advance monotonically
    // together from one write's totals, so the freshest edge holds the maximum of every one of them. MAX over
    // no rows is NULL, which is why StrengthenedAt alone is nullable here — a node with no edges has no
    // strengthening to date, and MemoryNodeRow.ToNode reports 0 for it, matching the COALESCE the other two apply in SQL.
    private const string NodeColumns = """
        n.id AS Id, n.engine AS Engine, n.task_key AS TaskKey, n.scope AS Scope,
        n.headline AS Headline, n.content AS Content, n.grade AS Grade,
        n.created_at AS CreatedAt, n.recall_count AS RecallCount,
        CAST(n.stability AS REAL) AS Stability, n.metadata AS Metadata, n.signals AS Signals,
        CAST(n.difficulty AS REAL) AS Difficulty,
        CAST(@position - n.last_recalled_position AS REAL) AS Age,
        CAST(@ordinal - n.encoding_ordinal AS REAL) AS OrdinalAge,
        CAST(@chars - n.encoding_chars AS REAL) AS VolumeAge,
        n.encoding_at AS EncodingAt,
        n.provenance_retrievability AS ProvenanceRetrievability, n.provenance_salience AS ProvenanceSalience,
        (SELECT COUNT(*) FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS Degree,
        (SELECT CAST(COALESCE(SUM(e.weight), 0) AS REAL) FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS Strength,
        (SELECT CAST(COALESCE(@position - MAX(e.strengthened_position), 0) AS REAL)
         FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS StrengthAge,
        (SELECT CAST(COALESCE(@ordinal - MAX(e.strengthened_ordinal), 0) AS REAL)
         FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS StrengthOrdinalAge,
        (SELECT CAST(COALESCE(@chars - MAX(e.strengthened_chars), 0) AS REAL)
         FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS StrengthVolumeAge,
        (SELECT MAX(e.strengthened_at)
         FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS StrengthenedAt
        """;

    // The MAX() guard is load-bearing: a zero stability divides by zero, SQLite evaluates that to NULL, and
    // a NULL predicate excludes the row SILENTLY — losing a memory rather than erroring.
    private const string AgeOverStability =
        "(@position - n.last_recalled_position) / MAX(CAST(n.stability AS REAL), "
        + MemoryGraphSql.MinimumStability + ")";

    // Seeding applies NO faintness bound — decay buries by rank in the engine, never by excluding a row
    // here. This predicate is PruneAsync's alone, where removing a memory is the explicit intent.

    // Bound as a PARAMETER rather than written as a literal: MemoryGrade is Inherit=0, Associative=1,
    // Authoritative=2, so a hand-written "grade = 1" silently means the OPPOSITE of what it reads like.
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
        // all: an unguarded NaN does not merely mis-sort here — Microsoft.Data.Sqlite refuses to bind it and
        // fails the whole write.
        var salience = MemorySignals.Salience(write.Signals);
        // The LIVE difficulty column — promoted from the bag, but NOT on the trigger salience uses.
        // Difficulty has a SECOND writer salience does not (the retrievability policy, via TouchAsync), so
        // its own precedence must key on whether THIS bag actually carries a difficulty signal, not on
        // whether it carries anything at all. See MemoryDecayState.Difficulty's own remarks for the full rule.
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
            ON CONFLICT(engine) DO UPDATE SET
                position = position + @advance, ordinal = ordinal + 1,
                chars = chars + @contentLength, encoded_at = @now
            RETURNING position AS Position, ordinal AS Ordinal, chars AS Chars, encoded_at AS EncodedAt
            """, new
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
                              -- metadata answers the same question as signals and now the same
                              -- way: an ABSENT bag is "no opinion" and keeps what is stored, a
                              -- supplied one REPLACES it. Absent from this SET until D91, which
                              -- silently ignored every correction a caller made.
                              metadata = COALESCE(@metadata, lyntai_memory_node.metadata),
                              salience = CASE WHEN @signals IS NULL THEN lyntai_memory_node.salience ELSE @salience END,
                              -- difficulty overwrites ONLY when THIS write's bag actually names a difficulty
                              -- signal - unlike salience, "the bag is merely non-empty" is
                              -- NOT the trigger: a write that judged something else entirely (salience,
                              -- say) must never reset what Reinforce has since tracked.
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
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var totals = await TotalsAsync(conn, engine, ct).ConfigureAwait(false);
        var position = totals.Position;
        var ordinal = totals.Ordinal;
        var chars = totals.Chars;

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
                // good matches, deliberately weaker than the non-matching branches below (which lead with
                // salience because they have no match quality to rank by at all). Letting salience outrank
                // bm25 here would let a salient POOR match displace a strong one, which is the distortion
                // Task 6's engine-side bound exists to cap — the store must not reproduce it unbounded.
                var hits = await QueryAsync(conn, $"""
                    SELECT {NodeColumns}
                    FROM lyntai_memory_node_fts f JOIN lyntai_memory_node n ON n.id = f.rowid
                    WHERE f.lyntai_memory_node_fts MATCH @match
                      AND n.engine = @engine AND n.task_key = @taskKey
                      AND (@scope IS NULL OR n.scope = @scope)
                    ORDER BY bm25(f.lyntai_memory_node_fts), n.salience DESC, n.id DESC
                    LIMIT @limit
                    """, new { match, engine, taskKey, scope, position, ordinal, chars, limit, authoritative = Authoritative },
                    totals.EncodedAt, ct).ConfigureAwait(false);

                // The FTS predicate cannot carry the grade carve-out and keep bm25 ordering, so exact facts
                // are fetched separately and merged. Only when the trigram index matched something: with no
                // match we must still fall through to LIKE, and returning exact facts here would skip it.
                if (hits.Count > 0)
                {
                    var exact = await QueryAsync(conn, $"""
                        SELECT {NodeColumns}
                        FROM lyntai_memory_node n
                        WHERE n.engine = @engine AND n.task_key = @taskKey
                          AND (@scope IS NULL OR n.scope = @scope)
                          AND n.grade = @authoritative
                        ORDER BY n.salience DESC, n.last_recalled_position DESC, n.id DESC
                        LIMIT @limit
                        """,
                        new { engine, taskKey, scope, position, ordinal, chars, limit, authoritative = Authoritative },
                        totals.EncodedAt, ct).ConfigureAwait(false);

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

        // Grade leads both orderings below, ahead of recency: the WHERE carve-out only keeps an exact fact
        // out of the filter, it says nothing about the LIMIT after it. A long-quiet exact fact has the
        // LOWEST last_recalled_position in its scope, so a recency-only order sorts it last and the LIMIT
        // cuts it before this method ever returns it.
        if (!string.IsNullOrWhiteSpace(query))
        {
            // Term-wise, via the same split the FTS path above uses — so falling back to LIKE degrades the
            // RANKING (a term count instead of bm25) without changing which entries are found.
            var kw = SearchTerms.LikeClause(query, ["n.content", "n.headline"]);
            var p = new DynamicParameters(new
            {
                engine, taskKey, scope, position, ordinal, chars, limit, authoritative = Authoritative,
            });
            foreach (var (name, value) in kw.Parameters) p.Add(name, value);

            // `Matched` is selected ONLY here, and it is what stops this path reporting a grade-admitted
            // non-match at the HEAD of the gradient — which is where the grade-first ORDER BY puts it.
            // MemoryNodeRow.Matched is nullable so every other query, which does not select it, keeps the plain
            // gradient rather than silently zeroing every row.
            return await QueryAsync(conn, $"""
                SELECT {NodeColumns},
                       {kw.Predicate} AS Matched
                FROM lyntai_memory_node n
                WHERE n.engine = @engine AND n.task_key = @taskKey
                  AND (@scope IS NULL OR n.scope = @scope)
                  AND (n.grade = @authoritative OR {kw.Predicate})
                ORDER BY (n.grade = @authoritative) DESC, {kw.MatchCount} DESC,
                         n.salience DESC, n.last_recalled_position DESC, n.id DESC
                LIMIT @limit
                """, p, totals.EncodedAt, ct).ConfigureAwait(false);
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

    /// <summary>Combine the query's matches with the scope's exact facts, without duplicating a node that is
    /// both.
    /// <para><b>Exact facts get RESERVED capacity, not an append.</b> <paramref name="matched"/> is itself
    /// <c>LIMIT</c>-bound, so appending exact facts after a full page of matches and truncating the tail
    /// would drop every one of them.</para>
    /// <para><b>Only exact facts the query did NOT match are ordered last, then the whole list is
    /// renormalized.</b> <see cref="GraphNode.Relevance"/> is a per-QUERY rank position, so the two
    /// independent queries each arrive with their own 1.0-topped gradient; splicing them would report a fact
    /// that matched NOTHING as the best hit for the query. Renormalizing over the merged order gives exact
    /// non-matches the tail of the gradient, which is honest — they were admitted by GRADE, not by
    /// matching.</para>
    /// <para><b>An exact fact that IS among the matches keeps the position bm25 earned it</b>, and is never
    /// re-appended at the tail. Demoting it would hand the fact that directly answers the query the bottom of
    /// the gradient, and <c>GraphMemoryEngine.RecallAsync</c> multiplies that
    /// <see cref="GraphNode.Relevance"/> into its rank before taking the top <c>limit</c> — so the demotion
    /// can drop it from recall outright, which is strictly worse than not merging at all.</para>
    /// <para>WHERE an admitted-but-non-matching exact fact lands in this gradient is SQLite's own answer; the
    /// <see cref="GraphNode.Relevance"/> it reports for one — <b>0</b> — is the contract's, on every backend
    /// (<see cref="IMemoryGraphStore.SeedAsync"/>). Admission never rides on that number.</para></summary>
    private static IReadOnlyList<GraphNode> Merge(IReadOnlyList<GraphNode> matched,
        IReadOnlyList<GraphNode> exact, int limit)
    {
        if (limit <= 0) return [];

        // only genuine NON-matches are tail-ordered; a row in both batches is already carrying its bm25
        // position, and stripping it out of `matched` to re-append it last is a demotion, not a merge
        var matchedIds = matched.Select(n => n.Id).ToHashSet();
        var unmatched = exact.Where(n => !matchedIds.Contains(n.Id)).ToList();
        var keepExact = Math.Min(unmatched.Count, limit);

        var kept = matched.Take(limit - keepExact).ToList();

        // The ORDER is unchanged — matches lead, exact non-matches take the low end, so the limit still cuts
        // the weakest match rather than an exact fact. Only the reported number changed: the gradient is
        // normalized over the MATCHES alone (they are what was ranked), and a non-matching exact fact reports
        // 0 rather than borrowing the tail of a gradient it never competed in.
        return
        [
            // renormalized over the MATCHES alone — they are what was ranked — through the same shared rule
            // QueryAsync uses, so the two paths cannot report the same row differently
            .. kept.Select((node, i) => node with { Relevance = MemoryRelevance.ByRankPosition(i, kept.Count) }),
            .. unmatched.Take(keepExact).Select(node => node with
            {
                Relevance = MemoryRelevance.ByRankPosition(0, 1, matched: false),
            }),
        ];
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
        var idList = ids.ToList();
        // RAW weight and staleness — the engine applies the decay curve and re-ranks; ordering here is a
        // cheap pre-sort only
        // One MemoryEdgeRow rather than five more positional splits: Dapper's multi-map arity is finite and a
        // five-double signature is exactly the shape whose slots get silently transposed.
        var rows = await conn.QueryAsync<MemoryNodeRow, MemoryEdgeRow, GraphNeighbour>(
            new CommandDefinition($"""
                SELECT {NodeColumns},
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
                WHERE n.engine = @engine
                ORDER BY x.w DESC, n.id DESC
                LIMIT @limit
                """, new { idList, engine, limit, position, ordinal = totals.Ordinal, chars = totals.Chars },
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
                recall_count = recall_count + 1,
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

    // Stamps all FOUR strengthening marks together — the Advance-driven position and the three
    // policy-independent primitives beside it, from one totals snapshot, so they can never disagree about
    // when this edge was last strengthened.
    private static Task StrengthenAsync(IDbConnection conn, long from, long to, string kind, double weight,
        MemoryPositionRow totals, CancellationToken ct) =>
        conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO lyntai_memory_edge (from_id, to_id, kind, weight, strengthened_position,
                strengthened_ordinal, strengthened_chars, strengthened_at)
            VALUES (@from, @to, @kind, @weight, @position, @ordinal, @chars, @at)
            ON CONFLICT(from_id, to_id, kind)
                DO UPDATE SET weight = weight + @weight, strengthened_position = @position,
                    strengthened_ordinal = @ordinal, strengthened_chars = @chars, strengthened_at = @at
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
        // Shared with the Postgres twin apart from the zero-stability guard's spelling — edges go with the
        // nodes through ON DELETE CASCADE, which here only fires because the factory sets foreign_keys=ON
        // per connection.
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
        // edges go with the nodes through ON DELETE CASCADE, same as PruneAsync above
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

        var effectiveCap = Math.Max(0, cap);
        var now = _clock();
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(MemoryGraphSql.InsertReview,
            reviews.Select(r => new
            {
                engine, now, r.NodeId, batchId = r.BatchId.ToString(), r.PreAge, r.PreStability,
                r.PreDifficulty, r.PreStrength, r.PreStrengthAge, r.Grade, r.PostStability, r.PostDifficulty,
                r.ProvenanceRetrievability,
                // a nullable bool must reach SQLite as 1/0/NULL — the three states are load-bearing, so
                // this is written out rather than left to the mapper's default handling
                Verified = r.Verified is null ? (long?)null : r.Verified.Value ? 1L : 0L,
            }),
            cancellationToken: ct)).ConfigureAwait(false);

        // MemoryReviewLogPacing (Task 3): the in-process counter alone decides whether THIS batch trims, so
        // the trim statement below — the one operation with a real cost — runs roughly once every
        // TrimInterval rows, never on every write. See that type's own remarks for the strategy and its
        // trade-off (a soft cap, paced from a counter this store never persists).
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
            SELECT id AS Id, engine AS Engine, node_id AS NodeId, batch_id AS BatchId,
                   created_at AS CreatedAt,
                   CAST(pre_age AS REAL) AS PreAge, CAST(pre_stability AS REAL) AS PreStability,
                   CAST(pre_difficulty AS REAL) AS PreDifficulty, CAST(pre_strength AS REAL) AS PreStrength,
                   CAST(pre_strength_age AS REAL) AS PreStrengthAge, CAST(grade AS REAL) AS Grade,
                   CAST(post_stability AS REAL) AS PostStability,
                   CAST(post_difficulty AS REAL) AS PostDifficulty,
                   provenance_retrievability AS ProvenanceRetrievability,
                   verified AS Verified
            FROM lyntai_memory_review WHERE engine = @engine ORDER BY id
            """, new { engine }, cancellationToken: ct)).ConfigureAwait(false)).AsList();
        // The 1/0/NULL -> tri-state conversion is this backend's alone; the other thirteen columns project
        // through the shared row.
        return [.. rows.Select(r => r.ToReview(r.Verified is null ? null : r.Verified != 0))];
    }

    /// <inheritdoc />
    public async Task RecordSubjectsAsync(string engine, long nodeId, IReadOnlyCollection<string> subjects,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        ct.ThrowIfCancellationRequested();
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);

        // REPLACE, never accumulate: a stale subject from an earlier annotation keeps linking future facts
        // into the wrong cluster, and nothing would ever surface it. The delete runs even when the new set is
        // empty, which is how an annotator that changes its mind to "no opinion" actually clears them.
        await conn.ExecuteAsync(new CommandDefinition(MemoryGraphSql.DeleteSubjects,
            new { engine, nodeId }, cancellationToken: ct)).ConfigureAwait(false);

        // MemorySubject.Canonicalize is the ONE normalization, shared with the other two backends and with
        // any BYO store: a private copy here is how the same annotation ends up linking a different cluster
        // depending on which backend stored it.
        var rows = MemorySubject.Canonicalize(subjects);
        if (rows.Count == 0) return;

        // task_key/scope are denormalized from the node so the lookup needs no join — it is on the write
        // path of every annotated remember, and the node's own task/scope never change after insert.
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT OR IGNORE INTO lyntai_memory_subject (engine, node_id, task_key, scope, subject)
            SELECT @engine, n.id, n.task_key, n.scope, @subject
            FROM lyntai_memory_node n WHERE n.id = @nodeId AND n.engine = @engine
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

    /// <summary>Where the engine currently stands, on every scale a store tracks: the legacy,
    /// <c>Advance</c>-driven <see cref="MemoryPositionRow.Position"/>, and the three primitives that advance
    /// unconditionally. All default to their zero value for an engine nothing has been written to — which is
    /// correct: nothing has happened in it, so nothing has aged.</summary>
    private static async Task<MemoryPositionRow> TotalsAsync(IDbConnection conn, string engine, CancellationToken ct) =>
        await conn.QuerySingleOrDefaultAsync<MemoryPositionRow>(new CommandDefinition("""
            SELECT CAST(position AS REAL) AS Position, ordinal AS Ordinal, chars AS Chars, encoded_at AS EncodedAt
            FROM lyntai_memory_position WHERE engine = @engine
            """, new { engine }, cancellationToken: ct)).ConfigureAwait(false)
        ?? new MemoryPositionRow { EncodedAt = DateTimeOffset.UnixEpoch };

    private static async Task<IReadOnlyList<GraphNode>> QueryAsync(IDbConnection conn, string sql,
        object parameters, DateTimeOffset encodedAt, CancellationToken ct)
    {
        var rows = (await conn.QueryAsync<MemoryNodeRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct)).ConfigureAwait(false)).AsList();
        // MemoryRelevance.ByRankPosition is the ONE rule, shared with the Postgres twin and with Merge below:
        // this backend's own rank position, normalized, with the grade-admitted non-match reporting 0.
        // Only the substring-fallback query selects `Matched`; everywhere else it is null ("not asked") and
        // the plain gradient applies.
        return [.. rows.Select((row, i) =>
            row.ToNode(encodedAt) with { Relevance = MemoryRelevance.ByRankPosition(i, rows.Count, row.Matched) })];
    }

    /// <summary>This backend's half of a review row: the thirteen shared columns come from
    /// <see cref="MemoryReviewRow"/>, and only <see cref="Verified"/> is declared here.
    /// <para>SQLite has no boolean, so the tri-state arrives as <c>1</c>/<c>0</c>/NULL and is typed
    /// <c>long?</c> — the Postgres twin binds a native <c>bool?</c> for the same column. That is a real
    /// dialect difference rather than an accident, which is why the shared row declines to declare it: the
    /// distinction between "judged not relevant" and "never judged" is load-bearing (see
    /// <see cref="MemoryReviewWrite.Verified"/>) and a bind that erased it would be silent.</para></summary>
    private sealed class ReviewRow : MemoryReviewRow
    {
        public long? Verified { get; set; }
    }
}
