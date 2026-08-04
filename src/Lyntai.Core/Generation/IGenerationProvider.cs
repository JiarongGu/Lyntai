namespace Lyntai.Generation;

/// <summary>
/// A media generation backend. This is the base seam every backend implements — identity, declared
/// capabilities, a no-cost probe, and INLINE generation (request → artifacts), which is how most image
/// backends behave.
///
/// Long-running and streaming backends implement an ADDITIONAL interface —
/// <see cref="IGenerationJobProvider"/> or <see cref="IGenerationStreamProvider"/> — rather than pretending their
/// delivery fits this method. That is the same optional-capability pattern Core uses for
/// <c>IProviderAuth</c>/<c>IProviderVersionInstaller</c>: a backend that can't do something simply doesn't
/// implement it, and callers pattern-match over the registered collection.
///
/// Lyntai does not generate media. A backend adapts a service or an engine the host already has.
/// </summary>
public interface IGenerationProvider
{
    /// <summary>The candidate id routing selects on (<c>"openai-images"</c>, <c>"a1111"</c>,
    /// <c>"local-diffusion"</c>).</summary>
    string Id { get; }

    /// <summary>What this backend can serve. Read by the router BEFORE spending anything.</summary>
    GenerationCapabilities Capabilities { get; }

    /// <summary>Is this backend usable right now — configured, reachable, provisioned — WITHOUT generating
    /// anything? Implementations must FAIL SAFE (a value, never a throw) except for caller cancellation.</summary>
    /// <remarks>The generate-and-discard test this replaces is a real pattern in a sibling app, and it bills
    /// a generation to answer a setup question. If a backend genuinely cannot be checked without generating,
    /// report <c>Available: false</c> with the reason — never guess, and never generate here.</remarks>
    Task<GenerationProbeResult> ProbeAsync(CancellationToken ct = default);

    /// <summary>Generate inline: one call, artifacts back. Implementations must FAIL SAFE — a transport or
    /// backend failure is a <see cref="GenerationResult"/> with a verdict, not an exception (cancellation
    /// propagates).</summary>
    Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default);
}
