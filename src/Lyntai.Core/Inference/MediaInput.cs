namespace Lyntai.Inference;

/// <summary>Source media handed TO a generation. Either <paramref name="Data"/> or <paramref name="Uri"/>
/// carries it — a backend that only accepts URLs and one that only accepts bytes are both real, so the
/// platform models both rather than forcing every caller to fetch or upload.</summary>
/// <param name="MediaType">The input's MIME type (<c>image/png</c>, <c>audio/wav</c>).</param>
/// <param name="Data">Inline bytes, when the caller has them.</param>
/// <param name="Uri">A location the BACKEND can read, when bytes would be wasteful.</param>
/// <param name="Role">What this input is to the generation — a <see cref="MediaInputRoles"/> value or any
/// role a backend documents. Null = the backend's default interpretation.</param>
/// <remarks><b>Prefer the named factories</b> (<see cref="Init(byte[], string)"/>,
/// <see cref="FirstFrame(byte[], string)"/>, <see cref="Reference(byte[], string)"/>,
/// <see cref="Voice(byte[], string)"/>, <see cref="From(string, byte[], string)"/>) over this constructor.
/// Three of its four slots are strings and <c>Role</c> is LAST, so the plausible positional call
/// <c>new MediaInput(MediaInputRoles.Init, bytes, "image/png")</c> compiles clean and binds
/// <c>"init"</c> to <c>MediaType</c> while leaving <c>Role</c> null — and nothing then fails: the backend
/// receives a well-formed roleless input, an img2img request silently degrades to text-to-image, a plausible
/// image comes back, and the caller's source image was simply ignored. The factories make the role impossible
/// to omit.</remarks>
public sealed record MediaInput(
    string MediaType,
    byte[]? Data = null,
    string? Uri = null,
    string? Role = null)
{
    /// <summary>The image a generation starts FROM (img2img / init image), as bytes.</summary>
    /// <param name="data">The source image.</param>
    /// <param name="mediaType">Its MIME type (<c>image/png</c>).</param>
    public static MediaInput Init(byte[] data, string mediaType) =>
        From(MediaInputRoles.Init, data, mediaType);

    /// <summary>The image a generation starts FROM, at a location the BACKEND reads.</summary>
    /// <param name="uri">Where the source image lives.</param>
    /// <param name="mediaType">Its MIME type (<c>image/png</c>).</param>
    public static MediaInput Init(Uri uri, string mediaType) =>
        From(MediaInputRoles.Init, uri, mediaType);

    /// <summary>The image that becomes the video's first frame, as bytes.</summary>
    /// <param name="data">The frame.</param>
    /// <param name="mediaType">Its MIME type (<c>image/png</c>).</param>
    public static MediaInput FirstFrame(byte[] data, string mediaType) =>
        From(MediaInputRoles.FirstFrame, data, mediaType);

    /// <summary>The image that becomes the video's first frame, at a location the BACKEND reads.</summary>
    /// <param name="uri">Where the frame lives.</param>
    /// <param name="mediaType">Its MIME type (<c>image/png</c>).</param>
    public static MediaInput FirstFrame(Uri uri, string mediaType) =>
        From(MediaInputRoles.FirstFrame, uri, mediaType);

    /// <summary>A style/subject reference the backend should follow without copying, as bytes.</summary>
    /// <param name="data">The reference media.</param>
    /// <param name="mediaType">Its MIME type.</param>
    public static MediaInput Reference(byte[] data, string mediaType) =>
        From(MediaInputRoles.Reference, data, mediaType);

    /// <summary>A style/subject reference, at a location the BACKEND reads.</summary>
    /// <param name="uri">Where the reference lives.</param>
    /// <param name="mediaType">Its MIME type.</param>
    public static MediaInput Reference(Uri uri, string mediaType) =>
        From(MediaInputRoles.Reference, uri, mediaType);

    /// <summary>A voice sample to clone or match, as bytes.</summary>
    /// <param name="data">The sample.</param>
    /// <param name="mediaType">Its MIME type (<c>audio/wav</c>).</param>
    public static MediaInput Voice(byte[] data, string mediaType) =>
        From(MediaInputRoles.Voice, data, mediaType);

    /// <summary>A voice sample, at a location the BACKEND reads.</summary>
    /// <param name="uri">Where the sample lives.</param>
    /// <param name="mediaType">Its MIME type (<c>audio/wav</c>).</param>
    public static MediaInput Voice(Uri uri, string mediaType) =>
        From(MediaInputRoles.Voice, uri, mediaType);

    /// <summary>An input in a role this platform doesn't name — <see cref="MediaInputRoles"/> is constants
    /// rather than an enum precisely so a backend can document its own (a mask, a depth map, a control image).
    /// <paramref name="role"/> comes FIRST here for the same reason the named factories exist: a role that can
    /// be silently omitted is a role that silently changes what the backend does.</summary>
    /// <param name="role">What this input IS to the generation.</param>
    /// <param name="data">The media, as bytes.</param>
    /// <param name="mediaType">Its MIME type.</param>
    public static MediaInput From(string role, byte[] data, string mediaType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        return new MediaInput(mediaType, Data: data, Role: role);
    }

    /// <summary>An input in a backend-documented role, at a location the BACKEND reads. Takes a
    /// <see cref="System.Uri"/> rather than a string so the media-type/location pair cannot be transposed —
    /// two adjacent strings would reintroduce exactly the misbinding these factories exist to prevent.</summary>
    /// <param name="role">What this input IS to the generation.</param>
    /// <param name="uri">Where the media lives.</param>
    /// <param name="mediaType">Its MIME type.</param>
    public static MediaInput From(string role, Uri uri, string mediaType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        return new MediaInput(mediaType, Uri: uri.ToString(), Role: role);
    }
}
