using Lyntai.Memory.Interference;

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
/// <param name="Stability">Half-life, in the engine's units (see <see cref="Lyntai.Memory.Interference.IMemoryAgePolicy"/>).</param>
/// <param name="Age">How far the engine's position has moved since this entry was last used — a plain
/// subtraction the store computes; the policy turns it into a probability. This is the store's single
/// <c>Advance</c>-driven view, and what a policy needing the exact write-time judgment
/// (<see cref="Lyntai.Memory.Interference.BurstDampenedAgePolicy"/>) is measured against.
/// <see cref="OrdinalAge"/>/<see cref="VolumeAge"/>/<see cref="ElapsedAge"/> are a SEPARATE,
/// policy-independent set tracked unconditionally alongside it.</param>
/// <param name="OrdinalAge">Writes to the engine since this entry was last used — what
/// <see cref="Lyntai.Memory.Interference.PerWriteAgePolicy"/> projects from. Advances by one per write
/// whatever policy is installed, so swapping one never reinterprets it.</param>
/// <param name="VolumeAge">Characters written to the engine since this entry was last used — what
/// <see cref="Lyntai.Memory.Interference.ContentSizeAgePolicy"/> projects from, advancing unconditionally by
/// each write's length.</param>
/// <param name="ElapsedAge">Real days since this entry was last used — what
/// <see cref="Lyntai.Memory.Interference.ElapsedAgePolicy"/> projects from. Frozen between writes like
/// <see cref="Age"/>: a recall does not advance it, so it is "as of the most recent write".</param>
/// <param name="Relevance">How well it matched the seeding query, 0..1 — a backend's own normalized rank
/// POSITION within one seed, not a portable score. SQLite normalizes its bm25 order; Postgres normalizes the
/// order its single query returned; the in-process store has no rank order and reports <c>1</c> for a match
/// and <c>0</c> for a grade-admitted non-match. Only the range and the direction
/// (higher is better, within one seed, from one backend) are contractual — see
/// <see cref="IMemoryGraphStore.SeedAsync"/> on where an admitted-but-non-matching authoritative node
/// lands.</param>
/// <param name="Degree">How many edges it has.</param>
/// <param name="Metadata">App-owned extra data, or null.</param>
/// <param name="Strength">The summed RAW weight of its edges — how embedded in the graph it is. A store
/// computes this as a plain <c>SUM</c>, never applying the decay curve.</param>
/// <param name="StrengthAge">How far the position has moved since any of those edges was last strengthened
/// — a plain <c>MAX</c> subtracted from the current position. The store's <c>Advance</c>-driven view, with
/// <see cref="Age"/>'s caveat: it speaks whatever unit was in force when the edge was strengthened. The
/// three <c>Strength*Age</c> members below are the policy-independent counterparts, so a
/// <see cref="Lyntai.Memory.Interference.MemoryAgeKind.Derivable"/> policy projects its own rather than
/// reading a residue in a foreign unit.</param>
/// <param name="StrengthOrdinalAge">Strength-side counterpart of <see cref="OrdinalAge"/>.</param>
/// <param name="StrengthVolumeAge">Strength-side counterpart of <see cref="VolumeAge"/>.</param>
/// <param name="StrengthElapsedAge">Strength-side counterpart of <see cref="ElapsedAge"/>, frozen between
/// writes for the same reason.</param>
/// <param name="Signals">Open retention signals recorded with the entry, replayed into
/// <see cref="MemoryDecayState.Signals"/> on read. Empty for anything written before signals existed, which
/// is why a retention policy must treat an empty bag as neutral.</param>
/// <param name="Difficulty">How hard this entry is to retain, replayed into
/// <see cref="MemoryDecayState.Difficulty"/> on read — see that field's own remarks for the full precedence
/// rule between an explicit <see cref="MemorySignals.WellKnown.Difficulty"/> signal and this LIVE value.
/// Neutral (<c>5</c>, the mid-point, NOT the floor <c>1</c> — see
/// <see cref="Lyntai.Memory.Forgetting.DsrOptions.NeutralDifficulty"/>'s own remarks) for anything written
/// before this column existed, the same shape <paramref name="Signals"/> itself already has for a
/// pre-signals row — though a row written under the OLD neutral still reads back <c>1</c>, a historical
/// fact this record's own default does not retroactively change. Set at first write from whatever the
/// incoming signals bag says (or the neutral default when it says nothing), refreshed on any later write
/// whose incoming bag is NON-EMPTY (mirroring <c>salience</c>'s own promoted-column rule exactly), and
/// otherwise updated only by <see cref="IMemoryGraphStore.TouchAsync"/>.</param>
/// <param name="ProvenanceRetrievability">Which retrievability policy computed <paramref name="Stability"/>
/// and <paramref name="Difficulty"/> — a
/// <see cref="Lyntai.Memory.Forgetting.MemoryRetrievabilityProvenance"/> value, read and written only through
/// <see cref="Lyntai.Memory.MemoryProvenance"/>. Set at first write and on every
/// <see cref="IMemoryGraphStore.TouchAsync"/>, exactly like <paramref name="Stability"/>, so a plain
/// re-remember never touches it. Zero (<c>None</c>) for anything predating the domain.</param>
/// <param name="ProvenanceSalience">Which salience polic(ies) PRODUCED <paramref name="Signals"/> — the OR
/// of every registered policy that returned a non-empty result. Follows <paramref name="Signals"/>'s own
/// "empty incoming keeps what's stored" rule, so a re-remember whose policies all decline leaves it
/// unchanged. Zero for anything predating the domain, or for a write no policy judged.</param>
public sealed record GraphNode(
    long Id, string Engine, string TaskKey, string Scope, string Headline, string Content,
    MemoryGrade Grade, DateTimeOffset CreatedAt, int RecallCount, double Stability, double Age,
    double Relevance, int Degree, IReadOnlyDictionary<string, string>? Metadata,
    double Strength = 0, double StrengthAge = 0, MemorySignals Signals = default,
    double OrdinalAge = 0, double VolumeAge = 0, double ElapsedAge = 0,
    long ProvenanceRetrievability = 0, long ProvenanceSalience = 0, double Difficulty = 5,
    double StrengthOrdinalAge = 0, double StrengthVolumeAge = 0, double StrengthElapsedAge = 0)
{
    /// <summary>This node's decay bookkeeping, for an <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy"/>.
    /// <para>Carries <see cref="Age"/> — the store's own, single, <c>Advance</c>-driven view — unchanged by
    /// the primitives below existing.</para></summary>
    public MemoryDecayState DecayState =>
        new(Age, RecallCount, Stability, Strength, StrengthAge, Signals, Difficulty);

    /// <summary>This node's view across the three policy-independent primitives, for an
    /// <see cref="Lyntai.Memory.Interference.IMemoryAgePolicy.Age"/> projection.</summary>
    public MemoryAgeSample AgeSample => new(OrdinalAge, VolumeAge, ElapsedAge);

    /// <summary>The same view for the entry's CONNECTION age — how long since any of its edges was last
    /// strengthened — so a <see cref="Lyntai.Memory.Interference.MemoryAgeKind.Derivable"/> policy projects
    /// <see cref="MemoryDecayState.StrengthAge"/> in its own unit exactly as <see cref="AgeSample"/> lets it
    /// project <see cref="MemoryDecayState.Age"/>.
    /// <para>Deliberately the SAME <see cref="MemoryAgeSample"/> type rather than a parallel one: an age
    /// policy's projection is a pure function of the three primitives and cannot tell — or need to tell —
    /// whether they were measured from an encoding or from a strengthening.</para></summary>
    public MemoryAgeSample StrengthAgeSample =>
        new(StrengthOrdinalAge, StrengthVolumeAge, StrengthElapsedAge);
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
/// <see cref="Lyntai.Memory.Interference.IMemoryAgePolicy"/>. Everything already stored ages by exactly this much.</param>
/// <param name="Metadata">App-owned extra data, or null.</param>
/// <param name="Signals">Open retention signals recorded with the entry, replayed into
/// <see cref="MemoryDecayState.Signals"/> on read. Empty for anything written before signals existed, which
/// is why a retention policy must treat an empty bag as neutral.
/// <para><b>A store MUST read an empty incoming bag as "no opinion" and keep what is already stored, never
/// as "no longer salient".</b> A salience policy may decline to judge a re-remembered write for any reason,
/// and reports that as <see cref="MemorySignals.Empty"/> — so blanking on empty would let the very
/// re-remember meant to REINFORCE an entry erase an earlier judgement instead.</para></param>
/// <param name="ProvenanceRetrievability">The active retrievability policy's own
/// <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy.Provenance"/>, cast to
/// <see langword="long"/> — what computed <paramref name="InitialStability"/>. A store persists this only
/// on a genuine first write, exactly like <paramref name="InitialStability"/> itself is never revisited by
/// a plain re-remember of identical content.</param>
/// <param name="ProvenanceSalience">Every contributing salience policy's own
/// <see cref="Lyntai.Memory.Salience.IMemorySaliencePolicy.Provenance"/>, already OR'd (through
/// <see cref="Lyntai.Memory.MemoryProvenance.Pack(System.Collections.Generic.IEnumerable{long})"/>) into one
/// value, cast to <see langword="long"/> — which polic(ies) produced <paramref name="Signals"/>. A store
/// applies the SAME "empty incoming keeps what's stored" rule <paramref name="Signals"/> itself already
/// gets: this value is meaningful only when <paramref name="Signals"/> is non-empty, so a store must keep
/// the existing column rather than blank it when the incoming bag is empty.</param>
public sealed record GraphNodeWrite(
    string Engine, string TaskKey, string Scope, string Headline, string Content, MemoryGrade Grade,
    double InitialStability, double Advance, IReadOnlyDictionary<string, string>? Metadata,
    MemorySignals Signals = default, long ProvenanceRetrievability = 0, long ProvenanceSalience = 0);

/// <summary>A reinforcement to record against one node. The store stamps the current position — a recall
/// does not advance it, so "now" is simply wherever the engine already is.
/// <para>This is the seam <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy.Reinforce"/>'s
/// full-<see cref="MemoryDecayState"/> return exists to feed (design doc §5.7, Task 5): a caller extracts
/// whatever the active policy actually claims from that return and hands it here — <see cref="Difficulty"/>
/// is the first field besides <see cref="Stability"/> a shipped policy uses it for
/// (<see cref="Lyntai.Memory.Forgetting.DsrRetrievability"/>).</para></summary>
/// <param name="Id">The node.</param>
/// <param name="Stability">Its new half-life, from the policy.</param>
/// <param name="ProvenanceRetrievability">The policy that computed <paramref name="Stability"/> and
/// <paramref name="Difficulty"/> this time — the SAME policy's own
/// <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy.Provenance"/> that produced them, cast to
/// <see langword="long"/>. A store overwrites the node's stored value with this on every touch, exactly as
/// it overwrites <paramref name="Stability"/> itself.</param>
/// <param name="Difficulty">Its new difficulty, from the policy — see
/// <see cref="MemoryDecayState.Difficulty"/>'s own remarks for what "new" means here. Defaults to the
/// neutral value (<c>5</c>, the mid-point) only for a caller that never reads
/// a policy's difficulty at all; a caller that DOES (every shipped engine path, via
/// <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy.Reinforce"/>'s
/// return) always supplies the policy's own value explicitly.</param>
public readonly record struct GraphTouch(
    long Id, double Stability, long ProvenanceRetrievability = 0, double Difficulty = 5);

/// <summary>A node reached by traversal, with the edge that reached it — so the ENGINE can rank by
/// effective (decayed) edge weight while the store reports the raw value and its age. The store never
/// applies the curve; the same division of labour as node ranking.</summary>
/// <param name="Node">The neighbour.</param>
/// <param name="EdgeWeight">The connecting edge's raw weight.</param>
/// <param name="EdgeAge">How far the position has moved since that edge was last strengthened — the store's
/// own <c>Advance</c>-driven view, exactly as <see cref="GraphNode.Age"/> is, and carrying the same caveat
/// about the unit it speaks. The three below are its policy-independent counterparts.</param>
/// <param name="EdgeOrdinalAge">How many writes have happened since that edge was last strengthened.</param>
/// <param name="EdgeVolumeAge">How many characters have been written since that edge was last
/// strengthened.</param>
/// <param name="EdgeElapsedAge">How much real time has passed, in days, since that edge was last
/// strengthened.</param>
public sealed record GraphNeighbour(GraphNode Node, double EdgeWeight, double EdgeAge,
    double EdgeOrdinalAge = 0, double EdgeVolumeAge = 0, double EdgeElapsedAge = 0)
{
    /// <summary>This EDGE's own view across the three policy-independent primitives, so the engine projects
    /// traversal age through the installed <see cref="Lyntai.Memory.Interference.IMemoryAgePolicy"/>s exactly
    /// as it projects a node's own age and connection age.
    /// <para>Distinct from <see cref="GraphNode.StrengthAgeSample"/> on <see cref="Node"/>, which is the MAX
    /// across ALL of that node's edges — this one is the single edge that actually reached it.</para></summary>
    public MemoryAgeSample EdgeAgeSample => new(EdgeOrdinalAge, EdgeVolumeAge, EdgeElapsedAge);
}

/// <summary>One reinforcement to log — the pre-review state, the derived grade, and the post-review state
/// (design spec §3). What FSRS parameter fitting needs and what this
/// library persisted none of before this task; see <see cref="IMemoryGraphStore.RecordReviewsAsync"/>.
/// <para><b>Only <see cref="MemoryDecayState.Stability"/> and <see cref="MemoryDecayState.Difficulty"/> get a
/// POST column.</b> Every other field of the pre-state (<see cref="PreAge"/>/<see cref="PreStrength"/>/
/// <see cref="PreStrengthAge"/>) is, by <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy.Reinforce"/>'s
/// own contract, returned UNCHANGED by every policy this library ships — see that member's own remarks ("a
/// policy MUST return every field it does not own exactly as state carries it"). Duplicating them as
/// "post" columns would record the same number twice; widen this type, the same way
/// <see cref="GraphTouch"/> would need widening, the day a policy claims a third field.</para></summary>
/// <param name="NodeId">The node this reinforcement was applied to.</param>
/// <param name="BatchId">Groups every reinforcement one recall (or expansion) produced into a single logical
/// event — <see cref="Lyntai.Memory.Engines.GraphMemoryEngine.RecallAsync"/> mints one per call and shares it
/// across every node <c>ReinforceAsync</c> touches that time, because a fitter may care that these
/// co-occurred (they were reinforced by the SAME recall, potentially competing candidates from the same
/// query) even though each row is still one independent <c>(state, grade, outcome)</c> observation on its
/// own.</param>
/// <param name="PreAge">The entry's age immediately before this reinforcement.</param>
/// <param name="PreStability">Its stability immediately before this reinforcement.</param>
/// <param name="PreDifficulty">Its difficulty immediately before this reinforcement.</param>
/// <param name="PreStrength">Its connection strength immediately before this reinforcement.</param>
/// <param name="PreStrengthAge">How stale that connection strength was immediately before this
/// reinforcement.</param>
/// <param name="Grade">The grade actually used to update difficulty this time —
/// <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy.DerivedGrade"/>'s own return, verbatim,
/// never re-derived from the state alone. Null when no grade-driven update happened at all (that member's
/// own remarks explain both reasons null can mean), which is why this column is the one deliberate exception
/// to this schema's usual <c>NOT NULL</c> convention — see the migration's own doc comment.</param>
/// <param name="PostStability">Its stability immediately after this reinforcement.</param>
/// <param name="PostDifficulty">Its difficulty immediately after this reinforcement.</param>
/// <param name="ProvenanceRetrievability">The policy that computed this reinforcement — the same value
/// <see cref="GraphTouch.ProvenanceRetrievability"/> carries for the SAME touch, so a fitter can segment a
/// log spanning a policy swap.</param>
/// <param name="Verified">What an <see cref="Lyntai.Memory.Verification.IMemoryVerificationPolicy"/> judged
/// about this entry for the recall that logged it: <c>true</c> it answered the query, <c>false</c> it did
/// not, <c>null</c> no verifier ran.
/// <para><b>This is the only column in the log that is not derived from the curve's own prediction, which is
/// the whole reason it exists.</b> <paramref name="Grade"/> comes from
/// <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy.DerivedGrade"/>, computed from the very
/// constants a fit would estimate — so maximising the likelihood of those grades recovers whatever produced
/// the log (<c>docs/DECISIONS.md</c> <b>D51</b>). An external judgement breaks that circularity.</para>
/// <para><b><c>false</c> and <c>null</c> are NOT interchangeable.</b> <c>false</c> is an observed failure —
/// the recall returned this and a judge said it did not answer — and it is the observation the log could
/// never previously contain, because rows were only written for entries that got reinforced. <c>null</c> is
/// the absence of any judgement. Collapsing them would turn "no verifier configured" into a corpus of
/// failures.</para></param>
public sealed record MemoryReviewWrite(
    long NodeId, Guid BatchId, double PreAge, double PreStability, double PreDifficulty, double PreStrength,
    double PreStrengthAge, double? Grade, double PostStability, double PostDifficulty,
    long ProvenanceRetrievability = 0, bool? Verified = null);

/// <summary>One logged reinforcement, as stored — <see cref="MemoryReviewWrite"/> plus what the store
/// itself assigns (<see cref="Id"/>, <see cref="Engine"/>, <see cref="CreatedAt"/>).</summary>
/// <param name="Id">Store-assigned, unique within the store.</param>
/// <param name="Engine">The owning engine's name.</param>
/// <param name="NodeId"><inheritdoc cref="MemoryReviewWrite.NodeId" path="/summary"/></param>
/// <param name="BatchId"><inheritdoc cref="MemoryReviewWrite.BatchId" path="/summary"/></param>
/// <param name="CreatedAt">When this reinforcement was logged. Wall-clock, for auditing only — nothing in
/// this domain's own arithmetic reads it.</param>
/// <param name="PreAge"><inheritdoc cref="MemoryReviewWrite.PreAge" path="/summary"/></param>
/// <param name="PreStability"><inheritdoc cref="MemoryReviewWrite.PreStability" path="/summary"/></param>
/// <param name="PreDifficulty"><inheritdoc cref="MemoryReviewWrite.PreDifficulty" path="/summary"/></param>
/// <param name="PreStrength"><inheritdoc cref="MemoryReviewWrite.PreStrength" path="/summary"/></param>
/// <param name="PreStrengthAge"><inheritdoc cref="MemoryReviewWrite.PreStrengthAge" path="/summary"/></param>
/// <param name="Grade"><inheritdoc cref="MemoryReviewWrite.Grade" path="/summary"/></param>
/// <param name="PostStability"><inheritdoc cref="MemoryReviewWrite.PostStability" path="/summary"/></param>
/// <param name="PostDifficulty"><inheritdoc cref="MemoryReviewWrite.PostDifficulty" path="/summary"/></param>
/// <param name="ProvenanceRetrievability">
/// <inheritdoc cref="MemoryReviewWrite.ProvenanceRetrievability" path="/summary"/></param>
/// <param name="Verified"><inheritdoc cref="MemoryReviewWrite.Verified" path="/summary"/></param>
public sealed record MemoryReview(
    long Id, string Engine, long NodeId, Guid BatchId, DateTimeOffset CreatedAt, double PreAge,
    double PreStability, double PreDifficulty, double PreStrength, double PreStrengthAge, double? Grade,
    double PostStability, double PostDifficulty, long ProvenanceRetrievability, bool? Verified = null);

/// <summary>
/// Storage for the graph memory engine: nodes, weighted edges, and the decay bookkeeping.
/// <para><b>The store never evaluates the decay curve.</b> Where it needs a faintness threshold at all it
/// uses a plain <c>age / stability &lt;= cutoff</c> comparison supplied by
/// <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy.CandidateCutoff"/>; exact retrievability and final ranking happen in the
/// engine, because no fixed SQL expression could encode a policy the application supplies.
/// <b><see cref="SeedAsync"/> is not one of those places</b> — it applies NO faintness bound, so the cutoff's
/// only consumer is <see cref="PruneAsync"/>. A cutoff that is too narrow therefore DELETES entries the curve
/// still rates retrievable; it does not merely shorten a recall.</para>
/// <para><b>Age is a subtraction, not a duration.</b> The store keeps a monotone position per engine,
/// advanced by each write's <see cref="GraphNodeWrite.Advance"/>, and reports how far it has moved since
/// each entry was last used. What that position counts is the engine's <see cref="Lyntai.Memory.Interference.IMemoryAgePolicy"/>'s
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
    /// <paramref name="taskKey"/>, <paramref name="scope"/>) matching <paramref name="query"/>, capped at
    /// <paramref name="limit"/> — <b>authoritative material first, then most recently used</b>, so that what
    /// the <paramref name="limit"/> cuts is the freshest ASSOCIATIVE material rather than the quietest exact
    /// fact.
    /// <para><b>Faintness never excludes a candidate.</b> A decayed entry is returned like any other and is
    /// hidden — if at all — by being OUTRANKED in the engine, which is the whole model: decay buries, it
    /// does not cut. The only bound here is the count, so a faint memory alone in a quiet engine still
    /// comes back. Deleting is <see cref="PruneAsync"/>'s job and is always explicit.</para>
    /// <para>A null or whitespace <paramref name="query"/> takes the most recent. AUTHORITATIVE nodes are
    /// admitted unconditionally — the query does not exclude them.</para>
    /// <para><b>The bound on that.</b> "Admitted unconditionally" means the QUERY never excludes an
    /// authoritative node — not that <paramref name="limit"/> can hold them all. A scope holding more
    /// authoritative nodes than <paramref name="limit"/> still loses some, by recency, and which ones is
    /// deliberately unspecified. Unlike the prompt layer, which reports what it omitted, the store drops
    /// silently. Keep authoritative material in a scope small enough that this cannot bite.</para>
    /// <para><b>How a SALIENT candidate is admitted is backend-specific, by the same carve-out.</b> Where
    /// nothing has already ranked candidates by match quality — the no-query and substring-fallback paths —
    /// salience leads recency, so a salient entry survives a limit recency alone would have cut. On a
    /// MATCH-RANKED path (SQLite's FTS branch) the score leads and salience is only a tiebreak, or a salient
    /// POOR match would displace a strong one. So "a salient entry is found even when it matches poorly" is
    /// a guarantee of the recency-ordered paths, not of every path. Rank contribution proper belongs to the
    /// ranking policy and is opt-in.</para>
    /// <para><b>An authoritative node admitted by GRADE that <paramref name="query"/> never matched reports
    /// <see cref="GraphNode.Relevance"/> exactly <c>0</c>, on every backend.</b> <c>0</c> is what "how well
    /// it matched the query" honestly says about something the query did not match; a node that genuinely
    /// matched keeps its match-derived position, and with no <paramref name="query"/> nothing is
    /// grade-admitted so each backend's gradient is unchanged.</para>
    /// <para>Portable guarantee, the same one <see cref="Lyntai.Storage.IMemoryStore.RecallAsync"/> states:
    /// a node whose content contains a single ≥3-character query token as a substring is found on every
    /// backend. Multi-token matching and same-match ordering diverge by design.</para></summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="taskKey">Consumer/purpose scope.</param>
    /// <param name="scope">Variant scope, or null for every scope of the task.</param>
    /// <param name="query">Relevance query, or null for the most recent.</param>
    /// <param name="limit">Maximum candidates.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<GraphNode>> SeedAsync(string engine, string taskKey, string? scope, string? query,
        int limit, CancellationToken ct = default);

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
    /// position AND the current <see cref="GraphNode.OrdinalAge"/>/<see cref="GraphNode.VolumeAge"/>/
    /// <see cref="GraphNode.ElapsedAge"/> primitives — a touch resets a node's age on every scale at once,
    /// not only the one <see cref="GraphNode.Age"/> reads. Best-effort by contract: the caller treats a
    /// failure here as "no learning", never as "no memory".</summary>
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

    /// <summary>Remove nodes, returning how many were removed. AUTHORITATIVE nodes are never eligible for
    /// <paramref name="maxAgeOverStability"/> — their retrievability is fixed at 1.</summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="taskKey">Consumer/purpose scope.</param>
    /// <param name="scope">Variant scope, or null for every scope of the task.</param>
    /// <param name="maxAgeOverStability">Remove past this ratio, or null to ignore it.</param>
    /// <param name="olderThan">Remove entries created longer ago than this in REAL time, or null to ignore
    /// it — the one calendar concern left in the model.</param>
    /// <param name="ct">Cancellation.</param>
    Task<int> PruneAsync(string engine, string taskKey, string? scope, double? maxAgeOverStability,
        TimeSpan? olderThan, CancellationToken ct = default);

    /// <summary>Remove specific nodes by id, and every edge touching one — returning how many were
    /// removed. The precise counterpart to <see cref="PruneAsync"/>'s own ratio filter, for a caller that
    /// has already decided WHICH nodes to remove rather than asking the store to decide.
    /// <para><b>Why this exists:</b>
    /// <see cref="PruneAsync"/>'s ratio filter is evaluated entirely by the STORE against its own
    /// <c>Advance</c>-driven position accumulator — cheap, but only correct when that accumulator is what a
    /// caller's retrievability curve actually reads. Once a caller composes age from several coexisting
    /// <see cref="Lyntai.Memory.Interference.IMemoryAgePolicy"/>s — some <c>Derivable</c>, whose resolved age
    /// the store cannot recompute without knowing the caller's own composition rule — the caller must
    /// evaluate retrievability itself, over candidates <see cref="SeedAsync"/> already returns, and remove
    /// precisely the ones it decided on. This is <b>not</b> exempt for AUTHORITATIVE nodes the way
    /// <see cref="PruneAsync"/> is — the caller is trusted to have already applied that exemption, since
    /// deciding exactly which ids to remove is this method's whole point.</para></summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="ids">The nodes to remove. Empty removes nothing and returns 0.</param>
    /// <param name="ct">Cancellation.</param>
    Task<int> DeleteAsync(string engine, IReadOnlyCollection<long> ids, CancellationToken ct = default);

    /// <summary>Remove every node in the scope, and every edge touching one.</summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="taskKey">Consumer/purpose scope.</param>
    /// <param name="scope">Variant scope, or null for every scope of the task.</param>
    /// <param name="ct">Cancellation.</param>
    Task ForgetAsync(string engine, string taskKey, string? scope, CancellationToken ct = default);

    /// <summary>Log <paramref name="reviews"/> — DATA for a future fitting task (design spec §3/§4),
    /// never read by anything in this domain's own recall, ranking or prune paths.
    /// Best-effort by the SAME contract as <see cref="TouchAsync"/>: a caller treats a failure here as
    /// "this reinforcement was not logged," never as reinforcement itself failing.
    /// <para><b>Bounded, and NOT by a per-write <c>DELETE</c>.</b> <paramref name="cap"/> is the most recent
    /// rows this call retains PER ENGINE; an implementation pays for enforcing it only occasionally, paced by
    /// <see cref="Lyntai.Memory.MemoryReviewLogPacing"/>, never on every call — seeing every backend apply the
    /// SAME pacing is what makes the cap's cost bounded on all three rather than on however many happened to
    /// get audited. This makes the cap SOFT: between trims, one engine can transiently hold up to
    /// <c>cap + MemoryReviewLogPacing.TrimInterval(cap) - 1</c> rows, and (being paced from an in-process
    /// counter, not a persisted one) a process restart can let it grow further still before the next trim
    /// catches up. Both are acceptable for a log whose job is giving a fitter something to read, not
    /// enforcing an exact budget.</para></summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="reviews">The reinforcements to log. Empty logs nothing.</param>
    /// <param name="cap">The most recent rows to retain for this engine; clamped to zero or above.</param>
    /// <param name="ct">Cancellation.</param>
    Task RecordReviewsAsync(string engine, IReadOnlyCollection<MemoryReviewWrite> reviews, int cap,
        CancellationToken ct = default);

    /// <summary>Every review currently retained for <paramref name="engine"/>, oldest first — for a fitter
    /// (design spec §4, not this task) or a test to inspect what <see cref="RecordReviewsAsync"/> actually
    /// recorded. Always bounded by whatever cap <see cref="RecordReviewsAsync"/> has been enforcing, so
    /// reading everything for one engine is always cheap.</summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<MemoryReview>> ReviewsAsync(string engine, CancellationToken ct = default);

    /// <summary>
    /// Record what a node is ABOUT, replacing whatever it was recorded as before.
    ///
    /// <para><b>Why this is stored rather than searched for.</b> An engine can link two facts by searching
    /// for a subject, but only when SOME entry names it in its own text — and the case that matters most has
    /// no such entry. Three facts about one owner ("the spouse is Alice", "the deploy key is in the vault",
    /// "the client is northern logistics") are all about *me*, and none of them contains "me". That is
    /// precisely the corpus's attribute cluster, and precisely where the measured no-graph floor comes from,
    /// so search-based linking cannot reach it by construction
    /// (<c>MemorySubjectLinkingTests.A_shared_subject_that_no_entry_names_links_nothing</c>).</para>
    ///
    /// <para><b>Kept out of the content index deliberately.</b> Appending subjects to the searchable text
    /// would make the existing seed path find them with no new method at all — and would silently change
    /// what every ordinary recall matches, which is a far larger blast radius than linking needs. Subjects
    /// steer LINKING; content steers RECALL.</para>
    ///
    /// <para>Replaces rather than accumulates, so re-remembering a fact whose annotation changed does not
    /// leave the old subjects behind — a stale subject links future facts to the wrong cluster forever, and
    /// nothing would ever reveal it.</para>
    /// </summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="nodeId">The node the subjects describe.</param>
    /// <param name="subjects">Its subjects; empty clears them. Normalized through
    /// <see cref="MemorySubject.Canonicalize"/> — trimmed, lowercased INVARIANTLY, de-duplicated — so an
    /// annotator that varies capitalization or padding still links. <b>A backend implements that rule by
    /// CALLING it, never by restating it</b>: a private <c>ToLower()</c> folds <c>"I"</c> differently under a
    /// Turkish culture, so the same handle would stop matching across machines.</param>
    /// <param name="ct">Cancellation.</param>
    Task RecordSubjectsAsync(string engine, long nodeId, IReadOnlyCollection<string> subjects,
        CancellationToken ct = default);

    /// <summary>The nodes recorded under <paramref name="subject"/>, newest first, within one task and
    /// scope — the lookup <see cref="RecordSubjectsAsync"/> exists to serve.</summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="taskKey">The task to search within.</param>
    /// <param name="scope">The scope, or null for every scope in the task.</param>
    /// <param name="subject">The subject, normalized through <see cref="MemorySubject.Normalize"/> — the
    /// same rule the write applied, which is what makes a lookup unable to miss a handle it stored.</param>
    /// <param name="limit">The most nodes to return; bounds how many edges one annotated write can create.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<long>> NodesBySubjectAsync(string engine, string taskKey, string? scope,
        string subject, int limit, CancellationToken ct = default);

    /// <summary>
    /// The subjects already in use in this task and scope, most-used first — so an annotator can REUSE a
    /// handle instead of inventing one.
    ///
    /// <para><b>Why this exists.</b> "What is this about" has several DEFENSIBLE answers for one fact — the
    /// entity, the relation, the location — so an unanchored annotator alternates between them and nothing
    /// links. Showing it the handles already in use is what anchors it, exactly as showing it recent facts is
    /// what makes a pronoun resolvable.</para>
    ///
    /// <para><b>The one member here with a default body</b>, unlike the five required additions this release
    /// makes. It is an accuracy HINT, not a correctness requirement: a store returning nothing gives an
    /// annotator no reuse candidates and everything still works, just less consistently. Forcing every BYO
    /// store to implement it for a feature it may never enable would be a cost with no matching
    /// guarantee.</para>
    /// </summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="taskKey">The task to read within.</param>
    /// <param name="scope">The scope, or null for every scope in the task.</param>
    /// <param name="limit">The most subjects to return.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<string>> KnownSubjectsAsync(string engine, string taskKey, string? scope,
        int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}
