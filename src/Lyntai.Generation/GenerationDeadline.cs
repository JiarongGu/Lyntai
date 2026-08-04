namespace Lyntai.Generation.Providers;

/// <summary>The per-call deadline every HTTP generation backend runs under, in one place.
///
/// <para>It exists because the <c>Add*</c> shims give their named client
/// <see cref="Timeout.InfiniteTimeSpan"/> — correctly, since a render routinely outlives
/// <see cref="HttpClient"/>'s 100-second default — which is only safe if something ELSE bounds the call. Without
/// it, a backend that accepts the connection and then stalls hangs until the caller's token fires, and a
/// background render with no cancel waits forever: unbounded and silent, which is worse than the cut-off it
/// replaced.</para>
///
/// <para><b>The subtle part is telling the two clocks apart.</b> A fired deadline must surface as a
/// <see cref="GenerationVerdict.Timeout"/> RESULT, because <see cref="IGenerationProvider"/> is contractually
/// fail-safe (a transport or backend failure is a verdict, never a throw), while the CALLER's cancellation must
/// still propagate as an <see cref="OperationCanceledException"/>. Both arrive here as the same exception type,
/// so the discriminator is the caller's own token: if <c>ct</c> is cancelled the caller asked to stop and the
/// exception is theirs; otherwise the only clocks left are ours and the client's, and both mean "timed out".
/// This mirrors the LLM side's idiom exactly (<c>OpenAiCompatibleProvider.CompleteAsync</c>) — one rule for both
/// domains rather than two that drift.</para>
///
/// <para>A caller who cancels at the same instant the deadline fires gets cancellation, not a timeout: the
/// caller's intent is the more specific statement.</para>
///
/// <para><b>One consequence worth knowing when you catch that exception:</b> its
/// <see cref="OperationCanceledException.CancellationToken"/> is the LINKED token this helper built, not the
/// one the caller passed in — so <c>catch (OperationCanceledException e) when (e.CancellationToken == myToken)</c>
/// will not match. Test the token itself (<c>myToken.IsCancellationRequested</c>), which is what this helper
/// does.</para></summary>
internal static class GenerationDeadline
{
    /// <summary>Resolve one call's budget: an explicit <see cref="GenerationRequest.TimeoutSeconds"/> wins
    /// (the most specific thing a caller can say), else the backend's configured default. A non-positive
    /// request value is not a budget and is ignored — the same rule as
    /// <c>LyntaiOptions.ResolveTimeout</c>.</summary>
    /// <param name="requestSeconds">The request's own budget, if it carries one.</param>
    /// <param name="configured">The backend option's default.</param>
    internal static TimeSpan Resolve(int? requestSeconds, TimeSpan configured) =>
        requestSeconds is { } seconds && seconds > 0 ? TimeSpan.FromSeconds(seconds) : configured;

    /// <summary>Run <paramref name="call"/> under <paramref name="budget"/>, composed WITH the caller's token
    /// rather than replacing it. Returns whatever the call returns; on a fired deadline returns
    /// <paramref name="timedOut"/>'s value instead, and on the caller's cancellation rethrows.</summary>
    /// <typeparam name="T">The call's result shape — a result, an operation, a probe.</typeparam>
    /// <param name="budget">The ceiling, already resolved by <see cref="Resolve"/>. Non-positive (including
    /// <see cref="Timeout.InfiniteTimeSpan"/>) means no deadline — the escape hatch for a host that owns
    /// timeouts itself. Note this is the RESOLVED value: an options default of
    /// <see cref="Timeout.InfiniteTimeSpan"/> does not survive a request that names its own budget.</param>
    /// <param name="ct">The caller's token, which keeps its own meaning.</param>
    /// <param name="call">The work, handed the composed token.</param>
    /// <param name="timedOut">Builds this call's timeout-shaped answer from a reason phrase.</param>
    internal static async Task<T> GuardAsync<T>(
        TimeSpan budget, CancellationToken ct,
        Func<CancellationToken, Task<T>> call, Func<string, T> timedOut)
    {
        using var deadline = budget > TimeSpan.Zero ? new CancellationTokenSource(budget) : null;
        using var linked = deadline is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        try
        {
            return await call(linked?.Token ?? ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // not the caller's token, so the only clocks that can have fired are ours and — for a BYO client
            // carrying its own Timeout — HttpClient's. Both are a timeout; say which, because the fix differs.
            return timedOut(deadline is { IsCancellationRequested: true }
                ? $"timed out after {budget}"
                : "timed out (the HttpClient's own Timeout elapsed first)");
        }
    }
}
