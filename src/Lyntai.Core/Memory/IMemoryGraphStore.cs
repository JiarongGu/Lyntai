namespace Lyntai.Memory;

/// <summary>One stored node.</summary>
/// <param name="Id">Store-assigned, unique within the store.</param>
/// <param name="Engine">The owning engine's name.</param>
/// <param name="TaskKey">Consumer/purpose scope.</param>
/// <param name="Scope">Variant scope.</param>
/// <param name="Headline">The one-line form recall returns.</param>
/// <param name="Content">The full text expansion returns.</param>
/// <param name="Grade">Associative or authoritative; never <see cref="MemoryGrade.Inherit"/>.</param>
/// <param name="CreatedAt">When first stored.</param>
/// <param name="LastRecalledAt">When last successfully recalled.</param>
/// <param name="RecallCount">How many times recalled.</param>
/// <param name="Stability">Half-life in days.</param>
/// <param name="Relevance">How well it matched the seeding query, 0..1. A backend that ranks by recency
/// rather than relevance reports 1.</param>
/// <param name="Degree">How many edges it has.</param>
/// <param name="Metadata">App-owned extra data, or null.</param>
/// <param name="Strength">The summed RAW weight of its edges — how embedded in the graph it is. A store
/// computes this as a plain <c>SUM</c>, never applying the decay curve.</param>
/// <param name="StrengthAsOf">When any of those edges was last strengthened — a plain <c>MAX</c> — so the
/// policy can decay <paramref name="Strength"/> as one aggregate.</param>
public sealed record GraphNode(
    long Id, string Engine, string TaskKey, string Scope, string Headline, string Content,
    MemoryGrade Grade, DateTimeOffset CreatedAt, DateTimeOffset LastRecalledAt, int RecallCount,
    double Stability, double Relevance, int Degree, IReadOnlyDictionary<string, string>? Metadata,
    double Strength = 0, DateTimeOffset? StrengthAsOf = null)
{
    /// <summary>This node's decay bookkeeping, for an <see cref="IRetrievabilityPolicy"/>.</summary>
    public MemoryDecayState DecayState =>
        new(CreatedAt, LastRecalledAt, RecallCount, Stability, Strength, StrengthAsOf);
}

/// <summary>A node reached by traversal, with the edge that reached it — so the ENGINE can rank by
/// effective (decayed) edge weight while the store orders by the raw value. The store never applies the
/// curve; the same division of labour as node ranking.</summary>
/// <param name="Node">The neighbour.</param>
/// <param name="EdgeWeight">The connecting edge's raw weight.</param>
/// <param name="EdgeStrengthenedAt">When that edge was last strengthened.</param>
public sealed record GraphNeighbour(GraphNode Node, double EdgeWeight, DateTimeOffset EdgeStrengthenedAt);

/// <summary>A node to store. Identity is (<paramref name="Engine"/>, <paramref name="TaskKey"/>,
/// <paramref name="Scope"/>, <paramref name="Content"/>) — storing identical content refreshes rather than
/// duplicating, matching every other memory surface in the library.</summary>
/// <param name="Engine">The owning engine's name.</param>
/// <param name="TaskKey">Consumer/purpose scope.</param>
/// <param name="Scope">Variant scope.</param>
/// <param name="Headline">The one-line form; the engine derives it when the caller authored none.</param>
/// <param name="Content">The full text.</param>
/// <param name="Grade">Associative or authoritative.</param>
/// <param name="InitialStability">Half-life in days for a new node, from the policy.</param>
/// <param name="Metadata">App-owned extra data, or null.</param>
public sealed record GraphNodeWrite(
    string Engine, string TaskKey, string Scope, string Headline, string Content, MemoryGrade Grade,
    double InitialStability, IReadOnlyDictionary<string, string>? Metadata);

/// <summary>A reinforcement to record against one node.</summary>
/// <param name="Id">The node.</param>
/// <param name="LastRecalledAt">The recall time.</param>
/// <param name="Stability">Its new half-life in days, from the policy.</param>
public readonly record struct GraphTouch(long Id, DateTimeOffset LastRecalledAt, double Stability);

/// <summary>
/// Storage for the graph memory engine: nodes, weighted edges, and the decay bookkeeping.
/// <para><b>The store never evaluates the decay curve.</b> It bounds its candidate set with a plain
/// <c>age_days / stability &lt;= cutoff</c> comparison supplied by
/// <see cref="IRetrievabilityPolicy.CandidateCutoff"/>; exact retrievability and final ranking happen in the
/// engine. SQLite has <c>pow</c> only when built with <c>SQLITE_ENABLE_MATH_FUNCTIONS</c>, and no fixed SQL
/// expression could encode a policy the application supplies.</para>
/// <para>Every method takes <c>now</c> rather than reading a clock, because a decay model tested against
/// the wall clock cannot be tested at all.</para>
/// </summary>
public interface IMemoryGraphStore
{
    /// <summary>Store a node, or refresh the existing one with identical content. Returns its id.</summary>
    /// <param name="write">The node to store.</param>
    /// <param name="now">The current time.</param>
    /// <param name="ct">Cancellation.</param>
    Task<long> UpsertAsync(GraphNodeWrite write, DateTimeOffset now, CancellationToken ct = default);

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
    /// <param name="now">The current time.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<GraphNode>> SeedAsync(string engine, string taskKey, string? scope, string? query,
        double? maxAgeOverStability, int limit, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Nodes connected to any of <paramref name="ids"/>, excluding the <paramref name="ids"/>
    /// themselves, ordered by RAW edge weight.
    /// <para>That ordering is a cheap pre-sort, not the final one: the engine re-ranks by decayed edge
    /// weight, so a heavy but stale link falls below a lighter fresh one. Over-fetch accordingly.</para></summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="ids">The frontier to walk out from.</param>
    /// <param name="limit">Maximum neighbours.</param>
    /// <param name="now">The current time.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<GraphNeighbour>> NeighboursAsync(string engine, IReadOnlyCollection<long> ids,
        int limit, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>One node by id, or null when there is none under that engine.</summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="id">The node.</param>
    /// <param name="ct">Cancellation.</param>
    Task<GraphNode?> GetAsync(string engine, long id, CancellationToken ct = default);

    /// <summary>Record reinforcement for the nodes a recall actually returned. Best-effort by contract: the
    /// caller treats a failure here as "no learning", never as "no memory".</summary>
    /// <param name="touches">The reinforcements to record.</param>
    /// <param name="ct">Cancellation.</param>
    Task TouchAsync(IReadOnlyCollection<GraphTouch> touches, CancellationToken ct = default);

    /// <summary>Connect two nodes, strengthening the edge when it already exists and recording
    /// <paramref name="now"/> as its last strengthening. Directed unless <paramref name="symmetric"/>.
    /// <para>The stored weight only ever grows; decay is applied at READ time by whoever owns the curve, so
    /// the store holds no curve constant. That is what keeps a link which stopped recurring from propping a
    /// memory up forever: its effective weight falls to nothing however large the raw value.</para></summary>
    /// <param name="from">Source node id.</param>
    /// <param name="to">Target node id.</param>
    /// <param name="kind">Optional relation name; null is an untyped association.</param>
    /// <param name="weight">How much to add to the edge.</param>
    /// <param name="symmetric">Write the reverse edge too.</param>
    /// <param name="now">The current time.</param>
    /// <param name="ct">Cancellation.</param>
    Task LinkAsync(long from, long to, string? kind, double weight, bool symmetric, DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>Reap nodes, returning how many were removed. AUTHORITATIVE nodes are never eligible for
    /// <paramref name="maxAgeOverStability"/> — their retrievability is fixed at 1.</summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="taskKey">Consumer/purpose scope.</param>
    /// <param name="scope">Variant scope, or null for every scope of the task.</param>
    /// <param name="maxAgeOverStability">Reap past this ratio, or null to ignore it.</param>
    /// <param name="olderThan">Reap entries created longer ago than this, or null to ignore it.</param>
    /// <param name="now">The current time.</param>
    /// <param name="ct">Cancellation.</param>
    Task<int> PruneAsync(string engine, string taskKey, string? scope, double? maxAgeOverStability,
        TimeSpan? olderThan, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Remove every node in the scope, and every edge touching one.</summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="taskKey">Consumer/purpose scope.</param>
    /// <param name="scope">Variant scope, or null for every scope of the task.</param>
    /// <param name="ct">Cancellation.</param>
    Task ForgetAsync(string engine, string taskKey, string? scope, CancellationToken ct = default);
}
