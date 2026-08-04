namespace Lyntai.Generation;

/// <summary>One generation request, for ANY medium. The medium is <see cref="Kind"/>; everything a specific
/// backend needs beyond the common fields travels in <see cref="Options"/>, so adding a knob is never a
/// contract change.</summary>
/// <remarks>Backend-specific options are string→string on purpose: they are passed THROUGH to a backend that
/// documents them (size, duration, aspect ratio, voice id, steps, a workflow id for a graph-based engine).
/// A platform that typed every backend's knobs would need a release per backend feature.</remarks>
public sealed record GenerationRequest
{
    /// <summary>Which medium to produce — a <see cref="GenerationKinds"/> value, or any string a backend
    /// advertises in <see cref="GenerationCapabilities.Kinds"/>.</summary>
    public required string Kind { get; init; }

    /// <summary>The text prompt, where the backend takes one. Null for a backend driven entirely by
    /// <see cref="Inputs"/> plus <see cref="Options"/> (e.g. a fixed workflow graph).</summary>
    public string? Prompt { get; init; }

    /// <summary>Source media the generation works FROM — an init image, a first frame, a style reference, a
    /// voice sample. Empty for pure text→media.</summary>
    public IReadOnlyList<GenerationInput> Inputs { get; init; } = [];

    /// <summary>The model / endpoint id to use AT the selected backend, when it serves more than one (an
    /// aggregator serves hundreds). Null = the backend's own default.</summary>
    public string? Model { get; init; }

    /// <summary>Backend-documented knobs, passed through verbatim.</summary>
    public IReadOnlyDictionary<string, string> Options { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-request budget override in seconds; null = the platform default.</summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>Who this render is for — the tag spend caps and rate limits are keyed by, exactly as on the
    /// LLM side (<c>LlmRequest.Consumer</c>), and matched case-insensitively by both. Governance is why this
    /// exists: without it every render in a process bills to one bucket, and the runaway-spend case (an agent
    /// loop rendering in a retry) can't be capped separately from a user pressing a button.</summary>
    public string Consumer { get; init; } = "default";

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
/// <param name="Role">What this input is to the generation — a <see cref="GenerationInputRoles"/> value or any
/// role a backend documents. Null = the backend's default interpretation.</param>
/// <remarks><b>Prefer the named factories</b> (<see cref="Init(byte[], string)"/>,
/// <see cref="FirstFrame(byte[], string)"/>, <see cref="Reference(byte[], string)"/>,
/// <see cref="Voice(byte[], string)"/>, <see cref="From(string, byte[], string)"/>) over this constructor.
/// Three of its four slots are strings and <c>Role</c> is LAST, so the plausible positional call
/// <c>new GenerationInput(GenerationInputRoles.Init, bytes, "image/png")</c> compiles clean and binds
/// <c>"init"</c> to <c>MediaType</c> while leaving <c>Role</c> null — and nothing then fails: the backend
/// receives a well-formed roleless input, an img2img request silently degrades to text-to-image, a plausible
/// image comes back, and the caller's source image was simply ignored. The factories make the role impossible
/// to omit.</remarks>
public sealed record GenerationInput(
    string MediaType,
    byte[]? Data = null,
    string? Uri = null,
    string? Role = null)
{
    /// <summary>The image a generation starts FROM (img2img / init image), as bytes.</summary>
    /// <param name="data">The source image.</param>
    /// <param name="mediaType">Its MIME type (<c>image/png</c>).</param>
    public static GenerationInput Init(byte[] data, string mediaType) =>
        From(GenerationInputRoles.Init, data, mediaType);

    /// <summary>The image a generation starts FROM, at a location the BACKEND reads.</summary>
    /// <param name="uri">Where the source image lives.</param>
    /// <param name="mediaType">Its MIME type (<c>image/png</c>).</param>
    public static GenerationInput Init(Uri uri, string mediaType) =>
        From(GenerationInputRoles.Init, uri, mediaType);

    /// <summary>The image that becomes the video's first frame, as bytes.</summary>
    /// <param name="data">The frame.</param>
    /// <param name="mediaType">Its MIME type (<c>image/png</c>).</param>
    public static GenerationInput FirstFrame(byte[] data, string mediaType) =>
        From(GenerationInputRoles.FirstFrame, data, mediaType);

    /// <summary>The image that becomes the video's first frame, at a location the BACKEND reads.</summary>
    /// <param name="uri">Where the frame lives.</param>
    /// <param name="mediaType">Its MIME type (<c>image/png</c>).</param>
    public static GenerationInput FirstFrame(Uri uri, string mediaType) =>
        From(GenerationInputRoles.FirstFrame, uri, mediaType);

    /// <summary>A style/subject reference the backend should follow without copying, as bytes.</summary>
    /// <param name="data">The reference media.</param>
    /// <param name="mediaType">Its MIME type.</param>
    public static GenerationInput Reference(byte[] data, string mediaType) =>
        From(GenerationInputRoles.Reference, data, mediaType);

    /// <summary>A style/subject reference, at a location the BACKEND reads.</summary>
    /// <param name="uri">Where the reference lives.</param>
    /// <param name="mediaType">Its MIME type.</param>
    public static GenerationInput Reference(Uri uri, string mediaType) =>
        From(GenerationInputRoles.Reference, uri, mediaType);

    /// <summary>A voice sample to clone or match, as bytes.</summary>
    /// <param name="data">The sample.</param>
    /// <param name="mediaType">Its MIME type (<c>audio/wav</c>).</param>
    public static GenerationInput Voice(byte[] data, string mediaType) =>
        From(GenerationInputRoles.Voice, data, mediaType);

    /// <summary>A voice sample, at a location the BACKEND reads.</summary>
    /// <param name="uri">Where the sample lives.</param>
    /// <param name="mediaType">Its MIME type (<c>audio/wav</c>).</param>
    public static GenerationInput Voice(Uri uri, string mediaType) =>
        From(GenerationInputRoles.Voice, uri, mediaType);

    /// <summary>An input in a role this platform doesn't name — <see cref="GenerationInputRoles"/> is constants
    /// rather than an enum precisely so a backend can document its own (a mask, a depth map, a control image).
    /// <paramref name="role"/> comes FIRST here for the same reason the named factories exist: a role that can
    /// be silently omitted is a role that silently changes what the backend does.</summary>
    /// <param name="role">What this input IS to the generation.</param>
    /// <param name="data">The media, as bytes.</param>
    /// <param name="mediaType">Its MIME type.</param>
    public static GenerationInput From(string role, byte[] data, string mediaType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        return new GenerationInput(mediaType, Data: data, Role: role);
    }

    /// <summary>An input in a backend-documented role, at a location the BACKEND reads. Takes a
    /// <see cref="System.Uri"/> rather than a string so the media-type/location pair cannot be transposed —
    /// two adjacent strings would reintroduce exactly the misbinding these factories exist to prevent.</summary>
    /// <param name="role">What this input IS to the generation.</param>
    /// <param name="uri">Where the media lives.</param>
    /// <param name="mediaType">Its MIME type.</param>
    public static GenerationInput From(string role, Uri uri, string mediaType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        return new GenerationInput(mediaType, Uri: uri.ToString(), Role: role);
    }
}
