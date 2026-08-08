namespace Lyntai.Memory;

/// <summary>One stored node.</summary>
/// <param name="Id">Store-assigned, unique within the store.</param>
/// <param name="Engine">The owning engine's name.</param>
/// <param name="TaskKey">Consumer/purpose scope.</param>
/// <param name="Scope">Variant scope.</param>
/// <param name="Headline">The one-line form recall returns.</param>
/// <param name="Content">The full text expansion returns.</param>
/// <param name="Grade">Associative or authoritative; never <see cref="MemoryGrade.Inherit"/>.</param>
/// <param name="CreatedAt">When first stored. Wall-clock, and used ONLY for
/// <see cref="IMemoryGraphStore.PruneAsync"/>'s <c>olderThan</c> and for auditing — decay does not read
/// it.</param>
/// <param name="RecallCount">How many times recalled.</param>
/// <param name="Stability">Half-life, in the engine's units (see <see cref="IMemoryClock"/>).</param>
/// <param name="Age">How far the engine's position has moved since this entry was last used. A plain
/// subtraction the store computes; the policy turns it into a probability.</param>
/// <param name="Relevance">How well it matched the seeding query, 0..1. A backend that ranks by recency
/// rather than relevance reports 1.</param>
/// <param name="Degree">How many edges it has.</param>
/// <param name="Metadata">App-owned extra data, or null.</param>
/// <param name="Strength">The summed RAW weight of its edges — how embedded in the graph it is. A store
/// computes this as a plain <c>SUM</c>, never applying the decay curve.</param>
/// <param name="StrengthAge">How far the position has moved since any of those edges was last
/// strengthened — a plain <c>MAX</c> subtracted from the current position.</param>
public sealed record GraphNode(
    long Id, string Engine, string TaskKey, string Scope, string Headline, string Content,
    MemoryGrade Grade, DateTimeOffset CreatedAt, int RecallCount, double Stability, double Age,
    double Relevance, int Degree, IReadOnlyDictionary<string, string>? Metadata,
    double Strength = 0, double StrengthAge = 0)
{
    /// <summary>This node's decay bookkeeping, for an <see cref="IRetrievabilityPolicy"/>.</summary>
    public MemoryDecayState DecayState => new(Age, RecallCount, Stability, Strength, StrengthAge);
}

/// <summary>A node to store. Identity is (<paramref name="Engine"/>, <paramref name="TaskKey"/>,
/// <paramref name="Scope"/>, <paramref name="Content"/>) — storing identical content refreshes rather than
/// duplicating, matching every other memory surface in the library.</summary>
/// <param name="Engine">The owning engine's name.</param>
/// <param name="TaskKey">Consumer/purpose scope.</param>
/// <param name="Scope">Variant scope.</param>
/// <param name="Headline">The one-line form; the engine derives it when the caller authored none.</param>
/// <param name="Content">The full text.</param>
/// <param name="Grade">Associative or authoritative.</param>
/// <param name="InitialStability">Half-life for a new entry, from the policy.</param>
/// <param name="Advance">How far this write moves the engine's position, from its
/// <see cref="IMemoryClock"/>. Everything already stored ages by exactly this much.</param>
/// <param name="Metadata">App-owned extra data, or null.</param>
public sealed record GraphNodeWrite(
    string Engine, string TaskKey, string Scope, string Headline, string Content, MemoryGrade Grade,
    double InitialStability, double Advance, IReadOnlyDictionary<string, string>? Metadata);

/// <summary>A reinforcement to record against one node. The store stamps the current position — a recall
/// does not advance it, so "now" is simply wherever the engine already is.</summary>
/// <param name="Id">The node.</param>
/// <param name="Stability">Its new half-life, from the policy.</param>
public readonly record struct GraphTouch(long Id, double Stability);

/// <summary>A node reached by traversal, with the edge that reached it — so the ENGINE can rank by
/// effective (decayed) edge weight while the store reports the raw value and its age. The store never
/// applies the curve; the same division of labour as node ranking.</summary>
/// <param name="Node">The neighbour.</param>
/// <param name="EdgeWeight">The connecting edge's raw weight.</param>
/// <param name="EdgeAge">How far the position has moved since that edge was last strengthened.</param>
public sealed record GraphNeighbour(GraphNode Node, double EdgeWeight, double EdgeAge);

/// <summary>
/// Storage for the graph memory engine: nodes, weighted edges, and the decay bookkeeping.
/// <para><b>The store never evaluates the decay curve.</b> It bounds its candidate set with a plain
/// <c>age / stability &lt;= cutoff</c> comparison supplied by
/// <see cref="IRetrievabilityPolicy.CandidateCutoff"/>; exact retrievability and final ranking happen in the
/// engine, because no fixed SQL expression could encode a policy the application supplies.</para>
/// <para><b>Age is a subtraction, not a duration.</b> The store keeps a monotone position per engine,
/// advanced by each write's <see cref="GraphNodeWrite.Advance"/>, and reports how far it has moved since
/// each entry was last used. What that position counts is the engine's <see cref="IMemoryClock"/>'s
/// business. Wall-clock timestamps are still stored, but only for <see cref="PruneAsync"/>'s
/// <c>olderThan</c> and for auditing.</para>
/// </summary>
public interface IMemoryGraphStore
{
    /// <summary>Store a node, or refresh the existing one with identical content, advancing the engine's
    /// position by <see cref="GraphNodeWrite.Advance"/>. Returns its id.</summary>
    /// <param name="write">The node to store.</param>
    /// <param name="ct">Cancellation.</param>
    Task<long> UpsertAsync(GraphNodeWrite write, CancellationToken ct = default);

    /// <summary>The candidate set for a recall: nodes in (<paramref name="engine"/>,
    /// <paramref name="taskKey"/>, <paramref name="scope"/>) matching <paramref name="query"/>, bounded by
    /// <paramref name="maxAgeOverStability"/> and capped at <paramref name="limit"/>.
    /// <para>A null or whitespace <paramref name="query"/> takes the most recent. AUTHORITATIVE nodes are
    /// admitted unconditionally — neither the query nor the cutoff excludes them.</para>
    /// <para>Portable guarantee, the same one <see cref="Lyntai.Storage.IMemoryStore.RecallAsync"/> states:
    /// a node whose content contains a single ≥3-character query token as a substring is found on every
    /// backend. Multi-token matching and same-match ordering diverge by design.</para></summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="taskKey">Consumer/purpose scope.</param>
    /// <param name="scope">Variant scope, or null for every scope of the task.</param>
    /// <param name="query">Relevance query, or null for the most recent.</param>
    /// <param name="maxAgeOverStability">The conservative cutoff, or null for no bound.</param>
    /// <param name="limit">Maximum candidates.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<GraphNode>> SeedAsync(string engine, string taskKey, string? scope, string? query,
        double? maxAgeOverStability, int limit, CancellationToken ct = default);

    /// <summary>Nodes connected to any of <paramref name="ids"/>, excluding the <paramref name="ids"/>
    /// themselves, ordered by RAW edge weight.
    /// <para>That ordering is a cheap pre-sort, not the final one: the engine re-ranks by decayed edge
    /// weight, so a heavy but stale link falls below a lighter fresh one. Over-fetch accordingly.</para></summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="ids">The frontier to walk out from.</param>
    /// <param name="limit">Maximum neighbours.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<GraphNeighbour>> NeighboursAsync(string engine, IReadOnlyCollection<long> ids,
        int limit, CancellationToken ct = default);

    /// <summary>One node by id, or null when there is none under that engine.</summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="id">The node.</param>
    /// <param name="ct">Cancellation.</param>
    Task<GraphNode?> GetAsync(string engine, long id, CancellationToken ct = default);

    /// <summary>Record reinforcement for the nodes a recall actually returned, stamping the current
    /// position. Best-effort by contract: the caller treats a failure here as "no learning", never as "no
    /// memory".</summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="touches">The reinforcements to record.</param>
    /// <param name="ct">Cancellation.</param>
    Task TouchAsync(string engine, IReadOnlyCollection<GraphTouch> touches, CancellationToken ct = default);

    /// <summary>Connect two nodes, strengthening the edge when it already exists and stamping the current
    /// position. Directed unless <paramref name="symmetric"/>.
    /// <para>The stored weight only ever grows; decay is applied at READ time by whoever owns the curve, so
    /// the store holds no curve constant. That is what keeps a link which stopped recurring from propping a
    /// memory up forever: its effective weight falls to nothing however large the raw value.</para></summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="from">Source node id.</param>
    /// <param name="to">Target node id.</param>
    /// <param name="kind">Optional relation name; null is an untyped association.</param>
    /// <param name="weight">How much to add to the edge.</param>
    /// <param name="symmetric">Write the reverse edge too.</param>
    /// <param name="ct">Cancellation.</param>
    Task LinkAsync(string engine, long from, long to, string? kind, double weight, bool symmetric,
        CancellationToken ct = default);

    /// <summary>Reap nodes, returning how many were removed. AUTHORITATIVE nodes are never eligible for
    /// <paramref name="maxAgeOverStability"/> — their retrievability is fixed at 1.</summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="taskKey">Consumer/purpose scope.</param>
    /// <param name="scope">Variant scope, or null for every scope of the task.</param>
    /// <param name="maxAgeOverStability">Reap past this ratio, or null to ignore it.</param>
    /// <param name="olderThan">Reap entries created longer ago than this in REAL time, or null to ignore
    /// it — the one calendar concern left in the model.</param>
    /// <param name="ct">Cancellation.</param>
    Task<int> PruneAsync(string engine, string taskKey, string? scope, double? maxAgeOverStability,
        TimeSpan? olderThan, CancellationToken ct = default);

    /// <summary>Remove every node in the scope, and every edge touching one.</summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="taskKey">Consumer/purpose scope.</param>
    /// <param name="scope">Variant scope, or null for every scope of the task.</param>
    /// <param name="ct">Cancellation.</param>
    Task ForgetAsync(string engine, string taskKey, string? scope, CancellationToken ct = default);
}
