namespace Lyntai.Llm;

/// <summary>
/// OPTIONAL capability of an <see cref="ILlmProvider"/> whose backend ships its OWN updater (a CLI with an
/// <c>update</c> command, a runtime that can pull a newer build). It exists so a host can offer a guided
/// upgrade instead of telling the user to open a terminal — Lyntai drives the backend's updater; it never
/// provisions, downloads or pins a binary itself (that stays the host's concern).
///
/// Usually implemented alongside <see cref="IProviderInstallation"/> (the version readout is how "did it
/// change?" is answered), but kept separate: a backend can be probe-able without being self-updating.
/// </summary>
public interface IProviderUpdater
{
    /// <summary>Run the backend's own updater. MUTATES the installation — calling it IS the opt-in, so a
    /// host should gate it behind a user action. Implementations must FAIL SAFE (a failure is reported as
    /// <see cref="ProviderUpdateResult.Succeeded"/> <c>false</c>, never a throw) except for caller
    /// cancellation, which propagates.</summary>
    Task<ProviderUpdateResult> UpdateAsync(CancellationToken ct = default);
}

/// <summary>The outcome of <see cref="IProviderUpdater.UpdateAsync"/>. Backends typically have no
/// check-only mode ("is an update available?" can't be asked without installing it), so "was there one?"
/// is answered after the fact by <see cref="Updated"/>: the version before vs. after.</summary>
/// <param name="Succeeded">The updater ran and reported success — whether or not anything was installed.</param>
/// <param name="Updated">The reported version CHANGED, i.e. an update was actually installed. False for an
/// already-current install (the common case) and for every failed run.</param>
/// <param name="FromVersion">The version observed before the updater ran; null when unreachable.</param>
/// <param name="ToVersion">The version observed after it ran — equal to <paramref name="FromVersion"/>
/// when nothing changed, including every failure path.</param>
/// <param name="Detail">The updater's own output (its "up to date" / "updated to X" line), or the failure
/// reason. Surface it verbatim rather than parsing it — the wording belongs to the backend and changes.</param>
public sealed record ProviderUpdateResult(
    bool Succeeded,
    bool Updated,
    string? FromVersion = null,
    string? ToVersion = null,
    string? Detail = null);
