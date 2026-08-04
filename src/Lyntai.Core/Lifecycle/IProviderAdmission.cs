namespace Lyntai.Lifecycle;

/// <summary>Bounds how many calls may be in flight against one CONFIGURATION at a time.
///
/// <para><b>Why this is a seam and not just a class.</b> <see cref="ProviderAdmission"/> bounds one PROCESS,
/// and the behaviour admission exists for — "two consumers pointing at the same self-hosted engine share its
/// capacity" — is exactly what stops being true once those consumers are two processes, two containers, or
/// two replicas of one service. A host in that position implements this over whatever it already coordinates
/// with (a distributed lock, a lease service, a shared counter) and registers it; the routers and the router
/// factories take this interface, so nothing above changes. Every other governance component here is
/// replaceable the same way (<see cref="IProviderPool{TProvider}"/>,
/// <see cref="Lyntai.Llm.RateLimiting.IRateLimiter"/>, <see cref="Lyntai.Llm.Budgeting.IUsageTracker"/>) —
/// admission has the strongest case of the three.</para></summary>
public interface IProviderAdmission
{
    /// <summary>Wait for a permit for this configuration, then return the handle that releases it.</summary>
    /// <param name="key">The configuration being called.</param>
    /// <param name="ct">Cancels the wait. An implementation that abandons a wait must release whatever
    /// bookkeeping the wait itself took, or a caller that never arrived pins the resource forever.</param>
    /// <returns>The release handle. <b>Never null</b> — an unlimited configuration returns a no-op handle
    /// instead, so a call site is one <c>using</c> with no branch, and a null would read as "unbounded"
    /// while silently admitting everyone. <b>Disposing it releases exactly once</b>, however many times it is
    /// disposed: an implementation that releases twice raises its own effective ceiling for as long as the
    /// count takes to catch up, which is a caller admitted past the configured limit and no error anywhere.
    /// The routers scope the handle with <c>using</c>, so success, failure, a throw and cancellation all
    /// release it the same way.</returns>
    ValueTask<IDisposable> EnterAsync(ProviderKey key, CancellationToken ct = default);
}
