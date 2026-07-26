namespace Lyntai.Llm.Streaming;

/// <summary>The inactivity clock a streaming provider arms around each read (design §6): <see cref="Arm"/>
/// immediately before a read, <see cref="Stop"/> immediately after it returns — so the window measures
/// PROVIDER inactivity only, never the time the provider and its consumer spend processing a chunk. A
/// single <c>CancelAfter</c> over a whole enumeration counts consumer dwell and kills slow-but-healthy
/// streams (this bug shipped in two providers). Wraps the provider's linked timeout CTS; the canonical
/// process-based shape is <c>ProcessRunner.StreamLinesAsync</c>.</summary>
public sealed class InactivityClock(CancellationTokenSource cts, TimeSpan timeout)
{
    /// <summary>Start (or re-start) the window — call immediately before each read.</summary>
    public void Arm() => cts.CancelAfter(timeout);

    /// <summary>Suspend the window — call immediately after a read returns, while the chunk is processed.</summary>
    public void Stop() => cts.CancelAfter(Timeout.InfiniteTimeSpan);
}

/// <summary>
/// The provider streaming read-loop in ONE place — every streaming provider used to hand-roll the same
/// guarded loop (arm the inactivity clock, read one item, stop the clock, rethrow caller cancellation,
/// map any other fault to a terminal, yield), and that duplication is exactly where the wall-clock bug
/// shipped twice. <see cref="ReadAll"/> yields each item as <c>(Item, null)</c>, a clean end-of-stream by
/// completing, and a mapped fault as a final <c>(null, Terminal)</c> — the caller yields its terminal and
/// stops. Ownership stays with the caller: it disposes its own source enumerator/reader, keeps its own
/// clock topology (pass <paramref name="clock"/> only when the provider owns the timeout; CLI providers
/// delegate theirs to <c>ProcessRunner</c> and pass none), and decides per-exception whether to map or
/// propagate.
/// </summary>
public static class GuardedStream
{
    /// <summary>Run the guarded read loop over <paramref name="readNext"/> (return null = clean end of
    /// stream). An <see cref="OperationCanceledException"/> with <paramref name="callerCt"/> cancelled
    /// ALWAYS rethrows (a cancelled caller must never receive a fabricated terminal). Any other exception
    /// goes to <paramref name="onFault"/>: return the terminal to yield, or null to PROPAGATE the
    /// exception unchanged (e.g. a provider-side <see cref="OperationCanceledException"/> the router
    /// handles). <paramref name="clock"/>, when supplied, is armed before and stopped after each read.</summary>
    public static async IAsyncEnumerable<(TItem? Item, TTerminal? Terminal)> ReadAll<TItem, TTerminal>(
        Func<ValueTask<TItem?>> readNext,
        Func<Exception, TTerminal?> onFault,
        CancellationToken callerCt,
        InactivityClock? clock = null)
        where TItem : class
        where TTerminal : class
    {
        while (true)
        {
            TItem? item;
            TTerminal? terminal = null;
            try
            {
                clock?.Arm();
                item = await readNext().ConfigureAwait(false);
                clock?.Stop();
            }
            catch (OperationCanceledException) when (callerCt.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                terminal = onFault(ex);
                if (terminal is null) throw; // the caller chose to propagate this one unchanged
                item = null;
            }

            if (terminal is not null)
            {
                yield return (null, terminal);
                yield break;
            }
            if (item is null) yield break; // clean end of stream
            yield return (item, null);
        }
    }
}
