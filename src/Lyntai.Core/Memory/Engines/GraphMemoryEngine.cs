using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Memory.Engines;

/// <summary>
/// Memory that forgets and relinks: entries decay unless reused, connect to whatever was recalled beside
/// them, and open as a cheap index of headlines that expand on demand.
/// <para>Recall runs seed → spread → score → filter → touch. It is the one recall path in this library that
/// WRITES — reinforcement and co-activation are recorded for what it returned — so both writes are
/// best-effort: a failure logs and the hits still come back, and a read-only database therefore degrades to
/// "no learning" rather than to "no memory".</para>
/// </summary>
/// <param name="name">This engine's name, hierarchical when it is a member of a composite.</param>
/// <param name="store">Node and edge storage.</param>
/// <param name="options">Retrieval knobs; null takes the defaults.</param>
/// <param name="policy">The decay curve; null builds a <see cref="HalfLifeRetrievability"/> from
/// <see cref="GraphMemoryOptions.Decay"/>.</param>
/// <param name="clock">Injected time — a decay model tested against the wall clock cannot be tested.</param>
/// <param name="logger">Optional; reinforcement and recall failures are logged rather than thrown.</param>
public sealed class GraphMemoryEngine(
    string name,
    IMemoryGraphStore store,
    GraphMemoryOptions? options = null,
    IRetrievabilityPolicy? policy = null,
    Func<DateTimeOffset>? clock = null,
    ILogger<GraphMemoryEngine>? logger = null)
    : IMemoryEngine, IExpandableMemory, ILinkableMemory, IForgettableMemory
{
    private readonly GraphMemoryOptions _options = options ?? new GraphMemoryOptions();
    private readonly IRetrievabilityPolicy _policy =
        policy ?? new HalfLifeRetrievability((options ?? new GraphMemoryOptions()).Decay);
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly ILogger _logger = logger ?? NullLogger<GraphMemoryEngine>.Instance;

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public MemoryGrades Supported => MemoryGrades.Associative | MemoryGrades.Authoritative;

    /// <inheritdoc />
    public async Task<MemoryRef> RememberAsync(MemoryWrite write, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        var grade = write.Grade == MemoryGrade.Inherit ? MemoryGrade.Associative : write.Grade;

        // authoritative material is NEVER passed through headline derivation — a truncated exact fact is
        // confidently wrong, which is worse than having no memory at all
        var headline = write.Headline
            ?? (grade == MemoryGrade.Authoritative
                ? write.Content
                : MemoryHeadline.Derive(write.Content, _options.HeadlineChars));

        var id = await store.UpsertAsync(
            new GraphNodeWrite(Name, write.TaskKey, write.Scope, headline, write.Content, grade,
                _policy.InitialStability, write.Metadata),
            _clock(), ct).ConfigureAwait(false);

        return new MemoryRef(Name, id.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc />
    public async Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        var now = _clock();
        var limit = query.Limit ?? _options.DefaultLimit;

        List<(GraphNode Node, int Hop)> found;
        try
        {
            found = await GatherAsync(query, limit, now, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "graph recall failed for {Engine}/{Task}; returning nothing",
                Name, query.TaskKey);
            return MemoryRecall.Empty;
        }

        var scored = found
            .Select(f =>
            {
                var retrievability = Retrievability(f.Node, now);
                return (f.Node, Retrievability: retrievability,
                    Rank: f.Node.Relevance * retrievability * Math.Pow(_options.HopAttenuation, f.Hop));
            })
            .Where(x => x.Node.Grade == MemoryGrade.Authoritative
                        || x.Retrievability >= _options.MinRetrievability)
            .OrderByDescending(x => x.Rank)
            .ThenByDescending(x => x.Node.Id) // unique tiebreaker: ties must not wobble
            .Take(limit)
            .ToList();

        if (scored.Count == 0) return MemoryRecall.Empty;

        await ReinforceAsync([.. scored.Select(x => x.Node)], now, ct).ConfigureAwait(false);

        var items = scored
            .Select(x => new MemoryItem(
                new MemoryRef(Name, x.Node.Id.ToString(CultureInfo.InvariantCulture)),
                x.Node.Headline,
                // associative content is withheld until expansion — that is what makes the first load
                // cheap; authoritative content is always present, because it is never returned truncated
                x.Node.Grade == MemoryGrade.Authoritative ? x.Node.Content : null,
                x.Node.Grade, x.Node.Relevance, x.Retrievability, x.Node.Degree))
            .ToList();

        return new MemoryRecall(items, MemorySources.Graph);
    }

    /// <inheritdoc />
    public async Task<MemoryRecall> ExpandAsync(MemoryRef reference, int hops = 1, int? charBudget = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!long.TryParse(reference.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return MemoryRecall.Empty;

        var now = _clock();
        var node = await store.GetAsync(Name, id, ct).ConfigureAwait(false);
        if (node is null) return MemoryRecall.Empty;

        var neighbours = await store
            .NeighboursAsync(Name, [id], _options.DefaultLimit, now, ct).ConfigureAwait(false);

        // expanding a node reinforces it — digging in one direction is exactly what should make that
        // direction more retrievable next time
        await ReinforceAsync([node], now, ct).ConfigureAwait(false);

        var items = new List<MemoryItem>(neighbours.Count + 1)
        {
            // the expanded node carries its FULL content whatever its grade — that is what expansion IS
            new(reference, node.Headline, node.Content, node.Grade, 1, Retrievability(node, now), node.Degree),
        };
        items.AddRange(neighbours.Select(n => new MemoryItem(
            new MemoryRef(Name, n.Id.ToString(CultureInfo.InvariantCulture)),
            n.Headline, n.Grade == MemoryGrade.Authoritative ? n.Content : null,
            n.Grade, n.Relevance, Retrievability(n, now), n.Degree)));

        return new MemoryRecall(items, MemorySources.Graph);
    }

    /// <inheritdoc />
    public async Task LinkAsync(MemoryRef from, MemoryRef to, string? kind = null, double weight = 1.0,
        bool symmetric = false, CancellationToken ct = default)
    {
        if (!long.TryParse(from.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var a) ||
            !long.TryParse(to.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
            throw new ArgumentException(
                $"Memory engine '{Name}' addresses nodes by numeric id; got '{from.Id}' and '{to.Id}'.");

        // an EXPLICIT link is a write, so it surfaces its failure — unlike the co-activation edges recall
        // records opportunistically, which are best-effort
        await store.LinkAsync(a, b, kind, weight, symmetric, _clock(), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<int> PruneAsync(string taskKey, string? scope = null, double? minRetrievability = null,
        TimeSpan? olderThan = null, CancellationToken ct = default) =>
        store.PruneAsync(Name, taskKey, scope,
            minRetrievability is double m ? _policy.CandidateCutoff(m) : null,
            olderThan, _clock(), ct);

    /// <summary>Forget everything under (<paramref name="taskKey"/>, <paramref name="scope"/>) — explicit,
    /// never a side effect of decay, which only ever ranks.</summary>
    /// <param name="taskKey">The task to clear.</param>
    /// <param name="scope">The scope, or null for every scope of the task.</param>
    /// <param name="ct">Cancellation.</param>
    public Task ForgetAsync(string taskKey, string? scope = null, CancellationToken ct = default) =>
        store.ForgetAsync(Name, taskKey, scope, ct);

    private double Retrievability(GraphNode node, DateTimeOffset now) =>
        node.Grade == MemoryGrade.Authoritative ? 1 : _policy.Retrievability(node.DecayState, now);

    private async Task<List<(GraphNode Node, int Hop)>> GatherAsync(MemoryQuery query, int limit,
        DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = _policy.CandidateCutoff(_options.MinRetrievability);
        var candidates = limit * Math.Max(1, _options.CandidateMultiplier);

        var seeds = await store.SeedAsync(Name, query.TaskKey, query.Scope, query.Query,
            double.IsPositiveInfinity(cutoff) ? null : cutoff, candidates, now, ct).ConfigureAwait(false);

        var found = new List<(GraphNode Node, int Hop)>(seeds.Select(n => (n, 0)));
        var seen = seeds.Select(n => n.Id).ToHashSet();
        var frontier = seen.ToList();

        for (var hop = 1; hop <= _options.Hops && frontier.Count > 0; hop++)
        {
            ct.ThrowIfCancellationRequested();
            var neighbours = await store
                .NeighboursAsync(Name, frontier, candidates, now, ct).ConfigureAwait(false);

            frontier = [];
            foreach (var neighbour in neighbours)
                if (seen.Add(neighbour.Id))
                {
                    found.Add((neighbour, hop));
                    frontier.Add(neighbour.Id);
                }
        }

        return found;
    }

    /// <summary>Record reinforcement and co-activation for what a recall actually returned.
    /// <para>BEST-EFFORT by design: a failure logs and the caller keeps its hits, so a read-only database
    /// degrades to "no learning" rather than to "no memory". Co-activation is capped, or a ten-item recall
    /// would write forty-five edges on every turn.</para></summary>
    private async Task ReinforceAsync(IReadOnlyList<GraphNode> nodes, DateTimeOffset now,
        CancellationToken ct)
    {
        try
        {
            var touches = nodes
                .Where(n => n.Grade != MemoryGrade.Authoritative) // nothing to reinforce at r = 1
                .Select(n => new GraphTouch(n.Id, now, _policy.Reinforce(n.DecayState, now)))
                .ToList();
            if (touches.Count > 0) await store.TouchAsync(touches, ct).ConfigureAwait(false);

            var top = nodes.Take(Math.Max(0, _options.CoActivationCap)).Select(n => n.Id).ToList();
            for (var i = 0; i < top.Count; i++)
                for (var j = i + 1; j < top.Count; j++)
                    await store.LinkAsync(top[i], top[j], null, 1, symmetric: true, now, ct)
                        .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "graph reinforcement failed for {Engine}; returning hits without learning", Name);
        }
    }
}
