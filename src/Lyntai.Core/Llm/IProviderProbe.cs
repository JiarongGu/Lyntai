namespace Lyntai.Llm;

/// <summary>
/// OPTIONAL capability of an <see cref="ILlmProvider"/> whose backend can describe ITSELF without running
/// a completion: a spawned CLI that prints a version, a local runtime that knows its loaded weights, a
/// server with a version endpoint. Lets a host show what is actually installed — on a fresh install,
/// before any turn has run — instead of a hardcoded guess or a value scraped from the last run's telemetry.
///
/// A separate interface rather than members on <see cref="ILlmProvider"/>: a provider that can't answer
/// cheaply simply doesn't implement it, and callers discover the capability by pattern-matching
/// (<c>provider is IProviderProbe p</c>) over the registered provider collection.
/// </summary>
public interface IProviderProbe
{
    /// <summary>Ask the backend what it is, WITHOUT running a completion (no tokens, no model call).
    /// Implementations must FAIL SAFE — an absent/unreachable/stalled backend returns
    /// <see cref="ProviderProbeResult.Available"/> <c>false</c> with the reason in
    /// <see cref="ProviderProbeResult.Detail"/>, never a throw — except for caller cancellation, which
    /// propagates.</summary>
    Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default);
}

/// <summary>The outcome of <see cref="IProviderProbe.ProbeAsync"/>.</summary>
/// <param name="Available">The backend answered — a STRONGER signal than
/// <see cref="ILlmProvider.IsAvailable"/>, which is a cheap guess that never contacts anything.</param>
/// <param name="Version">The backend's own version, normalized to its dotted number where it reports one
/// (<c>"2.1.220"</c>); null when unreachable or when it reports nothing version-shaped. Compare two of
/// these to tell whether an update changed anything.</param>
/// <param name="Model">The model the backend says it will use by default, when it can say so without a
/// completion (a local runtime naming its loaded weights, a server reporting its default). Null when the
/// backend can't answer that cheaply — a probe never GUESSES a model; read the model actually used from
/// the run's telemetry instead.</param>
/// <param name="Detail">What the backend reported, verbatim (useful in a diagnostics pane), or the failure
/// reason when <paramref name="Available"/> is false.</param>
public sealed record ProviderProbeResult(
    bool Available,
    string? Version = null,
    string? Model = null,
    string? Detail = null);
