using Lyntai.Memory;

namespace Lyntai.Storage.InMemory;

/// <summary>In-process <see cref="IMemoryGraphStore"/> — the zero-dependency default, and the reference
/// implementation the SQL backends are held to by the shared store contract.
/// <para>Recall matches the query as a CONTIGUOUS case-insensitive substring and ranks by recency, exactly
/// like <see cref="InMemoryMemoryStore"/>; SQLite's trigram/bm25 behaviour diverges by design and the
/// contract asserts only the portable single-token guarantee.</para></summary>
public sealed class InMemoryMemoryGraphStore : IMemoryGraphStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<long, GraphNode> _nodes = [];
    private readonly Dictionary<(long From, long To, string Kind), double> _edges = [];
    private long _next = 1;

    /// <inheritdoc />
    public Task<long> UpsertAsync(GraphNodeWrite write, DateTimeOffset now, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            var existing = _nodes.Values.FirstOrDefault(n =>
                n.Engine == write.Engine && n.TaskKey == write.TaskKey && n.Scope == write.Scope &&
                n.Content == write.Content);
            if (existing is not null)
            {
                // identical content REFRESHES, matching IMemoryStore and ISemanticMemory
                _nodes[existing.Id] = existing with { LastRecalledAt = now, Grade = write.Grade };
                return Task.FromResult(existing.Id);
            }

            var id = _next++;
            _nodes[id] = new GraphNode(id, write.Engine, write.TaskKey, write.Scope, write.Headline,
                write.Content, write.Grade, now, now, 0, write.InitialStability, 1, 0, write.Metadata);
            return Task.FromResult(id);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphNode>> SeedAsync(string engine, string taskKey, string? scope,
        string? query, double? maxAgeOverStability, int limit, DateTimeOffset now,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            var hits = _nodes.Values
                .Where(n => n.Engine == engine && n.TaskKey == taskKey)
                .Where(n => scope is null || n.Scope == scope)
                // authoritative material is admitted unconditionally — neither the query nor the decay
                // cutoff may exclude an exact fact
                .Where(n => n.Grade == MemoryGrade.Authoritative || Matches(n, query))
                .Where(n => n.Grade == MemoryGrade.Authoritative || WithinCutoff(n, maxAgeOverStability, now))
                .OrderByDescending(n => n.LastRecalledAt)
                .ThenByDescending(n => n.Id) // unique tiebreaker: ties must not wobble
                .Take(limit)
                .Select(WithDegree)
                .ToList();
            return Task.FromResult<IReadOnlyList<GraphNode>>(hits);
        }

        static bool Matches(GraphNode node, string? query) =>
            string.IsNullOrWhiteSpace(query) ||
            node.Content.Contains(query, StringComparison.OrdinalIgnoreCase);

        static bool WithinCutoff(GraphNode node, double? cutoff, DateTimeOffset now)
        {
            if (cutoff is not double c || double.IsPositiveInfinity(c)) return true;
            var stability = node.Stability > 0 ? node.Stability : 1;
            return (now - node.LastRecalledAt).TotalDays / stability <= c;
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphNode>> NeighboursAsync(string engine, IReadOnlyCollection<long> ids,
        int limit, DateTimeOffset now, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            var frontier = ids.ToHashSet();
            var hits = _edges
                .Where(e => frontier.Contains(e.Key.From) && !frontier.Contains(e.Key.To))
                .GroupBy(e => e.Key.To)
                .Select(g => (Id: g.Key, Weight: g.Max(e => e.Value)))
                .OrderByDescending(x => x.Weight)
                .ThenByDescending(x => x.Id) // unique tiebreaker
                .Where(x => _nodes.TryGetValue(x.Id, out var n) && n.Engine == engine)
                .Take(limit)
                .Select(x => WithDegree(_nodes[x.Id]))
                .ToList();
            return Task.FromResult<IReadOnlyList<GraphNode>>(hits);
        }
    }

    /// <inheritdoc />
    public Task<GraphNode?> GetAsync(string engine, long id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
            return Task.FromResult(
                _nodes.TryGetValue(id, out var node) && node.Engine == engine ? WithDegree(node) : null);
    }

    /// <inheritdoc />
    public Task TouchAsync(IReadOnlyCollection<GraphTouch> touches, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(touches);
        ct.ThrowIfCancellationRequested();
        lock (_lock)
            foreach (var touch in touches)
                if (_nodes.TryGetValue(touch.Id, out var node))
                    _nodes[touch.Id] = node with
                    {
                        LastRecalledAt = touch.LastRecalledAt,
                        Stability = touch.Stability,
                        RecallCount = node.RecallCount + 1,
                    };
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task LinkAsync(long from, long to, string? kind, double weight, bool symmetric,
        DateTimeOffset now, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (from == to) return Task.CompletedTask; // a self-edge is never useful and would skew Degree
        lock (_lock)
        {
            Strengthen(from, to);
            if (symmetric) Strengthen(to, from);
        }
        return Task.CompletedTask;

        void Strengthen(long a, long b)
        {
            var key = (a, b, kind ?? "");
            _edges[key] = _edges.TryGetValue(key, out var existing) ? existing + weight : weight;
        }
    }

    /// <inheritdoc />
    public Task<int> PruneAsync(string engine, string taskKey, string? scope,
        double? maxAgeOverStability, TimeSpan? olderThan, DateTimeOffset now,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            var doomed = _nodes.Values
                .Where(n => n.Engine == engine && n.TaskKey == taskKey)
                .Where(n => scope is null || n.Scope == scope)
                // never eligible: an authoritative node's retrievability is fixed at 1, so this falls out
                // of the formula rather than needing a special case
                .Where(n => n.Grade != MemoryGrade.Authoritative)
                .Where(n =>
                    (maxAgeOverStability is double c &&
                     (now - n.LastRecalledAt).TotalDays / (n.Stability > 0 ? n.Stability : 1) > c) ||
                    (olderThan is TimeSpan age && now - n.CreatedAt > age))
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

    private GraphNode WithDegree(GraphNode node) =>
        node with { Degree = _edges.Keys.Count(k => k.From == node.Id) };
}
