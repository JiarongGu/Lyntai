using Lyntai.Inference;

namespace Lyntai.Generation.Providers;

/// <summary>The refusal every local backend makes BEFORE it spends anything: a request naming a
/// <see cref="MediaParameters"/> the backend does not declare, or naming one with a value no engine could read.
/// One copy, so the three backends refuse by the same rule and in the same words.</summary>
internal static class ParameterRefusal
{
    /// <summary>The sentence saying why <paramref name="request"/> cannot be handed to a backend declaring
    /// <paramref name="capabilities"/>, or null when it can.</summary>
    /// <param name="capabilities">What the backend declares.</param>
    /// <param name="request">The request about to be handed to it.</param>
    /// <param name="loraTags">True where a LoRA is written into the prompt as a tag, so its name may carry no folder
    /// and no tag character; false where it is a JSON value the server resolves itself (ComfyUI, where a subfolder is
    /// legitimate).</param>
    /// <param name="checkpointFile">True where the checkpoint becomes a file under the host's folder, so its name may
    /// carry no folder.</param>
    public static string? For(ProviderCapabilities capabilities, MediaRequest request, bool loraTags, bool checkpointFile)
    {
        if (capabilities.Unhonourable(request) is { Count: > 0 } unhonoured)
            return $"this backend does not honour {string.Join(", ", unhonoured)}, so nothing was sent " +
                   "(ProviderCapabilities.Parameters says which it does)";
        if (request.Steps is <= 0) return "Steps must be positive";
        if (request.CfgScale is { } cfg && !double.IsFinite(cfg)) return "CfgScale must be a finite number";
        foreach (var lora in request.Loras)
        {
            if (string.IsNullOrWhiteSpace(lora.Name) || (loraTags && !PlainName(lora.Name)))
                return $"the LoRA name '{lora.Name}' is not a plain file name (no folder, no ':', '<' or '>')";
            if (!double.IsFinite(lora.Weight))
                return $"the weight of LoRA '{lora.Name}' must be a finite number";
        }
        if (request.Checkpoint is { Length: > 0 } checkpoint && checkpointFile && !PlainName(checkpoint))
            return $"the checkpoint '{checkpoint}' is not a plain file name (no folder, no ':', '<' or '>')";
        return null;
    }

    /// <summary>A file name and nothing else — the host's model folder decides where it is read from. The tag
    /// characters are refused because a name travels inside <c>&lt;lora:name:weight&gt;</c>, where a colon would
    /// move the weight.</summary>
    public static bool PlainName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.IndexOfAny([.. Path.GetInvalidFileNameChars(), '/', '\\', ':', '<', '>']) < 0 &&
        name is not ("." or "..");
}
