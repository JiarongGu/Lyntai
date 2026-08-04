namespace Lyntai.Media;

/// <summary>How a backend delivers a generation. The single most important axis in this platform: the same
/// medium is served inline by one backend and as a queued job by another, and modelling only one of them
/// forces every other backend to lie (a hidden poll loop, or a buffered stream).</summary>
public enum MediaDelivery
{
    /// <summary>Request → artifacts in the same call. Typical for image generation.</summary>
    Inline,

    /// <summary>Submit → an operation id → poll (or a webhook the APP owns) → fetch. Universal for video and
    /// batch music; renders run for minutes, so the caller needs progress, cancellation and restart-survival.</summary>
    Job,

    /// <summary>Chunks as they are produced, so playback can start before generation finishes. Typical for TTS.</summary>
    Stream,
}

/// <summary>What a backend can actually do. Declared rather than discovered, because a wrong guess costs a
/// billable generation — and because routing needs to PRE-FILTER candidates before spending anything.</summary>
public sealed record MediaCapabilities
{
    /// <summary>Media kinds served (<see cref="MediaKinds"/> values, or backend-specific strings).</summary>
    public IReadOnlyList<string> Kinds { get; init; } = [];

    /// <summary>Delivery modes offered. A backend may offer several (inline for a small image, job for a
    /// large one) — the router asks for the mode the caller wants.</summary>
    public IReadOnlyList<MediaDelivery> Deliveries { get; init; } = [];

    /// <summary>Model/endpoint ids this backend serves. EMPTY means "not enumerated" (a single-model backend,
    /// or a catalogue too large/volatile to mirror) — never "serves nothing", so an empty list must not filter
    /// a request out.</summary>
    public IReadOnlyList<string> Models { get; init; } = [];

    /// <summary>Whether the backend accepts <see cref="MediaRequest.Inputs"/> at all (img2img,
    /// image→video, voice cloning). A text-only backend handed an input would silently ignore it.</summary>
    public bool SupportsInputs { get; init; }

    /// <summary>Backend-documented ceilings, free-form so a new limit isn't a contract change
    /// (<c>max-duration-seconds</c>, <c>max-width</c>, <c>voices</c>). Informational for callers; the platform
    /// does not enforce them (only the backend knows the real rule).</summary>
    public IReadOnlyDictionary<string, string> Limits { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether this backend can serve <paramref name="request"/> via <paramref name="delivery"/>.
    /// Conservative on the axes it KNOWS (kind, delivery, inputs, an enumerated model) and permissive where it
    /// knows nothing (an empty model list) — a false "no" wastes a working backend, a false "yes" wastes a
    /// call, and only the first is silent.</summary>
    public bool Supports(MediaRequest request, MediaDelivery delivery)
    {
        if (!Kinds.Contains(request.Kind, StringComparer.OrdinalIgnoreCase)) return false;
        if (!Deliveries.Contains(delivery)) return false;
        if (request.Inputs.Count > 0 && !SupportsInputs) return false;
        if (request.Model is { Length: > 0 } model && Models.Count > 0 &&
            !Models.Contains(model, StringComparer.OrdinalIgnoreCase)) return false;
        return true;
    }
}

/// <summary>The outcome of <see cref="IMediaProvider.ProbeAsync"/> — "is this backend usable right now?",
/// answered WITHOUT generating anything.</summary>
/// <param name="Available">The backend is configured and reachable.</param>
/// <param name="Detail">What it reported, or why it isn't usable (a missing key, an unprovisioned engine, an
/// unreachable endpoint).</param>
/// <param name="Version">The backend's own version, where it reports one.</param>
public sealed record MediaProbeResult(bool Available, string? Detail = null, string? Version = null);
