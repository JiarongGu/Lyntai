namespace Lyntai.Memory;

/// <summary>How an entry is ended by <see cref="IEndableMemory.EndAsync"/>.</summary>
/// <param name="Reason">Why it ended — required, read back as <see cref="MemoryValidity.EndedReason"/>.</param>
public sealed record MemoryEnding(string Reason)
{
    /// <summary>When it stopped holding; null is now by the engine's clock, or the successor's start where there is one.</summary>
    public DateTimeOffset? At { get; init; }

    /// <summary>The entry of THIS engine that replaced it, or null for an end that names none. A successor records the
    /// supersession; none states the entry's own <see cref="MemoryValidity.To"/>.</summary>
    public MemoryRef? Successor { get; init; }

    /// <summary>The <see cref="MemoryItem.Revision"/> the end was reviewed on, or null for an unconditional end. A
    /// store that cannot hold one makes the call throw <see cref="NotSupportedException"/> rather than end anyway.</summary>
    public string? ExpectedRevision { get; init; }
}

/// <summary>What <see cref="IEndableMemory.EndAsync"/> did.</summary>
public enum MemoryEndOutcome
{
    /// <summary>The entry is now ended.</summary>
    Ended,
    /// <summary>An end was already recorded; nothing changed and its first reason stands.</summary>
    AlreadyEnded,
    /// <summary>The entry is not at the expected revision; nothing changed and the end needs a fresh review.</summary>
    Changed,
}

/// <summary>The outcome of one end.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Revision">The entry's revision after the call — for <see cref="MemoryEndOutcome.Changed"/>, the one found,
/// which is what a fresh review reads.</param>
public readonly record struct MemoryEndResult(MemoryEndOutcome Outcome, string? Revision);

/// <summary>OPTIONAL capability: an engine that ends one entry by reference with a reason, which keeps it whole under
/// <see cref="MemoryValidityReading.History"/> and takes it out of <see cref="MemoryValidityReading.Current"/>.
/// <para>An end is valid-time state beside the decay seam: no recall, expansion or reinforcement restores it, and a
/// re-remember that states no validity keeps it. A composite ALWAYS implements this and routes by
/// <see cref="MemoryRef.Engine"/>; where the owning member does not, the call throws, because a dropped end leaves a
/// stale fact current.</para></summary>
public interface IEndableMemory
{
    /// <summary>End <paramref name="entry"/>. Refuses, as a supersession does, a reference outside this engine or task,
    /// an end before the entry began, and a different successor than the one it already has.</summary>
    /// <param name="taskKey">The task the entry must belong to.</param>
    /// <param name="entry">The entry to end.</param>
    /// <param name="ending">Why, when, by what, and on which revision.</param>
    /// <param name="ct">Cancellation, which is never swallowed.</param>
    /// <exception cref="ArgumentException">A blank reason, or a reference that names no usable entry.</exception>
    /// <exception cref="InvalidOperationException">The entry already has another successor.</exception>
    /// <exception cref="NotSupportedException">The store cannot end an entry with a reason.</exception>
    Task<MemoryEndResult> EndAsync(string taskKey, MemoryRef entry, MemoryEnding ending, CancellationToken ct = default);
}
