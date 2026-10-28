namespace Lyntai.Inference;

/// <summary>One LoRA a request asks a backend to apply.</summary>
/// <param name="Name">The LoRA's file name as the BACKEND's model folder holds it (<c>"lcm-lora-sdv1-5.safetensors"</c>);
/// a backend that looks the file up itself also takes it without the extension. A name, never a path: the host decides
/// which folder a backend reads.</param>
/// <param name="Weight">How strongly it applies; 1 is the LoRA's own strength and 0 switches it off. Finite.</param>
public sealed record MediaLora(string Name, double Weight = 1.0);

/// <summary>The generation parameters a <see cref="MediaRequest"/> can name beyond its prompt, as the names a
/// backend declares in <see cref="ProviderCapabilities.Parameters"/>. A request carrying one a backend does not
/// declare is refused before any call, as an agent session needing an ability its door states unsupported is.
///
/// <para>Open like <see cref="ProviderKinds"/>: a backend of your own may declare a name nobody modelled. The size
/// of an image is not here: it stays the advisory <c>Options["size"]</c> hint every backend reads or ignores.</para></summary>
public static class MediaParameters
{
    /// <summary><see cref="MediaRequest.Checkpoint"/>.</summary>
    public const string Checkpoint = "checkpoint";

    /// <summary><see cref="MediaRequest.Loras"/>.</summary>
    public const string Loras = "loras";

    /// <summary><see cref="MediaRequest.NegativePrompt"/>.</summary>
    public const string NegativePrompt = "negative-prompt";

    /// <summary><see cref="MediaRequest.Seed"/>.</summary>
    public const string Seed = "seed";

    /// <summary><see cref="MediaRequest.Steps"/>.</summary>
    public const string Steps = "steps";

    /// <summary><see cref="MediaRequest.CfgScale"/>.</summary>
    public const string CfgScale = "cfg-scale";

    /// <summary><see cref="MediaRequest.Sampler"/>.</summary>
    public const string Sampler = "sampler";

    /// <summary><see cref="MediaRequest.Scheduler"/>.</summary>
    public const string Scheduler = "scheduler";

    /// <summary>Every parameter this library names, in a fixed order.</summary>
    public static IReadOnlyList<string> All { get; } =
        [Checkpoint, Loras, NegativePrompt, Seed, Steps, CfgScale, Sampler, Scheduler];

    /// <summary>The parameters <paramref name="request"/> names, in the order of <see cref="All"/>. A blank
    /// string and an empty LoRA list name nothing.</summary>
    public static IReadOnlyList<string> Needed(MediaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var needed = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(request.Checkpoint)) needed.Add(Checkpoint);
        if (request.Loras.Count > 0) needed.Add(Loras);
        if (!string.IsNullOrWhiteSpace(request.NegativePrompt)) needed.Add(NegativePrompt);
        if (request.Seed is not null) needed.Add(Seed);
        if (request.Steps is not null) needed.Add(Steps);
        if (request.CfgScale is not null) needed.Add(CfgScale);
        if (!string.IsNullOrWhiteSpace(request.Sampler)) needed.Add(Sampler);
        if (!string.IsNullOrWhiteSpace(request.Scheduler)) needed.Add(Scheduler);
        return needed;
    }
}
