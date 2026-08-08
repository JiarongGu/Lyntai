using Lyntai.Memory;

namespace Lyntai.Storage.InMemory;

/// <summary>In-process <see cref="IMemoryGraphStore"/> — the zero-dependency default, and the reference
/// implementation the SQL backends are held to by the shared store contract.
/// <para>Keeps a monotone POSITION per engine, advanced by each write's
/// <see cref="GraphNodeWrite.Advance"/>. An entry's age is how far that position has moved since it was
/// last used — a subtraction, never a duration. Wall-clock timestamps are recorded but feed only
/// <see cref="PruneAsync"/>'s <c>olderThan</c> and auditing.</para>
/// <para>Recall matches the query as a CONTIGUOUS case-insensitive substring and ranks by recency, exactly
/// like <see cref="InMemoryMemoryStore"/>; SQLite's trigram/bm25 behaviour diverges by design and the
/// contract asserts only the portable single-token guarantee.</para></summary>
/// <param name="clock">Time source for the audit timestamps; null takes the system clock.</param>
public sealed class InMemoryMemoryGraphStore(Func<DateTimeOffset>? clock = null) : IMemoryGraphStore
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Lock _lock = new();
    private readonly Dictionary<long, Row> _nodes = [];
    private readonly Dictionary<(long From, long To, string Kind), Edge> _edges = [];
    private readonly Dictionary<string, double> _positions = new(StringComparer.Ordinal);
    private long _next = 1;

    /// <summary>A stored node. <c>LastRecalledPosition</c> is where the engine's position stood when this
    /// was last used; the age reported to callers is the difference from where it stands now.</summary>
    private sealed record Row(
        long Id, string Engine, string TaskKey, string Scope, string Headline, string Content,
        MemoryGrade Grade, DateTimeOffset CreatedAt, int RecallCount, double Stability,
        double LastRecalledPosition, IReadOnlyDictionary<string, string>? Metadata);

    /// <summary>A stored edge. The weight only ever grows; decay is applied at read time by whoever owns
    /// the curve, so this store holds no curve constant.</summary>
    private readonly record struct Edge(double Weight, double StrengthenedPosition);

    private double Position(string engine) =>
        _positions.TryGetValue(engine, out var p) ? p : 0;

    /// <inheritdoc />
    public Task<long> UpsertAsync(GraphNodeWrite write, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            // the write crowds everything already stored: advance FIRST, so the new entry's own age is
            // zero relative to the position it is stamped with
            var position = Position(write.Engine) + Math.Max(0, write.Advance);
            _positions[write.Engine] = position;

            var existing = _nodes.Values.FirstOrDefault(n =>
                n.Engine == write.Engine && n.TaskKey == write.TaskKey && n.Scope == write.Scope &&
                n.Content == write.Content);
            if (existing is not null)
            {
                // identical content REFRESHES, matching IMemoryStore and ISemanticMemory
                _nodes[existing.Id] = existing with
                {
                    LastRecalledPosition = position,
                    Grade = write.Grade,
                    Headline = write.Headline,
                };
                return Task.FromResult(existing.Id);
            }

            var id = _next++;
            _nodes[id] = new Row(id, write.Engine, write.TaskKey, write.Scope, write.Headline,
                write.Content, write.Grade, _clock(), 0, write.InitialStability, position, write.Metadata);
            return Task.FromResult(id);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphNode>> SeedAsync(string engine, string taskKey, string? scope,
        string? query, double? maxAgeOverStability, int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            var position = Position(engine);
            var hits = _nodes.Values
                .Where(n => n.Engine == engine && n.TaskKey == taskKey)
                .Where(n => scope is null || n.Scope == scope)
                // authoritative material is admitted unconditionally — neither the query nor the decay
                // cutoff may exclude an exact fact
                .Where(n => n.Grade == MemoryGrade.Authoritative || Matches(n, query))
                .Where(n => n.Grade == MemoryGrade.Authoritative
                            || WithinCutoff(n, position, maxAgeOverStability))
                .OrderByDescending(n => n.LastRecalledPosition)
                .ThenByDescending(n => n.Id) // unique tiebreaker: ties must not wobble
                .Take(limit)
                .Select(n => ToNode(n, position))
                .ToList();
            return Task.FromResult<IReadOnlyList<GraphNode>>(hits);
        }

        static bool Matches(Row node, string? query) =>
            string.IsNullOrWhiteSpace(query) ||
            node.Content.Contains(query, StringComparison.OrdinalIgnoreCase);

        static bool WithinCutoff(Row node, double position, double? cutoff)
        {
            if (cutoff is not double c || double.IsPositiveInfinity(c)) return true;
            var stability = node.Stability > 0 ? node.Stability : 0.000001;
            return (position - node.LastRecalledPosition) / stability <= c;
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphNeighbour>> NeighboursAsync(string engine,
        IReadOnlyCollection<long> ids, int limit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            var position = Position(engine);
            var frontier = ids.ToHashSet();
            var hits = _edges
                .Where(e => frontier.Contains(e.Key.From) && !frontier.Contains(e.Key.To))
                .GroupBy(e => e.Key.To)
                // the strongest edge reaching this neighbour, and how stale it is; RAW weight only — the
                // engine applies the decay curve and re-ranks
                .Select(g => (Id: g.Key,
                    Weight: g.Max(e => e.Value.Weight),
                    At: g.Max(e => e.Value.StrengthenedPosition)))
                .OrderByDescending(x => x.Weight)
                .ThenByDescending(x => x.Id) // unique tiebreaker
                .Where(x => _nodes.TryGetValue(x.Id, out var n) && n.Engine == engine)
                .Take(limit)
                .Select(x => new GraphNeighbour(ToNode(_nodes[x.Id], position), x.Weight, position - x.At))
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
                ? ToNode(node, Position(engine))
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
            // a recall does NOT advance the position — it stamps wherever the engine already is
            var position = Position(engine);
            foreach (var touch in touches)
                if (_nodes.TryGetValue(touch.Id, out var node) && node.Engine == engine)
                    _nodes[touch.Id] = node with
                    {
                        LastRecalledPosition = position,
                        Stability = touch.Stability,
                        RecallCount = node.RecallCount + 1,
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
            var position = Position(engine);
            Strengthen(from, to, position);
            if (symmetric) Strengthen(to, from, position);
        }
        return Task.CompletedTask;

        void Strengthen(long a, long b, double position)
        {
            var key = (a, b, kind ?? "");
            var existing = _edges.TryGetValue(key, out var edge) ? edge.Weight : 0;
            _edges[key] = new Edge(existing + weight, position);
        }
    }

    /// <inheritdoc />
    public Task<int> PruneAsync(string engine, string taskKey, string? scope,
        double? maxAgeOverStability, TimeSpan? olderThan, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            var position = Position(engine);
            var createdBefore = olderThan is null ? (DateTimeOffset?)null : _clock() - olderThan.Value;
            var doomed = _nodes.Values
                .Where(n => n.Engine == engine && n.TaskKey == taskKey)
                .Where(n => scope is null || n.Scope == scope)
                // never eligible: an authoritative node's retrievability is fixed at 1, so this falls out
                // of the formula rather than needing a special case
                .Where(n => n.Grade != MemoryGrade.Authoritative)
                .Where(n =>
                    (maxAgeOverStability is double c &&
                     (position - n.LastRecalledPosition) / (n.Stability > 0 ? n.Stability : 0.000001) > c) ||
                    (createdBefore is DateTimeOffset before && n.CreatedAt < before))
                .Select(n => n.Id)
                .ToList();
            foreach (var id in doomed) Remove(id);
            return Task.FromResult(doomed.Count);
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

    // deleting a node takes its edges with it — the SQL backends get this from ON DELETE CASCADE, and a
    // dangling edge here would resurrect a deleted neighbour on the next traversal
    private void Remove(long id)
    {
        _nodes.Remove(id);
        foreach (var key in _edges.Keys.Where(k => k.From == id || k.To == id).ToList())
            _edges.Remove(key);
    }

    /// <summary>Project a stored row, filling in the graph-derived fields: how many edges it has, their
    /// summed RAW weight, and how stale the freshest of them is. All plain aggregates — the decay curve is
    /// applied by the engine, never here.</summary>
    private GraphNode ToNode(Row row, double position)
    {
        var edges = _edges.Where(e => e.Key.From == row.Id).Select(e => e.Value).ToList();
        return new GraphNode(
            row.Id, row.Engine, row.TaskKey, row.Scope, row.Headline, row.Content, row.Grade,
            row.CreatedAt, row.RecallCount, row.Stability, position - row.LastRecalledPosition,
            1, edges.Count, row.Metadata,
            edges.Sum(e => e.Weight),
            edges.Count == 0 ? 0 : position - edges.Max(e => e.StrengthenedPosition));
    }
}
