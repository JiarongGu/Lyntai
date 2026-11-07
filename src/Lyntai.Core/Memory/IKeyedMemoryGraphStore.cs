namespace Lyntai.Memory;

/// <summary>The caller's key for one store call, with what the receipt it leaves must record.</summary>
/// <param name="Key">The caller's opaque key, scoped to (engine, <paramref name="TaskKey"/>).</param>
/// <param name="TaskKey">The task the key is scoped to.</param>
/// <param name="Kind">The call.</param>
/// <param name="Fingerprint">An opaque digest of everything the call stated; two calls with one key and another fingerprint are
/// refused with <see cref="MemoryKeyConflictException"/>.</param>
/// <param name="At">When the call is made — the clock the receipt is stamped and its expiry judged by.</param>
/// <param name="ExpiresAt">When the receipt stops answering.</param>
public sealed record GraphOperationKey(
    string Key, string TaskKey, MemoryOperationKind Kind, string Fingerprint, DateTimeOffset At, DateTimeOffset ExpiresAt);

/// <summary>A receipt as a store holds it.</summary>
/// <param name="Key">The caller's key.</param>
/// <param name="TaskKey">The task it is scoped to.</param>
/// <param name="Kind">The call.</param>
/// <param name="Fingerprint">The digest the call was made with.</param>
/// <param name="NodeId">The entry the call concerned. Removing it removes the receipt.</param>
/// <param name="Revision">That entry's revision after the call.</param>
/// <param name="Outcome">What the call did.</param>
/// <param name="At">When it took effect.</param>
/// <param name="ExpiresAt">When it stops answering.</param>
public sealed record GraphReceipt(
    string Key, string TaskKey, MemoryOperationKind Kind, string Fingerprint, long NodeId, long Revision,
    MemoryOperationOutcome Outcome, DateTimeOffset At, DateTimeOffset ExpiresAt);

/// <summary>What a keyed store call returned.</summary>
/// <typeparam name="T">The un-keyed call's own result.</typeparam>
/// <param name="Value">The result. For a repeat it is rebuilt from the receipt, not recomputed.</param>
/// <param name="Replayed">Whether the key had already answered: nothing was applied.</param>
/// <param name="Receipt">The key's receipt, or null when the call took no effect and so left none.</param>
public readonly record struct GraphKeyed<T>(T Value, bool Replayed, GraphReceipt? Receipt);

/// <summary>OPTIONAL half of <see cref="IMemoryGraphStore"/> that makes a write answerable (<c>docs/DECISIONS.md</c> D242): each
/// keyed member does what its un-keyed twin does and writes the key's <see cref="GraphReceipt"/> in the SAME transaction (or
/// critical section, or journal batch), so the receipt exists exactly when the effect does.
/// <para>Per keyed call, in this order: expired receipts of the engine are purged; a live receipt with the same fingerprint
/// answers (<see cref="GraphKeyed{T}.Replayed"/>) and nothing else runs; one with another fingerprint or kind throws
/// <see cref="MemoryKeyConflictException"/>; otherwise the effect runs and, if it took effect, the receipt is written. A call that
/// changed nothing writes none. Two racing calls with one key land once: the loser's effect is not applied and it answers the
/// winner's receipt.</para>
/// <para>The portable line does not carry receipts. Removing an entry (a prune, a delete, a forget) removes the receipts that concern it. Implementing this owes
/// <see cref="IConfirmableMemoryGraphStore"/> and <see cref="ITypedLinkMemoryGraphStore"/>, whose calls it keys.</para></summary>
public interface IKeyedMemoryGraphStore : IConfirmableMemoryGraphStore, ITypedLinkMemoryGraphStore
{
    /// <summary>The key's live receipt, or null — one expired at <paramref name="now"/> reads as none. Writes and purges nothing.</summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="taskKey">The task the key is scoped to.</param>
    /// <param name="key">The caller's key.</param>
    /// <param name="now">The instant liveness is judged at.</param>
    /// <param name="ct">Cancellation.</param>
    Task<GraphReceipt?> ReceiptAsync(string engine, string taskKey, string key, DateTimeOffset now, CancellationToken ct = default);

    /// <summary><see cref="IMemoryGraphStore.UpsertAsync"/> under a key. The receipt records the entry's id, its revision after, and
    /// <see cref="MemoryOperationOutcome.Created"/> when the identity was not held, else
    /// <see cref="MemoryOperationOutcome.Refreshed"/>.</summary>
    /// <param name="write">The node to store.</param>
    /// <param name="key">The key, whose kind is <see cref="MemoryOperationKind.Remember"/>.</param>
    /// <param name="ct">Cancellation.</param>
    Task<GraphKeyed<long>> UpsertKeyedAsync(GraphNodeWrite write, GraphOperationKey key, CancellationToken ct = default);

    /// <summary><see cref="IMemoryGraphStore.LinkAsync"/> of one directed edge under a key. <see cref="GraphKeyed{T}.Value"/> is
    /// whether an edge was written; an endpoint the engine does not hold writes none and leaves no receipt. The receipt concerns the
    /// source alone, so removing the target drops the edge and keeps a receipt that replays; a caller that cares checks the target.</summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="from">Source node id.</param>
    /// <param name="to">Target node id.</param>
    /// <param name="kind">The relation name.</param>
    /// <param name="weight">How much to add.</param>
    /// <param name="key">The key, whose kind is <see cref="MemoryOperationKind.Relate"/>.</param>
    /// <param name="ct">Cancellation.</param>
    Task<GraphKeyed<bool>> LinkKeyedAsync(string engine, long from, long to, string kind, double weight, GraphOperationKey key,
        CancellationToken ct = default);

    /// <summary><see cref="IConfirmableMemoryGraphStore.ConfirmAsync"/> under a key. A receipt is written only for
    /// <see cref="GraphConfirmOutcome.Stamped"/>; a repeat reports <see cref="GraphConfirmOutcome.Unchanged"/> with the receipt's
    /// revision.</summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="confirm">The confirmation.</param>
    /// <param name="key">The key, whose kind is <see cref="MemoryOperationKind.Confirm"/>.</param>
    /// <param name="ct">Cancellation.</param>
    Task<GraphKeyed<GraphConfirmResult>> ConfirmKeyedAsync(string engine, GraphConfirm confirm, GraphOperationKey key,
        CancellationToken ct = default);

    /// <summary><see cref="IEndableMemoryGraphStore.EndAsync"/> under a key. A receipt is written only for
    /// <see cref="GraphEndOutcome.Ended"/>; a repeat reports <see cref="GraphEndOutcome.AlreadyEnded"/> with the receipt's revision,
    /// whatever revision the repeat expected. The key's kind is <see cref="MemoryOperationKind.End"/>, or
    /// <see cref="MemoryOperationKind.Confirm"/> for a failed check.</summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="end">The end.</param>
    /// <param name="key">The key.</param>
    /// <param name="ct">Cancellation.</param>
    Task<GraphKeyed<GraphEndResult>> EndKeyedAsync(string engine, GraphEnd end, GraphOperationKey key, CancellationToken ct = default);
}
