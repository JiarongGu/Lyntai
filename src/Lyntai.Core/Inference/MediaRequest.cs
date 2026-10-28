namespace Lyntai.Inference;

/// <summary>One generation request, for ANY medium. The medium is <see cref="Kind"/>; everything a specific
/// backend needs beyond the common fields travels in <see cref="Options"/>, so adding a knob is never a
/// contract change.</summary>
/// <remarks>Backend-specific options are string→string on purpose: they are passed THROUGH to a backend that
/// documents them (size, duration, aspect ratio, voice id, steps, a workflow id for a graph-based engine).
/// A platform that typed every backend's knobs would need a release per backend feature.</remarks>
public sealed record MediaRequest
{
    /// <summary>Which medium to produce — a <see cref="ProviderKinds"/> value, or any string a backend
    /// advertises in <see cref="ProviderCapabilities.Produces"/>.</summary>
    public required string Kind { get; init; }

    /// <summary>The text prompt, where the backend takes one. Null for a backend driven entirely by
    /// <see cref="Inputs"/> plus <see cref="Options"/> (e.g. a fixed workflow graph).</summary>
    public string? Prompt { get; init; }

    /// <summary>Source media the generation works FROM — an init image, a first frame, a style reference, a
    /// voice sample. Empty for pure text→media.</summary>
    public IReadOnlyList<MediaInput> Inputs { get; init; } = [];

    /// <summary>The model / endpoint id to use AT the selected backend, when it serves more than one (an
    /// aggregator serves hundreds). Null = the backend's own default.</summary>
    public string? Model { get; init; }

    /// <summary>The checkpoint to render with, by file name in the model folder the SELECTED BACKEND is configured
    /// with — never a path. Null = the backend's own default. Distinct from <see cref="Model"/>, which names an
    /// endpoint at an aggregator.</summary>
    public string? Checkpoint { get; init; }

    /// <summary>LoRAs to apply, in order, each by name with a weight. Empty = none.</summary>
    public IReadOnlyList<MediaLora> Loras { get; init; } = [];

    /// <summary>What the render should avoid, where the backend takes one.</summary>
    public string? NegativePrompt { get; init; }

    /// <summary>The random seed; the same seed, model and parameters give the same image on one backend. Null = the
    /// backend's own choice.</summary>
    public long? Seed { get; init; }

    /// <summary>Sampling steps. Null = the backend's configured default.</summary>
    public int? Steps { get; init; }

    /// <summary>Classifier-free guidance scale. Null = the backend's configured default.</summary>
    public double? CfgScale { get; init; }

    /// <summary>The sampler, spelled as the SELECTED BACKEND spells it (<c>euler</c>, <c>dpmpp_2m</c>,
    /// <c>dpm++2m</c>): engines disagree on the names and nothing here translates them.</summary>
    public string? Sampler { get; init; }

    /// <summary>The sigma scheduler, spelled as the SELECTED BACKEND spells it (<c>karras</c>, <c>simple</c>).</summary>
    public string? Scheduler { get; init; }

    /// <summary>Backend-documented knobs, passed through verbatim.</summary>
    public IReadOnlyDictionary<string, string> Options { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-request budget in seconds, applied by the SELECTED BACKEND where it supports one: the HTTP
    /// backends resolve it against their own configured <c>Timeout</c> option (a positive value here wins; a
    /// non-positive one is not a budget and is ignored), while a backend that owns its own clocks may ignore it
    /// entirely — the local engines (<c>sd-cli</c>, piper) do. Unlike <c>TextRequest.TimeoutSeconds</c> there is no
    /// platform-level default and no ceiling for generation: nothing clamps this to
    /// <c>LyntaiOptions.MaxProviderTimeout</c>, which governs the LLM domain only.</summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>Who this render is for — the tag spend caps and rate limits are keyed by, exactly as on the
    /// LLM side (<c>TextRequest.Consumer</c>), and matched case-insensitively by both. Governance is why this
    /// exists: without it every render in a process bills to one bucket, and the runaway-spend case (an agent
    /// loop rendering in a retry) can't be capped separately from a user pressing a button.</summary>
    public string Consumer { get; init; } = ProviderConsumers.Default;

    /// <summary>Read an option, or null when absent. Case-insensitive when the caller supplied the default
    /// dictionary; a caller passing its own dictionary decides its own comparer.</summary>
    public string? Option(string name) => Options.TryGetValue(name, out var value) ? value : null;
}
