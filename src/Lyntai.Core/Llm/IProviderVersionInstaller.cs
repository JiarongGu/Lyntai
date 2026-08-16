namespace Lyntai.Llm;

/// <summary>
/// OPTIONAL capability of an <see cref="ILlmProvider"/> whose backend can install a NAMED version of
/// itself (a CLI with an <c>install &lt;version|channel&gt;</c> command, a runtime that can fetch a
/// specific build). It is the difference between a host PINNING a known-good version and merely taking
/// whatever <see cref="IProviderUpdater.UpdateAsync"/> hands it.
///
/// This does not contradict "Lyntai never provisions a binary": that stays true for FETCHING a binary from
/// nowhere (a host owns its own download/storage policy). Driving an already-present backend's OWN
/// installer is the update path with an argument, and the same class of thing as
/// <see cref="IProviderUpdater"/> — which is why it reports the same
/// <see cref="ProviderUpdateResult"/>, so a caller keeps one result type for all self-maintenance.
///
/// Kept separate from <see cref="IProviderUpdater"/> rather than added to it: a backend can be
/// self-updating without being able to pin, and callers discover the capability by pattern-matching
/// (<c>provider is IProviderVersionInstaller i</c>) over the registered providers. Distinct from
/// <see cref="IProviderProbe"/>, which only READS what is installed.
/// </summary>
public interface IProviderVersionInstaller
{
    /// <summary>Ask the backend to install a version of itself. MUTATES the installation — calling it IS
    /// the opt-in, so a host should gate it behind a user action, and it can legitimately run for minutes
    /// (it downloads). Implementations must FAIL SAFE (a failure is
    /// <see cref="ProviderUpdateResult.Succeeded"/> <c>false</c>, never a throw) except for caller
    /// cancellation, which propagates.</summary>
    /// <param name="request">Which version/channel to install; <c>null</c> means the backend's own default
    /// target.</param>
    /// <param name="ct">Cancellation.</param>
    /// <remarks><see cref="ProviderUpdateResult.Updated"/> means the reported version CHANGED — which for a
    /// pin includes a DOWNGRADE, the whole point of asking for a named version.</remarks>
    Task<ProviderUpdateResult> InstallAsync(ProviderInstallRequest? request = null, CancellationToken ct = default);
}

/// <summary>Which version <see cref="IProviderVersionInstaller.InstallAsync"/> should install.</summary>
/// <param name="Version">A version or channel name in the backend's OWN vocabulary — an exact number
/// (<c>"2.1.220"</c>) or a moving target it recognizes (<c>"stable"</c>, <c>"latest"</c>). Free-form on
/// purpose: another backend's channel names must fit without an enum change. Null = the backend's default
/// target. An implementation must reject a value that its backend would read as a FLAG rather than a
/// version.</param>
/// <param name="Force">Install even when that version already appears to be present (a repair/reinstall),
/// where the backend supports it.</param>
public sealed record ProviderInstallRequest(
    string? Version = null,
    bool Force = false);
