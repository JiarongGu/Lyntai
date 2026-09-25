namespace Lyntai.Inference;

/// <summary>The retry-then-advance attempt at ONE candidate, for the two routers that read
/// <see cref="RoutingPolicy"/> (<see cref="TextRouter"/> and <see cref="ProviderRouter{TRequest,TResponse}"/>).
/// A candidate may be retried before the router advances, and the retries are part of ONE attempt: exactly one
/// failure is recorded when they are exhausted, never one per retry — recording per retry would cross the
/// dead-host threshold inside a single call.</summary>
internal static class CandidateAttempt
{
    /// <summary>Attempt one candidate until it answers Ok, the policy surfaces its answer, or it is done with.
    /// Returns the response the router must RETURN (Ok or surfaced), or null to advance; every failure it did not
    /// return goes to <paramref name="file"/>, in order, for the router's reporting slots.</summary>
    /// <param name="attempt">One call at the candidate.</param>
    /// <param name="file">Receives each failure the router does not return.</param>
    /// <param name="policy">What to do per verdict, and the retry budgets.</param>
    /// <param name="deadHosts">The tracker, or null for no cooldown.</param>
    /// <param name="key">The candidate's cooldown key.</param>
    /// <param name="onRetry">Told the verdict and the retry number before each retry.</param>
    /// <param name="ct">Caller cancellation, for the backoff.</param>
    internal static async Task<TResponse?> RunAsync<TResponse>(
        Func<Task<TResponse>> attempt, Action<TResponse> file, RoutingPolicy policy, DeadHostTracker? deadHosts,
        string key, Action<ProviderVerdict, int>? onRetry, CancellationToken ct)
        where TResponse : class, IProviderOutcome
    {
        for (var retries = 1; ; retries++)
        {
            var response = await attempt().ConfigureAwait(false);
            if (response.Verdict == ProviderVerdict.Ok)
            {
                deadHosts?.RecordSuccess(key);
                return response;
            }

            var action = policy.ActionFor(response.Verdict);
            if (action == FallbackAction.Surface) return response; // follows the prompt, not the host

            file(response);
            if (action == FallbackAction.CooldownAndAdvance)
            {
                deadHosts?.MarkDead(key); // terminal for this host, advance to the next
                return null;
            }
            if (!await RetryAsync(policy, deadHosts, key, response.Verdict, retries, onRetry, ct).ConfigureAwait(false))
                return null;
        }
    }

    /// <summary>After a failed attempt: whether to retry the same candidate (after the policy's backoff), or —
    /// retries exhausted or none configured — record the ONE failure a penalizing verdict carries and advance.</summary>
    internal static async ValueTask<bool> RetryAsync(
        RoutingPolicy policy, DeadHostTracker? deadHosts, string key, ProviderVerdict verdict, int retries,
        Action<ProviderVerdict, int>? onRetry, CancellationToken ct)
    {
        if (policy.ShouldRetrySameCandidate(verdict, retries))
        {
            onRetry?.Invoke(verdict, retries);
            if (policy.RetryBackoff > TimeSpan.Zero) await Task.Delay(policy.RetryBackoff, ct).ConfigureAwait(false);
            return true;
        }
        if (policy.ActionFor(verdict) == FallbackAction.PenalizeAndAdvance) deadHosts?.RecordFailure(key);
        return false;
    }
}
