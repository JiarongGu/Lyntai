namespace Lyntai.Memory;

/// <summary>What a host's check of a stored fact concluded.</summary>
public enum MemoryCheckOutcome
{
    /// <summary>The check held: the entry is stamped as confirmed. Nothing else about it moves, so it is not a reinforcement.</summary>
    Held,
    /// <summary><b>The host confirmed that the named claim no longer holds</b>, and nothing weaker: the entry ends at the confirmation,
    /// with the host's note as its reason. A run that did not finish — a timeout, a missing access, an ambiguous answer — is reported
    /// by calling nothing, so the entry stays due and the library records no state for it (<c>docs/DECISIONS.md</c> D241).</summary>
    Failed,
}

/// <summary>What <see cref="IConfirmableMemory.ConfirmAsync"/> did.</summary>
public enum MemoryConfirmOutcome
{
    /// <summary>A <see cref="MemoryCheckOutcome.Held"/> stamp moved forward.</summary>
    Stamped,
    /// <summary>The entry already carries a stamp at that instant or later; nothing changed.</summary>
    Unchanged,
    /// <summary>A <see cref="MemoryCheckOutcome.Failed"/> check ended the entry.</summary>
    Ended,
    /// <summary>The entry was already ended; nothing more was written.</summary>
    AlreadyEnded,
    /// <summary>The entry is not at the expected revision; nothing changed, and the confirmation needs a fresh review.</summary>
    Changed,
}

/// <summary>The outcome of one confirmation.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Revision">The entry's revision after the call — for <see cref="MemoryConfirmOutcome.Changed"/>, the one found.</param>
public readonly record struct MemoryConfirmResult(MemoryConfirmOutcome Outcome, string? Revision);

/// <summary>OPTIONAL capability: a host records what its own check of a stored fact concluded, and asks what is due for one.
/// <para><b>The library runs no check and ends no entry by age.</b> An entry carries an opaque <see cref="MemoryWrite.Recheck"/> text
/// the library never reads; the host decides when a fact is stale (<c>docs/DECISIONS.md</c> D240). <b>A due sweep is eventual repair, not
/// proof of currency at presentation</b>: a caller that presents cached material as current checks the revision then.</para></summary>
public interface IConfirmableMemory
{
    /// <summary>Record a check's conclusion. <see cref="MemoryCheckOutcome.Held"/> stamps <see cref="MemoryItem.ConfirmedAt"/>, never
    /// backwards; <see cref="MemoryCheckOutcome.Failed"/> ends the entry through <see cref="IEndableMemory.EndAsync"/>, at
    /// <paramref name="at"/> (the engine's clock when null) and never an earlier instant, with <paramref name="note"/> as its required reason.</summary>
    /// <param name="taskKey">The task the entry must belong to.</param>
    /// <param name="entry">The entry the check was for.</param>
    /// <param name="outcome">What the check concluded.</param>
    /// <param name="at">When it concluded; null is now by the engine's clock.</param>
    /// <param name="note">Why; required for <see cref="MemoryCheckOutcome.Failed"/>.</param>
    /// <param name="expectedRevision">The <see cref="MemoryItem.Revision"/> the due read returned; the call applies only if it is still current.</param>
    /// <param name="ct">Cancellation, which is never swallowed.</param>
    /// <exception cref="ArgumentException">A reference that names no usable entry, or a failed check with no note.</exception>
    /// <exception cref="NotSupportedException">The store cannot hold a confirmation.</exception>
    Task<MemoryConfirmResult> ConfirmAsync(string taskKey, MemoryRef entry, MemoryCheckOutcome outcome,
        DateTimeOffset? at = null, string? note = null, string? expectedRevision = null, CancellationToken ct = default);

    /// <summary>The live entries of the task and scope that carry a re-check and were last confirmed before
    /// <paramref name="notConfirmedSince"/> or never, never-confirmed first, each carrying its <see cref="MemoryItem.Recheck"/> and
    /// <see cref="MemoryItem.Revision"/>. Reads without reinforcing anything.</summary>
    /// <param name="taskKey">The task to read.</param>
    /// <param name="scope">The scope, or null for every scope of the task.</param>
    /// <param name="notConfirmedSince">An entry confirmed at or after this is not due.</param>
    /// <param name="limit">The most entries; zero or less returns none.</param>
    /// <param name="ct">Cancellation, which is never swallowed.</param>
    Task<IReadOnlyList<MemoryItem>> DueAsync(string taskKey, string? scope, DateTimeOffset notConfirmedSince, int limit = 50,
        CancellationToken ct = default);
}
