namespace Lyntai.Generation;

/// <summary>The media kinds this platform knows by name. Deliberately CONSTANTS, not an enum:
/// <see cref="GenerationRequest.Kind"/> is an open string so a backend offering a medium nobody has modelled yet
/// (3D meshes already ship on real aggregators; speech-to-speech, motion, whatever follows) fits without a
/// breaking contract change. Same reasoning as the free-form <c>Method</c>/<c>Version</c> values in
/// <c>docs/DECISIONS.md</c> D20.</summary>
public static class GenerationKinds
{
    /// <summary>A still image.</summary>
    public const string Image = "image";

    /// <summary>Moving pictures, with or without an audio track.</summary>
    public const string Video = "video";

    /// <summary>Speech, music or sound effects.</summary>
    public const string Audio = "audio";

    /// <summary>A 3D asset (mesh/scene). Chains into <see cref="Image"/> and then <see cref="Video"/> — see
    /// <see cref="GenerationArtifact.ToInput"/>.</summary>
    public const string Model3d = "3d";
}

/// <summary>Well-known roles for a <see cref="GenerationInput"/> — what an input IS to the generation, which is
/// what distinguishes a backend's modes from each other (one video backend offers text→video,
/// image→video-from-first-frame AND reference→video). Open strings, for the same reason as
/// <see cref="GenerationKinds"/>.</summary>
public static class GenerationInputRoles
{
    /// <summary>The image a generation starts FROM (img2img / init image).</summary>
    public const string Init = "init";

    /// <summary>The image that becomes the video's first frame.</summary>
    public const string FirstFrame = "first-frame";

    /// <summary>A style/subject reference the backend should follow without copying.</summary>
    public const string Reference = "reference";

    /// <summary>A voice sample to clone or match.</summary>
    public const string Voice = "voice";
}
