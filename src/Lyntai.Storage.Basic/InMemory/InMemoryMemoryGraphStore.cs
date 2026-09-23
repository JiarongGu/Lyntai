using Lyntai.Memory;

namespace Lyntai.Storage.InMemory;

/// <summary>In-process <see cref="IMemoryGraphStore"/> — the zero-dependency default, and the reference
/// implementation the SQL backends are held to by the shared store contract. The file-system store runs the
/// same internal core, so the two cannot drift.
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
    private readonly MemoryGraphState _graph = new(clock ?? (() => DateTimeOffset.UtcNow));
    private readonly Lock _lock = new();

    /// <inheritdoc />
    public Task<long> UpsertAsync(GraphNodeWrite write, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            var change = _graph.PlanUpsert(write);
            _graph.Apply(change);
            return Task.FromResult(change.Nodes[0].After.Id);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphNode>> SeedAsync(string engine, string taskKey, string? scope,
        string? query, int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock) return Task.FromResult(_graph.Seed(engine, taskKey, scope, query, limit));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphNeighbour>> NeighboursAsync(string engine, string taskKey,
        IReadOnlyCollection<long> ids, int limit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ct.ThrowIfCancellationRequested();
        lock (_lock) return Task.FromResult(_graph.Neighbours(engine, taskKey, ids, limit));
    }

    /// <inheritdoc />
    public Task<GraphNode?> GetAsync(string engine, long id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock) return Task.FromResult(_graph.Get(engine, id));
    }

    /// <inheritdoc />
    public Task TouchAsync(string engine, IReadOnlyCollection<GraphTouch> touches, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(touches);
        ct.ThrowIfCancellationRequested();
        lock (_lock) _graph.Apply(_graph.PlanTouch(engine, touches));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task LinkAsync(string engine, long from, long to, string? kind, double weight, bool symmetric,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock) _graph.Apply(_graph.PlanLink(engine, [new GraphEdgeWrite(from, to, kind, weight, symmetric)]));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> PruneAsync(string engine, string taskKey, string? scope,
        double? maxAgeOverStability, TimeSpan? olderThan, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock) return Task.FromResult(Remove(_graph.PruneIds(engine, taskKey, scope, maxAgeOverStability, olderThan)));
    }

    /// <inheritdoc />
    public Task<int> DeleteAsync(string engine, IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ct.ThrowIfCancellationRequested();
        lock (_lock) return Task.FromResult(Remove(_graph.DeleteIds(engine, ids)));
    }

    /// <inheritdoc />
    public Task ForgetAsync(string engine, string taskKey, string? scope, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock) Remove(_graph.ForgetIds(engine, taskKey, scope));
        return Task.CompletedTask;
    }

    private int Remove(IReadOnlyList<long> ids)
    {
        _graph.Apply(_graph.PlanRemove(ids));
        return ids.Count;
    }

    /// <inheritdoc />
    public Task RecordReviewsAsync(string engine, IReadOnlyCollection<MemoryReviewWrite> reviews, int cap,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        ct.ThrowIfCancellationRequested();
        lock (_lock) _graph.Apply(_graph.PlanReviews(engine, reviews, cap));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MemoryReview>> ReviewsAsync(string engine, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock) return Task.FromResult(_graph.Reviews(engine));
    }

    /// <inheritdoc />
    public Task RecordSubjectsAsync(string engine, long nodeId, IReadOnlyCollection<string> subjects,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        ct.ThrowIfCancellationRequested();
        lock (_lock) _graph.Apply(_graph.PlanSubjects(engine, nodeId, subjects));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> KnownSubjectsAsync(string engine, string taskKey, string? scope,
        int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock) return Task.FromResult(_graph.KnownSubjects(engine, taskKey, scope, limit));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<long>> NodesBySubjectAsync(string engine, string taskKey, string? scope,
        string subject, int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock) return Task.FromResult(_graph.NodesBySubject(engine, taskKey, scope, subject, limit));
    }
}
