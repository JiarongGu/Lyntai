namespace Lyntai.Media;

/// <summary>One generation request, for ANY medium. The medium is <see cref="Kind"/>; everything a specific
/// backend needs beyond the common fields travels in <see cref="Options"/>, so adding a knob is never a
/// contract change.</summary>
/// <remarks>Backend-specific options are string→string on purpose: they are passed THROUGH to a backend that
/// documents them (size, duration, aspect ratio, voice id, steps, a workflow id for a graph-based engine).
/// A platform that typed every backend's knobs would need a release per backend feature.</remarks>
public sealed record MediaRequest
{
    /// <summary>Which medium to produce — a <see cref="MediaKinds"/> value, or any string a backend
    /// advertises in <see cref="MediaCapabilities.Kinds"/>.</summary>
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

    /// <summary>Backend-documented knobs, passed through verbatim.</summary>
    public IReadOnlyDictionary<string, string> Options { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-request budget override in seconds; null = the platform default.</summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>Read an option, or null when absent. Case-insensitive when the caller supplied the default
    /// dictionary; a caller passing its own dictionary decides its own comparer.</summary>
    public string? Option(string name) => Options.TryGetValue(name, out var value) ? value : null;
}

/// <summary>Source media handed TO a generation. Either <paramref name="Data"/> or <paramref name="Uri"/>
/// carries it — a backend that only accepts URLs and one that only accepts bytes are both real, so the
/// platform models both rather than forcing every caller to fetch or upload.</summary>
/// <param name="MediaType">The input's MIME type (<c>image/png</c>, <c>audio/wav</c>).</param>
/// <param name="Data">Inline bytes, when the caller has them.</param>
/// <param name="Uri">A location the BACKEND can read, when bytes would be wasteful.</param>
/// <param name="Role">What this input is to the generation — a <see cref="MediaInputRoles"/> value or any
/// role a backend documents. Null = the backend's default interpretation.</param>
public sealed record MediaInput(
    string MediaType,
    byte[]? Data = null,
    string? Uri = null,
    string? Role = null);
