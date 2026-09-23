using Lyntai.Memory;

namespace Lyntai.Storage.InMemory;

/// <summary>
/// The in-process memory graph BOTH in-process stores run, so their semantics are one copy
/// (<c>docs/DECISIONS.md</c> D174). Every mutation is a <c>Plan*</c> that computes a <see cref="GraphChange"/>
/// without mutating, then <see cref="Apply"/>: the in-memory store applies at once, and the file store writes
/// the change to disk first. Not thread-safe — each store holds its own lock around every call.
/// </summary>
internal sealed class MemoryGraphState(Func<DateTimeOffset> clock)
{
    private readonly Dictionary<long, GraphRow> _nodes = [];
    private readonly Dictionary<(string Engine, string TaskKey, string Scope, string Content), long> _identity = [];
    private readonly Dictionary<long, Dictionary<(long To, string Kind), GraphEdgeState>> _out = [];
    private readonly Dictionary<long, HashSet<long>> _in = [];
    // node → recording engine → subjects; a subject steers linking and seeding, never content recall
    private readonly Dictionary<long, Dictionary<string, IReadOnlyList<string>>> _subjects = [];
    private readonly Dictionary<string, GraphTotals> _totals = new(StringComparer.Ordinal);
    private readonly Dictionary<long, MemoryReview> _reviews = [];
    // MemoryReviewLogPacing's per-engine counters: in-process only, so a restart resets them (a SOFT cap)
    private readonly Dictionary<string, long> _reviewCounters = new(StringComparer.Ordinal);
    private long _nextId = 1;
    private long _nextReview = 1;

    public long NextId => _nextId;

    public long NextReviewId => _nextReview;

    public GraphTotals Totals(string engine) => _totals.GetValueOrDefault(engine);

    public GraphRow? Row(long id) => _nodes.GetValueOrDefault(id);

    public IEnumerable<GraphRow> Rows => _nodes.Values;

    public IEnumerable<(GraphEdgeKey Key, GraphEdgeState Edge)> EdgesFrom(long id) =>
        _out.TryGetValue(id, out var outs) ? outs.Select(e => (new GraphEdgeKey(id, e.Key.To, e.Key.Kind), e.Value)) : [];

    public IEnumerable<(string Engine, long NodeId, IReadOnlyList<string> Subjects)> SubjectSets =>
        _subjects.SelectMany(n => n.Value.Select(e => (e.Key, n.Key, e.Value)));

    public IEnumerable<MemoryReview> ReviewsOf(string engine) =>
        _reviews.Values.Where(r => r.Engine == engine).OrderBy(r => r.Id);

    /// <summary>Never hand out an id at or below these — ids are never reused, even after a delete.</summary>
    public void Reserve(long nodeHighWater, long reviewHighWater)
    {
        _nextId = Math.Max(_nextId, nodeHighWater + 1);
        _nextReview = Math.Max(_nextReview, reviewHighWater + 1);
    }

    /// <summary>Whether two versions of one memory read the same — metadata compared by content.</summary>
    public static bool SameRecord(GraphNodeRecord a, GraphNodeRecord b) =>
        a with { Metadata = null } == b with { Metadata = null } && SameMetadata(a.Metadata, b.Metadata);

    private static bool SameMetadata(IReadOnlyDictionary<string, string>? a, IReadOnlyDictionary<string, string>? b) =>
        (a is null || a.Count == 0) ? (b is null || b.Count == 0)
        : b is not null && a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var v) && v == p.Value);

    // ---- planners: compute, never mutate ---------------------------------------------------------------

    public GraphChange PlanUpsert(GraphNodeWrite write)
    {
        var now = clock();
        // advance FIRST, so the new entry's age is zero against what it is stamped with. The three primitives
        // advance unconditionally — never from write.Advance, which is the age policy's business.
        var prior = Totals(write.Engine);
        var totals = new GraphTotals(prior.Position + Math.Max(0, write.Advance), prior.Ordinal + 1,
            prior.Chars + write.Content.Length, now);
        var change = new GraphChange();
        change.Totals.Add((write.Engine, totals));

        if (_identity.TryGetValue((write.Engine, write.TaskKey, write.Scope, write.Content), out var id))
        {
            var existing = _nodes[id];
            change.Nodes.Add((existing, Refresh(existing, write, totals)));
        }
        else
            change.Nodes.Add((null, new GraphRow(
                new GraphNodeRecord(_nextId, write.Engine, write.TaskKey, write.Scope, write.Headline, write.Content,
                    write.Grade, now, write.Metadata),
                new GraphNodeState(0, write.InitialStability, totals.Position, write.Signals, totals.Ordinal,
                    totals.Chars, totals.EncodedAt, write.ProvenanceRetrievability, write.ProvenanceSalience,
                    MemorySignals.Difficulty(write.Signals)))));
        return change;
    }

    // identical content REFRESHES, matching IMemoryStore and ISemanticMemory
    private static GraphRow Refresh(GraphRow existing, GraphNodeWrite write, GraphTotals totals)
    {
        var (record, state) = (existing.Record, existing.State);
        return new GraphRow(
            record with
            {
                // only a CALLER-NAMED grade overwrites, or a re-remember silently demotes an authoritative fact
                Grade = write.GradeStated ? write.Grade : record.Grade,
                // an AUTHORED headline survives a refresh that does not restate it (D91)
                Headline = write.HeadlineStated ? write.Headline : record.Headline,
                // an ABSENT bag is "no opinion" and keeps what is stored; a supplied one replaces it
                Metadata = write.Metadata is null || write.Metadata.Count == 0 ? record.Metadata : write.Metadata,
            },
            state with
            {
                LastRecalledPosition = totals.Position,
                // an EMPTY incoming bag keeps what is stored (GraphNodeWrite.Signals), and its attribution with it
                Signals = write.Signals.Count == 0 ? state.Signals : write.Signals,
                ProvenanceSalience = write.Signals.Count == 0 ? state.ProvenanceSalience : write.ProvenanceSalience,
                // difficulty has a second writer (TouchAsync): only a bag that NAMES it overwrites
                Difficulty = write.Signals.Values.ContainsKey(MemorySignals.WellKnown.Difficulty)
                    ? MemorySignals.Difficulty(write.Signals)
                    : state.Difficulty,
                EncodingOrdinal = totals.Ordinal,
                EncodingChars = totals.Chars,
                EncodingAt = totals.EncodedAt,
            });
    }

    public GraphChange PlanTouch(string engine, IReadOnlyCollection<GraphTouch> touches)
    {
        var change = new GraphChange();
        // a touch stamps each node to where the engine already is, on every scale — it advances nothing
        var totals = Totals(engine);
        var planned = new Dictionary<long, int>(); // id → its slot in change.Nodes, so a repeat compounds
        foreach (var touch in touches)
        {
            var at = planned.TryGetValue(touch.Id, out var i) ? i : -1;
            var node = at >= 0 ? change.Nodes[at].After : _nodes.GetValueOrDefault(touch.Id);
            if (node is null || node.Record.Engine != engine) continue;
            var next = node with
            {
                State = node.State with
                {
                    LastRecalledPosition = totals.Position,
                    Stability = touch.Stability,
                    Difficulty = touch.Difficulty,
                    ProvenanceRetrievability = touch.ProvenanceRetrievability,
                    RecallCount = node.State.RecallCount + 1,
                    EncodingOrdinal = totals.Ordinal,
                    EncodingChars = totals.Chars,
                    EncodingAt = totals.EncodedAt,
                },
            };
            if (at >= 0) change.Nodes[at] = (change.Nodes[at].Before, next);
            else
            {
                planned[touch.Id] = change.Nodes.Count;
                change.Nodes.Add((_nodes[touch.Id], next));
            }
        }
        return change;
    }

    /// <summary>Strengthen edges; <paramref name="into"/> lets a write-back carry its touches and edges as one change.</summary>
    public GraphChange PlanLink(string engine, IReadOnlyList<GraphEdgeWrite> edges, GraphChange? into = null)
    {
        var change = into ?? new GraphChange();
        // one position snapshot for the whole batch: every edge in it was strengthened by the same event
        var totals = Totals(engine);
        var planned = new Dictionary<GraphEdgeKey, int>();
        foreach (var e in edges)
        {
            if (e.From == e.To) continue; // a self-edge is never useful and would skew Degree
            Strengthen(e.From, e.To, e.Kind, e.Weight);
            if (e.Symmetric) Strengthen(e.To, e.From, e.Kind, e.Weight);
        }
        return change;

        void Strengthen(long from, long to, string? kind, double weight)
        {
            var key = new GraphEdgeKey(from, to, kind ?? "");
            var at = planned.TryGetValue(key, out var i) ? i : -1;
            var current = at >= 0 ? change.Edges[at].Edge.Weight
                : _out.TryGetValue(from, out var outs) && outs.TryGetValue((to, key.Kind), out var edge) ? edge.Weight : 0;
            var next = new GraphEdgeState(current + weight, totals.Position, totals.Ordinal, totals.Chars, totals.EncodedAt);
            if (at >= 0) change.Edges[at] = (key, next);
            else
            {
                planned[key] = change.Edges.Count;
                change.Edges.Add((key, next));
            }
        }
    }

    public GraphChange PlanSubjects(string engine, long nodeId, IReadOnlyCollection<string> subjects)
    {
        var change = new GraphChange();
        // REPLACE, never accumulate; MemorySubject.Canonicalize is the ONE normalization. A missing node clears.
        change.Subjects.Add((engine, nodeId, _nodes.ContainsKey(nodeId) ? MemorySubject.Canonicalize(subjects) : []));
        return change;
    }

    public GraphChange PlanReviews(string engine, IReadOnlyCollection<MemoryReviewWrite> reviews, int cap)
    {
        var change = new GraphChange();
        if (reviews.Count == 0) return change;
        var now = clock();
        var id = _nextReview;
        foreach (var r in reviews)
            change.Reviews.Add(new MemoryReview(id++, engine, r.NodeId, r.BatchId, now, r.PreAge, r.PreStability,
                r.PreDifficulty, r.PreStrength, r.PreStrengthAge, r.ReviewGrade, r.PostStability, r.PostDifficulty,
                r.ProvenanceRetrievability, r.Verified));

        // MemoryReviewLogPacing decides from the in-process counter alone, never a per-write DELETE
        var effectiveCap = Math.Max(0, cap);
        var before = _reviewCounters.GetValueOrDefault(engine);
        change.ReviewCounters.Add((engine, before + reviews.Count));
        if (MemoryReviewLogPacing.CrossesBoundary(before, reviews.Count, effectiveCap))
            change.TrimmedReviews.AddRange(ReviewsOf(engine).Concat(change.Reviews)
                .OrderByDescending(r => r.Id).Skip(effectiveCap).Select(r => r.Id));
        return change;
    }

    public GraphChange PlanRemove(IEnumerable<long> ids)
    {
        var change = new GraphChange();
        change.Removed.AddRange(ids);
        return change;
    }

    // ---- which ids a removal takes ------------------------------------------------------------------------

    public IReadOnlyList<long> PruneIds(string engine, string taskKey, string? scope, double? maxAgeOverStability,
        TimeSpan? olderThan)
    {
        var position = Totals(engine).Position;
        var createdBefore = olderThan is null ? (DateTimeOffset?)null : clock() - olderThan.Value;
        return [.. InScope(engine, taskKey, scope)
            // never eligible: an authoritative node's retrievability is fixed at 1
            .Where(n => n.Record.Grade != MemoryGrade.Authoritative)
            .Where(n =>
                (maxAgeOverStability is double c &&
                 (position - n.State.LastRecalledPosition) /
                    Math.Max(n.State.Stability, MemoryGraphSql.MinimumStabilityValue) > c) ||
                (createdBefore is DateTimeOffset before && n.Record.CreatedAt < before))
            .Select(n => n.Id)];
    }

    // scoped to the named engine: an id read from a DIFFERENT engine must not be removable through this one
    public IReadOnlyList<long> DeleteIds(string engine, IReadOnlyCollection<long> ids) =>
        [.. ids.Distinct().Where(id => _nodes.TryGetValue(id, out var n) && n.Record.Engine == engine)];

    public IReadOnlyList<long> ForgetIds(string engine, string taskKey, string? scope) =>
        [.. InScope(engine, taskKey, scope).Select(n => n.Id)];

    private IEnumerable<GraphRow> InScope(string engine, string taskKey, string? scope) =>
        _nodes.Values.Where(n => n.Record.Engine == engine && n.Record.TaskKey == taskKey
                                 && (scope is null || n.Record.Scope == scope));

    // ---- apply ---------------------------------------------------------------------------------------------

    public void Apply(GraphChange change)
    {
        foreach (var (engine, totals) in change.Totals) _totals[engine] = totals;
        foreach (var (_, row) in change.Nodes)
        {
            _nodes[row.Id] = row;
            _identity[Identity(row.Record)] = row.Id;
            _nextId = Math.Max(_nextId, row.Id + 1);
        }
        foreach (var (key, edge) in change.Edges)
        {
            if (!_out.TryGetValue(key.From, out var outs)) _out[key.From] = outs = [];
            outs[(key.To, key.Kind)] = edge;
            if (!_in.TryGetValue(key.To, out var sources)) _in[key.To] = sources = [];
            sources.Add(key.From);
        }
        foreach (var (engine, nodeId, subjects) in change.Subjects)
        {
            if (!_subjects.TryGetValue(nodeId, out var byEngine)) _subjects[nodeId] = byEngine = new(StringComparer.Ordinal);
            if (subjects.Count > 0) byEngine[engine] = subjects;
            else byEngine.Remove(engine);
            if (byEngine.Count == 0) _subjects.Remove(nodeId);
        }
        foreach (var id in change.Removed) Remove(id);
        foreach (var review in change.Reviews)
        {
            _reviews[review.Id] = review;
            _nextReview = Math.Max(_nextReview, review.Id + 1);
        }
        foreach (var id in change.TrimmedReviews) _reviews.Remove(id);
        foreach (var (engine, count) in change.ReviewCounters) _reviewCounters[engine] = count;
    }

    private static (string, string, string, string) Identity(GraphNodeRecord r) => (r.Engine, r.TaskKey, r.Scope, r.Content);

    // a node takes its edges and subjects with it — the SQL backends get this from ON DELETE CASCADE, and a
    // dangling edge would resurrect a deleted neighbour on the next traversal
    private void Remove(long id)
    {
        if (!_nodes.Remove(id, out var row)) return;
        // a duplicate identity (a repaired file beside a newer copy) may own the index entry; leave it alone
        if (_identity.TryGetValue(Identity(row.Record), out var owner) && owner == id) _identity.Remove(Identity(row.Record));
        if (_out.Remove(id, out var outs))
            foreach (var to in outs.Keys.Select(k => k.To).Distinct())
                if (_in.TryGetValue(to, out var sources) && sources.Remove(id) && sources.Count == 0) _in.Remove(to);
        if (_in.Remove(id, out var from))
            foreach (var source in from)
                if (_out.TryGetValue(source, out var edges))
                {
                    foreach (var key in edges.Keys.Where(k => k.To == id).ToList()) edges.Remove(key);
                    if (edges.Count == 0) _out.Remove(source);
                }
        _subjects.Remove(id);
    }

    // ---- reads ---------------------------------------------------------------------------------------------

    public IReadOnlyList<GraphNode> Seed(string engine, string taskKey, string? scope, string? query, int limit)
    {
        var totals = Totals(engine);
        var terms = SearchTerms.SubstringTerms(query);
        return [.. InScope(engine, taskKey, scope)
            // authoritative material is admitted unconditionally; FAINTNESS excludes nothing — decay buries by rank
            .Where(n => n.Record.Grade == MemoryGrade.Authoritative || Matches(n.Record, query, terms))
            // exact facts first, or the limit cuts the quietest one; then salience — through MemorySignals.Salience,
            // the one coercion every backend calls — then recency
            .OrderByDescending(n => n.Record.Grade == MemoryGrade.Authoritative)
            .ThenByDescending(n => MemorySignals.Salience(n.State.Signals))
            .ThenByDescending(n => n.State.LastRecalledPosition)
            .ThenByDescending(n => n.Id) // unique tiebreaker: ties must not wobble
            .Take(limit)
            // a grade-admitted node the query never matched reports 0; this read asked, so it may answer (D97)
            .Select(n => ToNode(n, totals) with
            {
                Relevance = Matches(n.Record, query, terms) ? 1 : 0,
                Matched = Matches(n.Record, query, terms),
            })];
    }

    // ANY term, case-insensitively, over content AND headline; a query too short to yield a term matches whole
    private static bool Matches(GraphNodeRecord node, string? query, IReadOnlyList<string> terms) =>
        string.IsNullOrWhiteSpace(query) ||
        (terms.Count == 0
            ? node.Content.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)
              || node.Headline.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)
            : terms.Any(t => node.Content.Contains(t, StringComparison.OrdinalIgnoreCase)
                          || node.Headline.Contains(t, StringComparison.OrdinalIgnoreCase)));

    public IReadOnlyList<GraphNeighbour> Neighbours(string engine, string taskKey, IReadOnlyCollection<long> ids, int limit)
    {
        var totals = Totals(engine);
        var frontier = ids.ToHashSet();
        return [.. frontier
            .Where(_out.ContainsKey)
            .SelectMany(from => _out[from].Where(e => !frontier.Contains(e.Key.To)))
            .GroupBy(e => e.Key.To)
            // the strongest edge reaching each neighbour, RAW — the engine applies the curve and re-ranks. Each mark
            // takes its own MAX: all four advance together, so the freshest edge holds every maximum.
            .Select(g => (Id: g.Key,
                Weight: g.Max(e => e.Value.Weight),
                At: g.Max(e => e.Value.Position),
                Ordinal: g.Max(e => e.Value.Ordinal),
                Chars: g.Max(e => e.Value.Chars),
                When: g.Max(e => e.Value.At)))
            .OrderByDescending(x => x.Weight)
            .ThenByDescending(x => x.Id)
            // the walk stays inside the task, or taskKey is a boundary for every read but this one
            .Where(x => _nodes.TryGetValue(x.Id, out var n) && n.Record.Engine == engine && n.Record.TaskKey == taskKey)
            .Take(limit)
            .Select(x => new GraphNeighbour(ToNode(_nodes[x.Id], totals), x.Weight, totals.Position - x.At,
                EdgeOrdinalAge: totals.Ordinal - x.Ordinal,
                EdgeVolumeAge: totals.Chars - x.Chars,
                EdgeElapsedAge: (totals.EncodedAt - x.When).TotalDays))];
    }

    public GraphNode? Get(string engine, long id) =>
        _nodes.TryGetValue(id, out var node) && node.Record.Engine == engine ? ToNode(node, Totals(engine)) : null;

    public IReadOnlyList<MemoryReview> Reviews(string engine) => [.. ReviewsOf(engine)];

    public IReadOnlyList<string> KnownSubjects(string engine, string taskKey, string? scope, int limit) =>
        limit <= 0 ? [] : [.. SubjectRows(engine, taskKey, scope)
            .GroupBy(s => s.Subject, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count()) // most-used first: the handle worth reusing
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .Take(limit)];

    public IReadOnlyList<long> NodesBySubject(string engine, string taskKey, string? scope, string subject, int limit)
    {
        if (!MemorySubject.IsUsable(subject) || limit <= 0) return [];
        // the same normalization the write applied, so a lookup can never miss a handle it stored
        var needle = MemorySubject.Normalize(subject);
        return [.. SubjectRows(engine, taskKey, scope)
            .Where(s => s.Subject == needle)
            .Select(s => s.NodeId)
            .Distinct()
            .OrderByDescending(id => id) // newest first, matching the SQL backends
            .Take(limit)];
    }

    // every subject this engine recorded, for live nodes in the task and scope
    private IEnumerable<(long NodeId, string Subject)> SubjectRows(string engine, string taskKey, string? scope)
    {
        foreach (var (nodeId, byEngine) in _subjects)
            if (byEngine.TryGetValue(engine, out var subjects) && _nodes.TryGetValue(nodeId, out var node)
                && node.Record.TaskKey == taskKey && (scope is null || node.Record.Scope == scope))
                foreach (var subject in subjects)
                    yield return (nodeId, subject);
    }

    // graph-derived fields are plain aggregates over RAW weights — the curve is applied by the engine, never here
    private GraphNode ToNode(GraphRow row, GraphTotals totals)
    {
        var (r, s) = (row.Record, row.State);
        IReadOnlyCollection<GraphEdgeState> edges = _out.TryGetValue(row.Id, out var outs) ? outs.Values : [];
        return new GraphNode(
            r.Id, r.Engine, r.TaskKey, r.Scope, r.Headline, r.Content, r.Grade, r.CreatedAt, s.RecallCount,
            s.Stability, totals.Position - s.LastRecalledPosition,
            // Relevance 0 with Matched null — "nobody asked"; Seed overwrites both (D97)
            0, edges.Count, r.Metadata,
            edges.Sum(e => e.Weight),
            edges.Count == 0 ? 0 : totals.Position - edges.Max(e => e.Position),
            s.Signals,
            OrdinalAge: totals.Ordinal - s.EncodingOrdinal,
            VolumeAge: totals.Chars - s.EncodingChars,
            ElapsedAge: (totals.EncodedAt - s.EncodingAt).TotalDays,
            ProvenanceRetrievability: s.ProvenanceRetrievability,
            ProvenanceSalience: s.ProvenanceSalience,
            Difficulty: s.Difficulty,
            StrengthOrdinalAge: edges.Count == 0 ? 0 : totals.Ordinal - edges.Max(e => e.Ordinal),
            StrengthVolumeAge: edges.Count == 0 ? 0 : totals.Chars - edges.Max(e => e.Chars),
            StrengthElapsedAge: edges.Count == 0 ? 0 : (totals.EncodedAt - edges.Max(e => e.At)).TotalDays,
            Matched: null);
    }
}
