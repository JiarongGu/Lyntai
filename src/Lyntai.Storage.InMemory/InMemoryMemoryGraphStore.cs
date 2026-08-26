using Lyntai.Memory;

namespace Lyntai.Storage.InMemory;

/// <summary>In-process <see cref="IMemoryGraphStore"/> — the zero-dependency default, and the reference
/// implementation the SQL backends are held to by the shared store contract.
/// <para>Keeps a monotone POSITION per engine, advanced by each write's
/// <see cref="GraphNodeWrite.Advance"/>. An entry's age is how far that position has moved since it was
/// last used — a subtraction, never a duration. Wall-clock timestamps are recorded but feed only
/// <see cref="PruneAsync"/>'s <c>olderThan</c> and auditing.</para>
/// <para>Recall matches any TERM of the query, case-insensitively, using the shared
/// <see cref="SearchTerms"/> split — so a multi-word cue finds the same entries here as on SQLite and
/// Postgres. It then orders by GRADE first, then by recency: authoritative material is admitted
/// unconditionally, and a recency-led ordering would let the candidate limit cut the quietest exact fact
/// before the engine ranked anything. What stays backend-specific is the RANKING among matches — this store
/// has none to give, where SQLite has bm25.</para>
/// <para>This store has no relevance SCORE to normalize, so it reports <see cref="GraphNode.Relevance"/>
/// <c>1</c> for anything the query matched and <c>0</c> for a node admitted by the grade carve-out that the
/// query did NOT match (see <see cref="IMemoryGraphStore.SeedAsync"/>). A flat <c>1</c> is what a query-less
/// enumeration reports, where everything matches by definition — never the unconditional answer.</para></summary>
/// <param name="clock">Time source for the audit timestamps; null takes the system clock.</param>
public sealed class InMemoryMemoryGraphStore(Func<DateTimeOffset>? clock = null) : IMemoryGraphStore
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Lock _lock = new();
    private readonly Dictionary<long, Row> _nodes = [];
    private readonly Dictionary<(long From, long To, string Kind), Edge> _edges = [];

    /// <summary>What each node is ABOUT — steers linking, never recall. A list rather than a dictionary
    /// because both directions are needed (all subjects of a node when replacing, all nodes of a subject when
    /// linking) and the volumes here are a test/in-process fixture's, not a database's.</summary>
    private readonly List<(string Engine, long NodeId, string TaskKey, string Scope, string Subject)> _subjects = [];
    private readonly Dictionary<string, EngineTotals> _totals = new(StringComparer.Ordinal);
    private readonly Dictionary<long, ReviewRow> _reviews = [];
    // Per-engine review-write counters (Task 3, MemoryReviewLogPacing) — in-process only, protected by the
    // same _lock as every other mutable field here, never persisted: see that type's own remarks on why a
    // process restart resetting this is an acceptable trade-off for a SOFT cap.
    private readonly Dictionary<string, long> _reviewCounters = new(StringComparer.Ordinal);
    private long _next = 1;
    private long _nextReview = 1;

    /// <summary>A stored node. <c>LastRecalledPosition</c> is where the engine's position stood when this
    /// was last used; the age reported to callers is the difference from where it stands now.
    /// <para>The three <c>Encoding*</c> fields are the POLICY-INDEPENDENT primitives (design doc §5.7):
    /// unlike <c>LastRecalledPosition</c>, which reflects whatever <see cref="GraphNodeWrite.Advance"/> the
    /// caller's chosen <c>IMemoryAgePolicy</c> computed, these three are stamped by THIS store, always the
    /// same way, regardless of that policy — so they can never be reinterpreted by swapping it.</para>
    /// <para><c>ProvenanceRetrievability</c>/<c>ProvenanceSalience</c> are design §5.7's provenance columns
    /// (Task 4): which policy computed <c>Stability</c>, and which salience polic(ies) produced <c>Signals</c>.
    /// Neither is touched by a plain re-remember of identical content — the first mirrors
    /// <c>Stability</c> itself (only set at first write, then only by <see cref="TouchAsync"/>); the second
    /// mirrors <c>Signals</c>' own "empty incoming keeps what's stored" rule.</para>
    /// <para><c>Difficulty</c> is LIVE state a retrievability policy
    /// both reads and writes, the same way <c>Stability</c> is — seeded/refreshed from the incoming signals
    /// bag on any NON-EMPTY write (mirroring how the SQL backends promote <c>salience</c>), and otherwise
    /// updated only by <see cref="TouchAsync"/>. See <see cref="MemoryDecayState.Difficulty"/>'s own remarks
    /// for the full precedence rule.</para></summary>
    private sealed record Row(
        long Id, string Engine, string TaskKey, string Scope, string Headline, string Content,
        MemoryGrade Grade, DateTimeOffset CreatedAt, int RecallCount, double Stability,
        double LastRecalledPosition, IReadOnlyDictionary<string, string>? Metadata,
        MemorySignals Signals, long EncodingOrdinal, long EncodingChars, DateTimeOffset EncodingAt,
        long ProvenanceRetrievability, long ProvenanceSalience, double Difficulty = 5);

    /// <summary>A stored edge. The weight only ever grows; decay is applied at read time by whoever owns the
    /// curve, so this store holds no curve constant. <c>StrengthenedPosition</c> is the store's own <c>Advance</c>-driven view;
    /// <c>StrengthenedOrdinal</c>/<c>StrengthenedChars</c>/<c>StrengthenedAt</c> are the policy-independent
    /// primitives beside it — the strength-side counterparts of a node's own encoding primitives, so a
    /// <c>Derivable</c> age policy can project connection age in its own unit after a policy swap.</summary>
    private readonly record struct Edge(double Weight, double StrengthenedPosition,
        long StrengthenedOrdinal, long StrengthenedChars, DateTimeOffset StrengthenedAt);

    /// <summary>A logged reinforcement (Task 3) — <see cref="MemoryReviewWrite"/> plus this store's own
    /// assigned id and timestamp, matching <see cref="MemoryReview"/>'s own shape.</summary>
    private sealed record ReviewRow(
        long Id, string Engine, long NodeId, Guid BatchId, DateTimeOffset CreatedAt, double PreAge,
        double PreStability, double PreDifficulty, double PreStrength, double PreStrengthAge, double? Grade,
        double PostStability, double PostDifficulty, long ProvenanceRetrievability, bool? Verified);

    /// <summary>One engine's current state on every scale a store tracks: the legacy, <c>Advance</c>-driven
    /// <see cref="Position"/>, and the three primitives that advance unconditionally — <see cref="Ordinal"/>
    /// by one, <see cref="Chars"/> by the write's own content length, <see cref="EncodedAt"/> to the write's
    /// own timestamp. Frozen between writes: a recall never advances any of the four.</summary>
    private readonly record struct EngineTotals(double Position, long Ordinal, long Chars, DateTimeOffset EncodedAt);

    private EngineTotals Totals(string engine) =>
        _totals.TryGetValue(engine, out var t) ? t : default;

    /// <inheritdoc />
    public Task<long> UpsertAsync(GraphNodeWrite write, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            var now = _clock();
            // the write crowds everything already stored: advance FIRST, so the new entry's own age is
            // zero relative to the position/primitives it is stamped with. The three new primitives advance
            // unconditionally — never from write.Advance, which is this write's chosen IMemoryAgePolicy's
            // business, not the store's.
            var prior = Totals(write.Engine);
            var totals = new EngineTotals(
                prior.Position + Math.Max(0, write.Advance), prior.Ordinal + 1,
                prior.Chars + write.Content.Length, now);
            _totals[write.Engine] = totals;

            var existing = _nodes.Values.FirstOrDefault(n =>
                n.Engine == write.Engine && n.TaskKey == write.TaskKey && n.Scope == write.Scope &&
                n.Content == write.Content);
            if (existing is not null)
            {
                // identical content REFRESHES, matching IMemoryStore and ISemanticMemory
                _nodes[existing.Id] = existing with
                {
                    LastRecalledPosition = totals.Position,
                    // only a CALLER-NAMED grade overwrites — MemoryGrade.Inherit resolves to the engine's
                    // role, so without this a re-remember that did not restate the grade silently demoted
                    // an authoritative fact. See GraphNodeWrite.GradeStated.
                    Grade = write.GradeStated ? write.Grade : existing.Grade,
                    Headline = write.Headline,
                    // an EMPTY incoming bag keeps what is stored — see GraphNodeWrite.Signals for why
                    // blanking here would erase a judgement on the very write meant to reinforce it
                    Signals = write.Signals.Count == 0 ? existing.Signals : write.Signals,
                    // provenance_salience follows Signals' own rule exactly: an empty incoming bag means no
                    // policy produced anything THIS time, so the existing attribution is left alone rather
                    // than blanked. provenance_retrievability is NOT updated here at all (see below) —
                    // Stability itself isn't either, on a plain re-remember.
                    ProvenanceSalience = write.Signals.Count == 0
                        ? existing.ProvenanceSalience
                        : write.ProvenanceSalience,
                    // Difficulty does NOT follow Signals'/ProvenanceSalience's "bag is non-empty" rule: it
                    // has a SECOND writer salience does not — the retrievability policy, via TouchAsync — so
                    // it overwrites only when THIS bag actually names a difficulty signal, and is otherwise
                    // left exactly as Reinforce last set it. See MemoryDecayState.Difficulty's own remarks.
                    Difficulty = write.Signals.Values.ContainsKey(MemorySignals.WellKnown.Difficulty)
                        ? MemorySignals.Difficulty(write.Signals)
                        : existing.Difficulty,
                    EncodingOrdinal = totals.Ordinal, EncodingChars = totals.Chars, EncodingAt = totals.EncodedAt,
                };
                return Task.FromResult(existing.Id);
            }

            var id = _next++;
            _nodes[id] = new Row(id, write.Engine, write.TaskKey, write.Scope, write.Headline,
                write.Content, write.Grade, now, 0, write.InitialStability, totals.Position, write.Metadata,
                write.Signals, totals.Ordinal, totals.Chars, totals.EncodedAt,
                write.ProvenanceRetrievability, write.ProvenanceSalience,
                MemorySignals.Difficulty(write.Signals));
            return Task.FromResult(id);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphNode>> SeedAsync(string engine, string taskKey, string? scope,
        string? query, int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            var totals = Totals(engine);
            // Term-wise, via the shared split — the same terms the SQL backends match on, so a multi-word
            // cue finds the same entries here as it does there.
            var terms = SearchTerms.SubstringTerms(query);
            var hits = _nodes.Values
                .Where(n => n.Engine == engine && n.TaskKey == taskKey)
                .Where(n => scope is null || n.Scope == scope)
                // authoritative material is admitted unconditionally — the query may not exclude an exact
                // fact. FAINTNESS excludes nothing at all: decay buries by rank in the engine, never here.
                .Where(n => n.Grade == MemoryGrade.Authoritative || Matches(n, query, terms))
                // exact facts first: a long-quiet one has the lowest position and a plain recency order
                // would let the limit cut it before the engine ever ranks it. Salience is next: a salient
                // ASSOCIATIVE entry has no grade carve-out to lean on and needs the same protection from the
                // limit — decay resistance AND admission priority.
                // Through MemorySignals.Salience, not the raw bag: this store holds the bag VERBATIM where
                // the SQL backends coerce it into their promoted column, so reading it raw here would order
                // the same data differently on different backends — a bag holding 0.5 below every
                // unjudged row, and a NaN dead last (Comparer<double> ranks NaN under everything).
                .OrderByDescending(n => n.Grade == MemoryGrade.Authoritative)
                .ThenByDescending(n => MemorySignals.Salience(n.Signals))
                .ThenByDescending(n => n.LastRecalledPosition)
                .ThenByDescending(n => n.Id) // unique tiebreaker: ties must not wobble
                .Take(limit)
                // Relevance is "how well it matched the QUERY", so a node admitted by GRADE that the query
                // never matched reports 0 — see IMemoryGraphStore.SeedAsync. With no query, Matches is true
                // for everything and this store's flat 1 is unchanged.
                .Select(n => ToNode(n, totals) with { Relevance = Matches(n, query, terms) ? 1 : 0 })
                .ToList();
            return Task.FromResult<IReadOnlyList<GraphNode>>(hits);
        }

        // A node matches when it carries ANY term. `terms` is empty when the query was too short to yield
        // one (a two-character CJK word, "ab"), and then the whole query is matched as a substring — the
        // same fallback the SQL backends take, and the behaviour a short query always had.
        // HEADLINE as well as content: an authored headline is a summary the caller wrote so the entry could
        // be found by it, and every backend matches both — SQLite through its FTS mirror, Postgres through
        // the headline trigram index.
        static bool Matches(Row node, string? query, IReadOnlyList<string> terms) =>
            string.IsNullOrWhiteSpace(query) ||
            (terms.Count == 0
                ? node.Content.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)
                  || node.Headline.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)
                : terms.Any(t => node.Content.Contains(t, StringComparison.OrdinalIgnoreCase)
                              || node.Headline.Contains(t, StringComparison.OrdinalIgnoreCase)));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphNeighbour>> NeighboursAsync(string engine,
        IReadOnlyCollection<long> ids, int limit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            var totals = Totals(engine);
            var position = totals.Position;
            var frontier = ids.ToHashSet();
            var hits = _edges
                .Where(e => frontier.Contains(e.Key.From) && !frontier.Contains(e.Key.To))
                .GroupBy(e => e.Key.To)
                // the strongest edge reaching this neighbour, and how stale it is; RAW weight only — the
                // engine applies the decay curve and re-ranks
                // Each mark takes its own MAX, for the same reason ToNode's do: all four advance monotonically
                // together from one write's totals, so the freshest edge holds the maximum of every one.
                .Select(g => (Id: g.Key,
                    Weight: g.Max(e => e.Value.Weight),
                    At: g.Max(e => e.Value.StrengthenedPosition),
                    Ordinal: g.Max(e => e.Value.StrengthenedOrdinal),
                    Chars: g.Max(e => e.Value.StrengthenedChars),
                    When: g.Max(e => e.Value.StrengthenedAt)))
                .OrderByDescending(x => x.Weight)
                .ThenByDescending(x => x.Id) // unique tiebreaker
                .Where(x => _nodes.TryGetValue(x.Id, out var n) && n.Engine == engine)
                .Take(limit)
                .Select(x => new GraphNeighbour(ToNode(_nodes[x.Id], totals), x.Weight, position - x.At,
                    EdgeOrdinalAge: totals.Ordinal - x.Ordinal,
                    EdgeVolumeAge: totals.Chars - x.Chars,
                    EdgeElapsedAge: (totals.EncodedAt - x.When).TotalDays))
                .ToList();
            return Task.FromResult<IReadOnlyList<GraphNeighbour>>(hits);
        }
    }

    /// <inheritdoc />
    public Task<GraphNode?> GetAsync(string engine, long id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
            return Task.FromResult(_nodes.TryGetValue(id, out var node) && node.Engine == engine
                ? ToNode(node, Totals(engine))
                : null);
    }

    /// <inheritdoc />
    public Task TouchAsync(string engine, IReadOnlyCollection<GraphTouch> touches,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(touches);
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            // a recall does NOT advance the position or any of the three primitives — it stamps the touched
            // node's own snapshot to wherever the engine already is, on every scale at once
            var totals = Totals(engine);
            foreach (var touch in touches)
                if (_nodes.TryGetValue(touch.Id, out var node) && node.Engine == engine)
                    _nodes[touch.Id] = node with
                    {
                        LastRecalledPosition = totals.Position,
                        Stability = touch.Stability,
                        Difficulty = touch.Difficulty,
                        ProvenanceRetrievability = touch.ProvenanceRetrievability,
                        RecallCount = node.RecallCount + 1,
                        EncodingOrdinal = totals.Ordinal, EncodingChars = totals.Chars, EncodingAt = totals.EncodedAt,
                    };
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task LinkAsync(string engine, long from, long to, string? kind, double weight, bool symmetric,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (from == to) return Task.CompletedTask; // a self-edge is never useful and would skew Degree
        lock (_lock)
        {
            var totals = Totals(engine);
            Strengthen(from, to, totals);
            if (symmetric) Strengthen(to, from, totals);
        }
        return Task.CompletedTask;

        void Strengthen(long a, long b, EngineTotals totals)
        {
            var key = (a, b, kind ?? "");
            var existing = _edges.TryGetValue(key, out var edge) ? edge.Weight : 0;
            _edges[key] = new Edge(existing + weight, totals.Position,
                totals.Ordinal, totals.Chars, totals.EncodedAt);
        }
    }

    /// <inheritdoc />
    public Task<int> PruneAsync(string engine, string taskKey, string? scope,
        double? maxAgeOverStability, TimeSpan? olderThan, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            var position = Totals(engine).Position;
            var createdBefore = olderThan is null ? (DateTimeOffset?)null : _clock() - olderThan.Value;
            var doomed = _nodes.Values
                .Where(n => n.Engine == engine && n.TaskKey == taskKey)
                .Where(n => scope is null || n.Scope == scope)
                // never eligible: an authoritative node's retrievability is fixed at 1, so this falls out
                // of the formula rather than needing a special case
                .Where(n => n.Grade != MemoryGrade.Authoritative)
                .Where(n =>
                    (maxAgeOverStability is double c &&
                     (position - n.LastRecalledPosition) /
                        Math.Max(n.Stability, Lyntai.Storage.MemoryGraphSql.MinimumStabilityValue) > c) ||
                    (createdBefore is DateTimeOffset before && n.CreatedAt < before))
                .Select(n => n.Id)
                .ToList();
            foreach (var id in doomed) Remove(id);
            return Task.FromResult(doomed.Count);
        }
    }

    /// <inheritdoc />
    public Task<int> DeleteAsync(string engine, IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ct.ThrowIfCancellationRequested();
        if (ids.Count == 0) return Task.FromResult(0);
        lock (_lock)
        {
            // scoped to the named engine, the same as every other member here — an id a caller read from a
            // DIFFERENT engine must not be removable through this one
            var removed = 0;
            foreach (var id in ids)
                if (_nodes.TryGetValue(id, out var row) && row.Engine == engine)
                {
                    Remove(id);
                    removed++;
                }
            return Task.FromResult(removed);
        }
    }

    /// <inheritdoc />
    public Task ForgetAsync(string engine, string taskKey, string? scope, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            var doomed = _nodes.Values
                .Where(n => n.Engine == engine && n.TaskKey == taskKey)
                .Where(n => scope is null || n.Scope == scope)
                .Select(n => n.Id)
                .ToList();
            foreach (var id in doomed) Remove(id);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RecordReviewsAsync(string engine, IReadOnlyCollection<MemoryReviewWrite> reviews, int cap,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        ct.ThrowIfCancellationRequested();
        if (reviews.Count == 0) return Task.CompletedTask;

        var effectiveCap = Math.Max(0, cap);
        lock (_lock)
        {
            var now = _clock();
            foreach (var r in reviews)
            {
                var id = _nextReview++;
                _reviews[id] = new ReviewRow(id, engine, r.NodeId, r.BatchId, now, r.PreAge, r.PreStability,
                    r.PreDifficulty, r.PreStrength, r.PreStrengthAge, r.Grade, r.PostStability,
                    r.PostDifficulty, r.ProvenanceRetrievability, r.Verified);
            }

            // MemoryReviewLogPacing (Task 3): decide from the in-process counter alone, never a per-write
            // DELETE — see that type's own remarks for the strategy and its trade-off.
            var before = _reviewCounters.TryGetValue(engine, out var count) ? count : 0;
            _reviewCounters[engine] = before + reviews.Count;
            if (MemoryReviewLogPacing.CrossesBoundary(before, reviews.Count, effectiveCap))
            {
                var doomed = _reviews.Values
                    .Where(r => r.Engine == engine)
                    .OrderByDescending(r => r.Id)
                    .Skip(effectiveCap)
                    .Select(r => r.Id)
                    .ToList();
                foreach (var id in doomed) _reviews.Remove(id);
            }
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MemoryReview>> ReviewsAsync(string engine, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            var rows = _reviews.Values
                .Where(r => r.Engine == engine)
                .OrderBy(r => r.Id)
                .Select(r => new MemoryReview(r.Id, r.Engine, r.NodeId, r.BatchId, r.CreatedAt, r.PreAge,
                    r.PreStability, r.PreDifficulty, r.PreStrength, r.PreStrengthAge, r.Grade,
                    r.PostStability, r.PostDifficulty, r.ProvenanceRetrievability, r.Verified))
                .ToList();
            return Task.FromResult<IReadOnlyList<MemoryReview>>(rows);
        }
    }

    /// <inheritdoc />
    public Task RecordSubjectsAsync(string engine, long nodeId, IReadOnlyCollection<string> subjects,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            // REPLACE, never accumulate: a stale subject from an earlier annotation would keep linking future
            // facts into the wrong cluster, and nothing would ever surface it
            _subjects.RemoveAll(s => s.Engine == engine && s.NodeId == nodeId);
            if (!_nodes.TryGetValue(nodeId, out var node)) return Task.CompletedTask;

            // MemorySubject.Canonicalize is the ONE normalization, shared with the SQL backends: a private
            // copy here is how the same annotation ends up linking a different cluster per backend.
            foreach (var subject in MemorySubject.Canonicalize(subjects))
                _subjects.Add((engine, nodeId, node.TaskKey, node.Scope, subject));
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> KnownSubjectsAsync(string engine, string taskKey, string? scope,
        int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (limit <= 0) return Task.FromResult<IReadOnlyList<string>>([]);

        lock (_lock)
        {
            IReadOnlyList<string> subjects =
            [
                .. _subjects
                    .Where(s => s.Engine == engine && s.TaskKey == taskKey)
                    .Where(s => scope is null || s.Scope == scope)
                    .Where(s => _nodes.ContainsKey(s.NodeId))
                    .GroupBy(s => s.Subject, StringComparer.Ordinal)
                    .OrderByDescending(g => g.Count())      // most-used first: the handle worth reusing
                    .ThenBy(g => g.Key, StringComparer.Ordinal)
                    .Select(g => g.Key)
                    .Take(limit),
            ];
            return Task.FromResult(subjects);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<long>> NodesBySubjectAsync(string engine, string taskKey, string? scope,
        string subject, int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!MemorySubject.IsUsable(subject) || limit <= 0)
            return Task.FromResult<IReadOnlyList<long>>([]);

        // the same normalization the write applied, so a lookup can never miss a handle it stored
        var needle = MemorySubject.Normalize(subject);
        lock (_lock)
        {
            IReadOnlyList<long> ids =
            [
                .. _subjects
                    .Where(s => s.Engine == engine && s.TaskKey == taskKey && s.Subject == needle)
                    .Where(s => scope is null || s.Scope == scope)
                    .Select(s => s.NodeId)
                    .Where(_nodes.ContainsKey)      // a pruned node keeps no claim on its subjects
                    .Distinct()
                    .OrderByDescending(id => id)    // newest first, matching the SQL backends
                    .Take(limit),
            ];
            return Task.FromResult(ids);
        }
    }

    // deleting a node takes its edges with it — the SQL backends get this from ON DELETE CASCADE, and a
    // dangling edge here would resurrect a deleted neighbour on the next traversal. Its subjects go too:
    // a subject row pointing at a deleted node would link future facts to something no longer there.
    private void Remove(long id)
    {
        _nodes.Remove(id);
        foreach (var key in _edges.Keys.Where(k => k.From == id || k.To == id).ToList())
            _edges.Remove(key);
        _subjects.RemoveAll(s => s.NodeId == id);
    }

    /// <summary>Project a stored row, filling in the graph-derived fields: how many edges it has, their
    /// summed RAW weight, and how stale the freshest of them is. All plain aggregates — the decay curve is
    /// applied by the engine, never here.
    /// <para>The three primitive ages are plain subtractions against <paramref name="totals"/>, the same
    /// shape as <c>Age</c> against <c>Position</c> — except <c>ElapsedAge</c>, computed in .NET rather than
    /// SQL, which is this backend's own concern only (the SQL backends avoid a date-parsing round trip in the
    /// database for the reason <c>SqliteMemoryGraphStore</c>'s own doc explains).</para></summary>
    private GraphNode ToNode(Row row, EngineTotals totals)
    {
        var edges = _edges.Where(e => e.Key.From == row.Id).Select(e => e.Value).ToList();
        return new GraphNode(
            row.Id, row.Engine, row.TaskKey, row.Scope, row.Headline, row.Content, row.Grade,
            row.CreatedAt, row.RecallCount, row.Stability, totals.Position - row.LastRecalledPosition,
            1, edges.Count, row.Metadata,
            edges.Sum(e => e.Weight),
            edges.Count == 0 ? 0 : totals.Position - edges.Max(e => e.StrengthenedPosition),
            row.Signals,
            OrdinalAge: totals.Ordinal - row.EncodingOrdinal,
            VolumeAge: totals.Chars - row.EncodingChars,
            ElapsedAge: (totals.EncodedAt - row.EncodingAt).TotalDays,
            ProvenanceRetrievability: row.ProvenanceRetrievability,
            ProvenanceSalience: row.ProvenanceSalience,
            Difficulty: row.Difficulty,
            // Each primitive takes its own MAX rather than "the primitives of whichever edge has the max
            // position". That is the same edge either way: all four advance monotonically together from one
            // EngineTotals snapshot, so the freshest edge holds the maximum of every one of them.
            StrengthOrdinalAge: edges.Count == 0 ? 0 : totals.Ordinal - edges.Max(e => e.StrengthenedOrdinal),
            StrengthVolumeAge: edges.Count == 0 ? 0 : totals.Chars - edges.Max(e => e.StrengthenedChars),
            StrengthElapsedAge: edges.Count == 0
                ? 0
                : (totals.EncodedAt - edges.Max(e => e.StrengthenedAt)).TotalDays);
    }
}
