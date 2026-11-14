using Lyntai.Inference;
namespace Lyntai.Inference.Budgeting;

/// <summary>An OPTIONAL <see cref="IUsageTracker"/> capability that records a spend once per KEY, however often it is
/// reported: a render fetched twice is paid for once. A caller that holds a key for its spend type-tests the registered
/// tracker for this and falls back to <see cref="IUsageTracker.RecordAsync"/> — counting every report — when it is
/// absent. The generation job handlers and <c>generate_fetch</c> key a queued render's bill
/// <c>generation:&lt;backend id&gt;:&lt;operation id&gt;</c>.
/// <para>An implementer must write the key and the spend in ONE atomic step, so racing callers with one key record
/// one entry; compare keys ordinally (an operation id is the backend's, and its case is part of it); keep keys across
/// <see cref="IUsageTracker.ResetAsync"/>, which clears SPEND, so an operation billed before a reset is not billed again
/// after it; and keep every key until <see cref="PruneRecordKeysAsync"/> removes it — nothing expires on its own.</para></summary>
public interface IIdempotentUsageTracker : IUsageTracker
{
    /// <summary>Record <paramref name="usage"/> under <paramref name="consumer"/> unless <paramref name="key"/> was
    /// recorded before, under any consumer.</summary>
    /// <param name="consumer">Whose bucket, as <see cref="IUsageTracker.RecordAsync"/> takes it.</param>
    /// <param name="usage">What the call spent.</param>
    /// <param name="key">The spend's name in the ledger; neither null nor blank.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>Whether this call recorded it: false when the key was already spent.</returns>
    ValueTask<bool> RecordOnceAsync(string consumer, ProviderUsage usage, string key, CancellationToken ct = default);

    /// <summary>Forget every key recorded before <paramref name="before"/>, so the ledger of keys does not grow for
    /// ever. The spend those keys recorded stays. Called by the HOST, at an interval it chooses: a key pruned while
    /// its operation can still be fetched bills that fetch again.</summary>
    /// <param name="before">Keys recorded earlier than this instant are removed.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>How many keys were removed.</returns>
    ValueTask<int> PruneRecordKeysAsync(DateTimeOffset before, CancellationToken ct = default);
}
