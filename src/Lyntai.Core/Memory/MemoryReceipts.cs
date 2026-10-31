namespace Lyntai.Memory;

/// <summary>Which call a <see cref="MemoryReceipt"/> answers.</summary>
public enum MemoryOperationKind
{
    /// <summary><see cref="IMemoryEngine.RememberAsync"/>.</summary>
    Remember,
    /// <summary><see cref="IKeyedMemory.RelateKeyedAsync"/>.</summary>
    Relate,
    /// <summary><see cref="IKeyedMemory.ConfirmKeyedAsync"/>, whichever way the check concluded.</summary>
    Confirm,
    /// <summary><see cref="IEndableMemory.EndAsync"/>.</summary>
    End,
}

/// <summary>What a keyed call did, as its receipt keeps it.</summary>
public enum MemoryOperationOutcome
{
    /// <summary>A remember stored an entry that was not held.</summary>
    Created,
    /// <summary>A remember landed on an entry already held — another writer's content, or the same caller's earlier one.</summary>
    Refreshed,
    /// <summary>Every other call that took effect.</summary>
    Applied,
}

/// <summary>What a keyed call that took effect left behind: the answer a caller who lost the reply can read back.
/// <para>Written in the effect's own transaction, so it exists exactly when the effect does. A call that changed nothing — a
/// stale revision, an entry already ended — leaves none.</para></summary>
/// <param name="Key">The caller's key.</param>
/// <param name="Kind">The call it answers.</param>
/// <param name="Reference">The entry the call concerned — the stored one for a remember, the asserting one for a relation, the
/// successor for a supersession.</param>
/// <param name="Revision">That entry's revision after the call, or null where the store holds none.</param>
/// <param name="Outcome">What the call did.</param>
/// <param name="At">When it took effect, by the engine's clock.</param>
/// <param name="ExpiresAt">When the receipt stops answering. A repeat after it is a new call, and the revision guard answers it.</param>
public sealed record MemoryReceipt(
    string Key, MemoryOperationKind Kind, MemoryRef? Reference, string? Revision, MemoryOperationOutcome Outcome,
    DateTimeOffset At, DateTimeOffset ExpiresAt)
{
    /// <summary>True when this call found the receipt and applied nothing; false on the call that took effect.</summary>
    public bool Replayed { get; init; }
}

/// <summary>A key already answered for a different call: the same (engine, task, key) with another kind or other content.
/// Nothing was applied. A key names one call, so a caller that reuses one for a different call has a defect, not a retry.</summary>
public sealed class MemoryKeyConflictException : InvalidOperationException
{
    /// <summary>Refuses <paramref name="key"/>, which already answered a <paramref name="answered"/> call in <paramref name="taskKey"/>.</summary>
    /// <param name="key">The key that was reused.</param>
    /// <param name="taskKey">The task it is scoped to.</param>
    /// <param name="answered">The call its receipt answers.</param>
    public MemoryKeyConflictException(string key, string taskKey, MemoryOperationKind answered)
        : base($"The operation key '{key}' already answered a {answered} call with other content in task '{taskKey}'; a key names one " +
               "call. Nothing was applied.") => Key = key;

    /// <summary>The key that was reused.</summary>
    public string Key { get; }
}

/// <summary>OPTIONAL capability: an engine that makes a write answerable. A call carrying a caller's key leaves a
/// <see cref="MemoryReceipt"/> in its own transaction, and a repeat with that key answers the receipt — moves no revision,
/// adds no weight, stamps nothing — so a caller who lost the reply settles it by asking rather than by guessing
/// (<c>docs/DECISIONS.md</c> D242).
/// <para>The key is opaque, at most <see cref="MemoryLimits.MaxOperationKeyChars"/> characters, and scoped to (engine, task).
/// A remember and an end carry it on <see cref="MemoryWrite.OperationKey"/> and <see cref="MemoryEnding.OperationKey"/>; a
/// supersession on <c>MemorySupersession.OperationKey</c>; a relation and a confirmation take it here. An engine over a
/// store that cannot hold receipts REFUSES a keyed call, because a dropped key is a write nobody can settle.</para>
/// <para>A composite ALWAYS implements this and routes by <see cref="MemoryRef.Engine"/>.</para></summary>
public interface IKeyedMemory
{
    /// <summary>Whether a keyed call would be kept. A graph engine reports whether its store implements
    /// <see cref="IKeyedMemoryGraphStore"/>.</summary>
    bool HoldsReceipts { get; }

    /// <summary>The receipt a key left in <paramref name="taskKey"/>, or null when there is none or it has expired. Reads without
    /// effect: nothing is reinforced and no receipt is purged.</summary>
    /// <param name="taskKey">The task the key is scoped to.</param>
    /// <param name="key">The caller's key.</param>
    /// <param name="ct">Cancellation, which is never swallowed.</param>
    /// <exception cref="NotSupportedException">The store cannot hold receipts.</exception>
    Task<MemoryReceipt?> ReceiptAsync(string taskKey, string key, CancellationToken ct = default);

    /// <summary>As <see cref="IRelatableMemory.RelateAsync"/>, once per key: a repeat adds no weight and answers the receipt.</summary>
    /// <param name="taskKey">The task both entries must belong to.</param>
    /// <param name="from">The asserting entry.</param>
    /// <param name="to">The entry it asserts the relation of.</param>
    /// <param name="relation">What is asserted.</param>
    /// <param name="operationKey">The caller's key.</param>
    /// <param name="ct">Cancellation, which is never swallowed.</param>
    /// <exception cref="MemoryKeyConflictException">The key answered a different call.</exception>
    /// <exception cref="NotSupportedException">The store cannot hold receipts, or a typed link.</exception>
    Task<MemoryReceipt> RelateKeyedAsync(string taskKey, MemoryRef from, MemoryRef to, MemoryRelation relation, string operationKey,
        CancellationToken ct = default);

    /// <summary>As <see cref="IConfirmableMemory.ConfirmAsync"/>, once per key: a repeat stamps nothing and reports
    /// <see cref="MemoryConfirmOutcome.Unchanged"/> (held) or <see cref="MemoryConfirmOutcome.AlreadyEnded"/> (failed), carrying the
    /// receipt in <see cref="MemoryConfirmResult.Receipt"/>.</summary>
    /// <param name="taskKey">The task the entry must belong to.</param>
    /// <param name="entry">The entry the check was for.</param>
    /// <param name="outcome">What the check concluded.</param>
    /// <param name="operationKey">The caller's key.</param>
    /// <param name="at">When it concluded; null is now by the engine's clock.</param>
    /// <param name="note">Why; required for <see cref="MemoryCheckOutcome.Failed"/>.</param>
    /// <param name="expectedRevision">The revision the due read returned.</param>
    /// <param name="ct">Cancellation, which is never swallowed.</param>
    /// <exception cref="MemoryKeyConflictException">The key answered a different call.</exception>
    /// <exception cref="NotSupportedException">The store cannot hold receipts, or a confirmation.</exception>
    Task<MemoryConfirmResult> ConfirmKeyedAsync(string taskKey, MemoryRef entry, MemoryCheckOutcome outcome, string operationKey,
        DateTimeOffset? at = null, string? note = null, string? expectedRevision = null, CancellationToken ct = default);
}

/// <summary>The refusal an engine that cannot hold a receipt gives a keyed write — one wording, as
/// <see cref="MemoryValidityRefusal"/> is for valid time. A key silently dropped is a write nobody can settle.</summary>
internal static class MemoryKeyRefusal
{
    public static void ThrowIfStated(MemoryWrite write, string engine)
    {
        if (write.OperationKey is not null)
            throw new NotSupportedException(
                $"Memory engine '{engine}' cannot hold a receipt, so a write carrying an operation key could not be settled after a lost " +
                "answer. Route it to a graph engine over a store that keeps receipts, or send it without a key.");
    }
}
