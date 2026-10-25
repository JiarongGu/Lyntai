namespace Lyntai.Memory;

/// <summary>
/// The OPTIONAL half of <see cref="IMemoryGraphStore"/> that carries an engine's entries, links and subjects out and
/// in WITH their decay state — what a graph engine's <see cref="IPortableMemory"/> needs, and what
/// <see cref="IMemoryGraphStore.UpsertAsync"/> cannot do: it stamps a fresh entry and advances the clock.
/// <para><b>Separate rather than a member of <see cref="IMemoryGraphStore"/></b>, as
/// <see cref="ICountableMemoryGraphStore"/> is: a required member breaks every store an application wrote. Without it
/// the engine refuses to export or import. All four shipped stores implement it.</para>
/// <para><b>Ages travel RELATIVE to the engine's head</b> (<c>docs/DECISIONS.md</c> D213): an export reports how far
/// each entry and link sits behind where its engine stands, on every scale, and an import stamps each that same
/// distance behind where the importing engine stands. <b>An import advances the engine on no scale</b>, so nothing
/// the target already held ages by it; an engine nothing was written to is given a head at zero on every count
/// scale and at the import's own time.</para>
/// </summary>
public interface IPortableMemoryGraphStore : IMemoryGraphStore
{
    /// <summary>Every node under (<paramref name="engine"/>, <paramref name="taskKey"/>, <paramref name="scope"/>) in
    /// id order, every link leaving one of them to a node of the same engine and task, and their subjects — read
    /// as of one position.</summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="taskKey">The task to read.</param>
    /// <param name="scope">The scope, or null for every scope of the task.</param>
    /// <param name="ct">Cancellation.</param>
    Task<MemoryGraphExport> ExportAsync(string engine, string taskKey, string? scope, CancellationToken ct = default);

    /// <summary>Insert each node under (<paramref name="engine"/>, <paramref name="taskKey"/>) with the state it
    /// carries, its ages re-anchored to this engine's head, and record its subjects; one result per node, in order.
    /// <para><b>A node whose <see cref="MemoryContentKey"/> identity is already held — or was inserted by an earlier
    /// node of the same call — is left EXACTLY as it is</b>, decay state, grade, metadata and subjects included, and
    /// reported with its id and <see cref="GraphNodeImportResult.Inserted"/> false. Ids are allocated as for any
    /// insert and never reissued.</para></summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="taskKey">The task every node is written under.</param>
    /// <param name="nodes">The nodes, in the order their ids should rise.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<GraphNodeImportResult>> ImportNodesAsync(string engine, string taskKey,
        IReadOnlyList<GraphNodeImport> nodes, CancellationToken ct = default);

    /// <summary>Insert each link with its weight and re-anchored ages, returning how many were written. A link is
    /// written only between two nodes <paramref name="engine"/> holds, never to itself, and never over a link the
    /// store already holds under the same (from, to, kind) — that one keeps its weight and marks.</summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="links">The links, their endpoints already this store's ids.</param>
    /// <param name="ct">Cancellation.</param>
    Task<int> ImportLinksAsync(string engine, IReadOnlyList<GraphLink> links, CancellationToken ct = default);
}

/// <summary>What <see cref="IPortableMemoryGraphStore.ExportAsync"/> read.</summary>
/// <param name="Nodes">The nodes, in id order, projected as any read projects them.</param>
/// <param name="Links">The links leaving them, each endpoint a node of the same engine and task.</param>
/// <param name="Subjects">Each node's subjects, by node id; a node with none is absent.</param>
public sealed record MemoryGraphExport(
    IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphLink> Links,
    IReadOnlyDictionary<long, IReadOnlyList<string>> Subjects);

/// <summary>One directed link as stored: its RAW weight, and how far its engine has moved since it was last
/// strengthened on each scale — the same four ages <see cref="GraphNeighbour"/> reports for an edge.</summary>
/// <param name="From">Source node id.</param>
/// <param name="To">Target node id.</param>
/// <param name="Kind">The relation name; empty for an untyped association.</param>
/// <param name="Weight">The raw weight.</param>
/// <param name="Age">Position moved since it was last strengthened.</param>
/// <param name="OrdinalAge">Writes since then.</param>
/// <param name="VolumeAge">Characters written since then.</param>
/// <param name="ElapsedAge">Real days since then, as of the engine's most recent write.</param>
public sealed record GraphLink(long From, long To, string Kind, double Weight,
    double Age, double OrdinalAge, double VolumeAge, double ElapsedAge);

/// <summary>A node to insert WITH its state, for <see cref="IPortableMemoryGraphStore.ImportNodesAsync"/>. Every field
/// means what the same-named <see cref="GraphNode"/> member means; each age is relative to the head of the engine the
/// node is imported into.</summary>
/// <param name="Scope">Variant scope.</param>
/// <param name="Headline">The one-line form.</param>
/// <param name="Content">The full text; with the scope, the engine and the task, its identity.</param>
/// <param name="Grade">Associative or authoritative; never <see cref="MemoryGrade.Inherit"/>.</param>
/// <param name="CreatedAt">When it was first stored, wherever that was.</param>
/// <param name="RecallCount">How many times it has been recalled.</param>
/// <param name="Stability">Its half-life, in the engine's units.</param>
/// <param name="Difficulty">Its live difficulty.</param>
/// <param name="Signals">Its retention signals; the store derives its promoted salience from them.</param>
/// <param name="ProvenanceRetrievability">Which retrievability policy computed its stability and difficulty.</param>
/// <param name="ProvenanceSalience">Which salience policies produced its signals.</param>
/// <param name="Metadata">App-owned extra data, or null.</param>
/// <param name="Subjects">What it is about; empty records none.</param>
/// <param name="Age">Position behind the head.</param>
/// <param name="OrdinalAge">Writes behind the head.</param>
/// <param name="VolumeAge">Characters behind the head.</param>
/// <param name="ElapsedAge">Real days behind the head's most recent write.</param>
public sealed record GraphNodeImport(
    string Scope, string Headline, string Content, MemoryGrade Grade, DateTimeOffset CreatedAt,
    int RecallCount, double Stability, double Difficulty, MemorySignals Signals,
    long ProvenanceRetrievability, long ProvenanceSalience, IReadOnlyDictionary<string, string>? Metadata,
    IReadOnlyCollection<string> Subjects, double Age, double OrdinalAge, double VolumeAge, double ElapsedAge)
{
    /// <summary>When it started holding, or null. Honoured only by a store implementing
    /// <see cref="ITemporalMemoryGraphStore"/>; the engine refuses to import one into any other.</summary>
    public DateTimeOffset? ValidFrom { get; init; }

    /// <summary>When the writer said it stopped holding, or null; honoured as <see cref="ValidFrom"/> is.</summary>
    public DateTimeOffset? ValidTo { get; init; }

    /// <summary>When a successor took over, or null; honoured as <see cref="ValidFrom"/> is. It lands WITH the
    /// insert, so an import cut short after it leaves the entry ended with no successor named — the safe direction —
    /// and never open. The successor itself is named afterwards through
    /// <see cref="ITemporalMemoryGraphStore.SupersedeAsync"/>, because its id in this store is known only then.</summary>
    public DateTimeOffset? SupersededAt { get; init; }
}

/// <summary>Where one imported node landed: <paramref name="Id"/> in this store, and whether this import
/// <paramref name="Inserted"/> it or found it already held.</summary>
/// <param name="Id">The node's id in this store.</param>
/// <param name="Inserted">False when its identity was already held, and nothing about it changed.</param>
public readonly record struct GraphNodeImportResult(long Id, bool Inserted);

/// <summary>Re-anchoring a relative age against an engine's head — the ONE rule every store applies, so an imported
/// entry sits the same distance behind on every backend.</summary>
public static class MemoryGraphAnchor
{
    /// <summary>The mark <paramref name="age"/> behind <paramref name="head"/> on a count scale.</summary>
    /// <param name="head">Where the engine stands.</param>
    /// <param name="age">How far behind it; a non-finite age reads as zero.</param>
    public static long Behind(long head, double age) =>
        double.IsFinite(age) ? head - (long)Math.Round(age, MidpointRounding.AwayFromZero) : head;

    /// <summary>The mark <paramref name="age"/> behind <paramref name="head"/> on the position.</summary>
    /// <param name="head">Where the engine stands.</param>
    /// <param name="age">How far behind it; a non-finite age reads as zero.</param>
    public static double Behind(double head, double age) => double.IsFinite(age) ? head - age : head;

    /// <summary>The time <paramref name="days"/> before <paramref name="head"/>, clamped to the representable range
    /// and returned in UTC.</summary>
    /// <param name="head">The engine's most recent write.</param>
    /// <param name="days">How many days before it; a non-finite value reads as zero.</param>
    public static DateTimeOffset Behind(DateTimeOffset head, double days)
    {
        var utc = head.ToUniversalTime();
        if (!double.IsFinite(days) || days == 0) return utc;
        var room = (utc - DateTimeOffset.MinValue).TotalDays;
        return days >= room ? DateTimeOffset.MinValue : utc - TimeSpan.FromDays(days);
    }
}
