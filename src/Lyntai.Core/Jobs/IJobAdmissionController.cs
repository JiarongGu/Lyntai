namespace Lyntai.Jobs;

/// <summary>
/// An app-provided admission gate the <see cref="IJobRunner"/> consults per lane BEFORE it claims from
/// that lane, so the app can throttle work by signals Lyntai knows nothing about — external GPU/CPU load,
/// a maintenance flag, a time-of-day window, a downstream rate limit. Returning <c>false</c> HOLDS the
/// lane for this pass: its jobs stay Pending (no state change), the runner simply skips claiming from it
/// and re-checks next poll.
/// <para>This is the transient, whole-lane counterpart to <see cref="JobStatus.Paused"/> (which persists a
/// hold on ONE job). The default (<see cref="AdmitAllAdmissionController"/>) admits every lane; override by
/// registering your own via <c>AddJobAdmissionController</c>. Keep it cheap and non-throwing — it's called
/// once per active lane per pass; the runner treats a throw as "hold this lane" and logs it.</para>
/// </summary>
public interface IJobAdmissionController
{
    /// <summary>Whether the runner may claim from <paramref name="lane"/> right now.</summary>
    ValueTask<bool> CanClaimAsync(string lane, CancellationToken ct = default);
}

/// <summary>The default admission controller — admits every lane (no throttling). Replaced by registering
/// a custom <see cref="IJobAdmissionController"/>.</summary>
public sealed class AdmitAllAdmissionController : IJobAdmissionController
{
    public ValueTask<bool> CanClaimAsync(string lane, CancellationToken ct = default) => new(true);
}
