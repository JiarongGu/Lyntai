namespace Lyntai.Generation;

/// <summary>The outcome of <see cref="IGenerationProvider.ProbeAsync"/> — "is this backend usable right now?",
/// answered WITHOUT generating anything.</summary>
/// <param name="Available">The backend is configured and reachable.</param>
/// <param name="Detail">What it reported, or why it isn't usable (a missing key, an unprovisioned engine, an
/// unreachable endpoint).</param>
/// <param name="Version">The backend's own version, where it reports one.</param>
public sealed record GenerationProbeResult(bool Available, string? Detail = null, string? Version = null);
