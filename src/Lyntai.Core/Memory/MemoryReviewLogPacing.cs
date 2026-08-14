namespace Lyntai.Memory;

/// <summary>
/// Shared pacing for every <see cref="IMemoryGraphStore.RecordReviewsAsync"/> implementation, so bounded
/// eviction is a per-write <c>DELETE</c> on NONE of them (design spec §3, 2026-08-11 fsrs-properly plan
/// Task 3) — the write-amplification the cap itself exists to prevent, arriving through the mechanism meant
/// to enforce it.
/// <para><b>The strategy.</b> Each store keeps an in-process, per-engine counter of how many review rows it
/// has written since the process started (never persisted — see the trade-off below) and issues the trim
/// <c>DELETE</c> only when writing this batch carries that counter across a <see cref="TrimInterval"/>
/// boundary. Deciding "was a boundary crossed" is a single integer-division comparison, no query — the
/// actual trim statement, the one operation with a real cost, runs roughly once every
/// <see cref="TrimInterval"/> rows rather than once per write.</para>
/// <para><b>The trade-off, stated once for all three backends rather than three times.</b> This makes the
/// cap SOFT, not exact: a busy engine can transiently hold up to <c>cap + TrimInterval(cap) - 1</c> rows
/// between trims, and because the counter lives in memory, a process restart resets it — so the log can grow
/// further still before the next trim catches up. Neither costs correctness: the trim statement itself
/// always computes "keep the newest <c>cap</c> rows for this engine" from the table's actual contents, never
/// from the counter's own belief about them, so whichever process's counter fires next still converges the
/// row count back toward the cap. What varies is only WHEN a trim happens, never whether the table
/// eventually bounds — acceptable for a log whose job is giving a future fitting task something to read,
/// not enforcing an exact row budget.</para>
/// </summary>
public static class MemoryReviewLogPacing
{
    /// <summary>How many rows accumulate, for one engine, between trim attempts — a TENTH of
    /// <paramref name="cap"/>, floored at 1. A large cap amortizes the trim's cost across many writes; a tiny
    /// one (as a test configures, to observe eviction without writing thousands of rows) still trims
    /// promptly enough to be observable, at 1-in-1 or close to it.</summary>
    /// <param name="cap">The configured retention cap. Clamped to zero or above before dividing.</param>
    public static int TrimInterval(int cap) => Math.Max(1, Math.Max(0, cap) / 10);

    /// <summary>Whether writing <paramref name="written"/> more rows for one engine, on top of
    /// <paramref name="before"/> already written this process, crosses a <see cref="TrimInterval"/> boundary
    /// — the one thing a store needs to decide whether THIS batch issues the trim statement. A pure integer
    /// comparison: no query, so every write pays this even when it returns false.</summary>
    /// <param name="before">This engine's own review-write counter immediately before this batch.</param>
    /// <param name="written">How many rows this batch is about to write. Non-positive never crosses.</param>
    /// <param name="cap">The configured retention cap, feeding <see cref="TrimInterval"/>.</param>
    public static bool CrossesBoundary(long before, int written, int cap)
    {
        if (written <= 0) return false;
        var interval = TrimInterval(cap);
        return before / interval != (before + written) / interval;
    }
}
