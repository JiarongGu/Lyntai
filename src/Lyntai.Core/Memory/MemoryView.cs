namespace Lyntai.Memory;

/// <summary>OPTIONAL capability: an engine that can hand out a read-only view of ONE task, so a second host reads the facts another
/// host owns and writes without copying them (<c>docs/DECISIONS.md</c> D242, <c>docs/memory.md</c> §9).
/// <para>A composite ALWAYS implements this; <see cref="HoldsView"/> says whether a member does, and <see cref="ViewOf"/> refuses
/// where none does.</para></summary>
public interface IViewableMemory
{
    /// <summary>Whether <see cref="ViewOf"/> would answer. A graph engine reports whether its store keeps revisions
    /// (<see cref="IEndableMemoryGraphStore"/>); a lexical, semantic or curated engine holds none.</summary>
    bool HoldsView { get; }

    /// <summary>A view of <paramref name="taskKey"/>. It reads what the owner wrote and moves nothing of the owner's.</summary>
    /// <param name="taskKey">The one task the view reads.</param>
    /// <param name="check">Asked once per expansion, before anything is read; null permits every reference of the task.</param>
    /// <exception cref="NotSupportedException">The engine holds no view.</exception>
    IMemoryView ViewOf(string taskKey, IMemoryViewCheck? check = null);
}

/// <summary>A read-only view of one task. <b>It has no write path in its type</b>: nothing here remembers, ends, confirms, relates,
/// links or forgets, and a read through it reinforces nothing, co-activates nothing and ages nothing — the owner's use stamp,
/// confirmation, links and decay are as they were. Every result is a reference into the owner's engine, never a copy.
/// <para>A reference outside <see cref="TaskKey"/> reads as a missing one, so a refusal reveals nothing. An owner's end is read at the
/// next call, because nothing is cached.</para></summary>
public interface IMemoryView
{
    /// <summary>The one task this view reads.</summary>
    string TaskKey { get; }

    /// <summary>The names of the engines whose entries this view returns — what <see cref="MemoryRef.Engine"/> carries on its
    /// results, and what a blend routes by.</summary>
    IReadOnlyList<string> Engines { get; }

    /// <summary>Recall within the task, as <see cref="IMemoryEngine.RecallAsync"/> does but without learning.
    /// <b>Fails open</b> as a recall does.</summary>
    /// <param name="query">What to recall.</param>
    /// <param name="ct">Cancellation, which is never swallowed.</param>
    Task<MemoryViewRecall> RecallAsync(MemoryViewQuery query, CancellationToken ct = default);

    /// <summary>Expand one entry as <see cref="IExpandableMemory.ExpandAsync(MemoryRef, int, int?, MemoryDetail, MemoryValidityReading, CancellationToken)"/>
    /// does, without learning. The view's check is asked about <paramref name="reference"/> FIRST, and a refusal reads nothing; the
    /// named entry comes back whatever its validity, and its neighbours are not asked about.</summary>
    /// <param name="reference">The entry to expand; empty result where it is refused, missing, another engine's or in another task.</param>
    /// <param name="hops">How far to walk from it.</param>
    /// <param name="charBudget">Maximum characters; null takes the engine's configured budget.</param>
    /// <param name="detail">How much of each neighbour to return.</param>
    /// <param name="validity">Which neighbours to admit.</param>
    /// <param name="ct">Cancellation, which is never swallowed.</param>
    Task<MemoryViewRecall> ExpandAsync(MemoryRef reference, int hops = 1, int? charBudget = null,
        MemoryDetail detail = MemoryDetail.Headline, MemoryValidityReading validity = default, CancellationToken ct = default);

    /// <summary>The entry's revision (<see cref="MemoryItem.Revision"/>), read by id alone. Moves nothing and walks nothing. Null where
    /// the reference is missing, outside the task, another engine's, or its store holds no revision; compare it for equality only.</summary>
    /// <param name="reference">The entry.</param>
    /// <param name="ct">Cancellation, which is never swallowed.</param>
    Task<string?> RevisionOfAsync(MemoryRef reference, CancellationToken ct = default);
}

/// <summary>A host's per-reference check before a view expands it. A call, not a grant: the library keeps no owners, grants or log,
/// so who may read what is whatever the host answers here. A check that throws propagates and nothing was read.</summary>
public interface IMemoryViewCheck
{
    /// <summary>Whether <paramref name="reference"/> may be expanded through the view.</summary>
    /// <param name="reference">The entry about to be read.</param>
    /// <param name="ct">Cancellation.</param>
    Task<bool> PermitsAsync(MemoryRef reference, CancellationToken ct = default);
}

/// <summary>What a view recalls: <see cref="MemoryQuery"/> without a task, which the view fixes.</summary>
/// <param name="Query">Optional relevance query; null or whitespace recalls the most recent.</param>
/// <param name="Scope">Optional scope filter; null recalls across the task's scopes.</param>
/// <param name="Limit">Maximum items; null takes the engine's default.</param>
/// <param name="CharBudget">Maximum characters; null takes the engine's configured budget.</param>
/// <param name="RankingPolicyName">As <see cref="MemoryQuery.RankingPolicyName"/>.</param>
/// <param name="Detail">As <see cref="MemoryQuery.Detail"/>.</param>
public sealed record MemoryViewQuery(
    string? Query = null,
    string? Scope = null,
    int? Limit = null,
    int? CharBudget = null,
    string? RankingPolicyName = null,
    MemoryDetail Detail = MemoryDetail.Headline)
{
    /// <summary>Which entries to admit by valid time; the default reads what holds now. The owner's end is seen here at the next call.</summary>
    public MemoryValidityReading Validity { get; init; }
}

/// <summary>Whether an entry holds at the instant a view was read.</summary>
public enum MemoryStanding
{
    /// <summary>It holds, or carries no valid time.</summary>
    Current = 0,

    /// <summary>It stopped holding — superseded or ended by the owner. Seen only in a <see cref="MemoryValidityReading.History"/> or
    /// earlier reading, or as the named entry of an expansion.</summary>
    Ended = 1,

    /// <summary>Its valid time has not begun.</summary>
    NotYetValid = 2,
}

/// <summary>One result of a view: the owner's entry by reference, with the task it came from and where it stands.</summary>
/// <param name="Item">The entry: its <see cref="MemoryItem.Reference"/>, opaque <see cref="MemoryItem.Revision"/>, in-page
/// <see cref="MemoryItem.Relations"/> and <see cref="MemoryItem.Validity"/> (who superseded it, why it ended).</param>
/// <param name="SourceTask">The task it was read from — the view's own.</param>
/// <param name="Standing">Its retirement standing at the engine's clock when it was read.</param>
public sealed record MemoryViewItem(MemoryItem Item, string SourceTask, MemoryStanding Standing);

/// <summary>What a view returned.</summary>
/// <param name="Items">Hits, most relevant first; for an expansion, the named entry first.</param>
/// <param name="Ran">Which tiers produced them, as <see cref="MemoryRecall.Ran"/>.</param>
public sealed record MemoryViewRecall(IReadOnlyList<MemoryViewItem> Items, MemorySources Ran)
{
    /// <summary>Nothing, from no tier.</summary>
    public static MemoryViewRecall Empty { get; } = new([], MemorySources.None);
}

/// <summary>The refusal an engine that holds no view gives <see cref="IViewableMemory.ViewOf"/> — one wording.</summary>
internal static class MemoryViewRefusal
{
    public static NotSupportedException For(string engine) =>
        new($"Memory engine '{engine}' holds no read-only view: it needs a graph member over a store that keeps revisions " +
            "(IEndableMemoryGraphStore). Nothing was read.");
}
