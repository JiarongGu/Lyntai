namespace Lyntai.Generation;

/// <summary>Well-known roles for a <see cref="GenerationInput"/> — what an input IS to the generation, which is
/// what distinguishes a backend's modes from each other (one video backend offers text→video,
/// image→video-from-first-frame AND reference→video). Open strings, for the same reason as
/// <see cref="Lyntai.Inference.ProviderKinds"/>.
///
/// <para><b>A separate vocabulary from what a backend PRODUCES, not an oversight.</b> These say what an
/// input IS TO a generation; the kinds say what comes out. They shared a file until <b>D140</b> merged the
/// kinds away, which is exactly when proximity would have started deciding for a reader.</para></summary>
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
