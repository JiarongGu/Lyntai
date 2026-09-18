namespace Lyntai.Inference;

/// <summary>The outcome of <see cref="IModelProvider.ProbeAsync"/> — "is this backend usable right now?",
/// answered WITHOUT spending anything.
///
/// <para>The generate-and-discard check this replaces is a real pattern elsewhere, and it bills a
/// generation to answer a setup question. A backend that genuinely cannot be checked without spending
/// reports <c>Available: false</c> with the reason — it never guesses, and never spends here.</para></summary>
/// <param name="Available">The backend is configured and reachable.</param>
/// <param name="Detail">What it reported, or why it is not usable (a missing key, an unprovisioned engine,
/// an unreachable endpoint).</param>
/// <param name="Version">The backend's own version, where it reports one.</param>
/// <param name="Model">The model the backend reports it is serving, where it names one.</param>
public sealed record ProviderProbeResult(
    bool Available, string? Detail = null, string? Version = null, string? Model = null);
