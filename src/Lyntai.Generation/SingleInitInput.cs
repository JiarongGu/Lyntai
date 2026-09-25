using Lyntai.Inference;

namespace Lyntai.Generation.Providers;

/// <summary>The input rule of a backend that reads ONE source image, as its init image — OpenAI images,
/// Automatic1111 and <c>sd-cli</c>. <see cref="ProviderCapabilities.SupportsInputs"/> makes the router hand such
/// a backend any input-carrying request, so an input it cannot place is refused, never used as the init image
/// or dropped.</summary>
internal static class SingleInitInput
{
    /// <summary>The one init image <paramref name="request"/> carries, or null when it carries none. Sets
    /// <paramref name="refusal"/> instead when it carries a second input, or one in a role other than
    /// <see cref="MediaInputRoles.Init"/>; a roleless input is the init image.</summary>
    /// <param name="request">The request.</param>
    /// <param name="backend">How the refusal names this backend.</param>
    /// <param name="refusal">Why the request cannot be served as posed, or null.</param>
    public static MediaInput? Read(MediaRequest request, string backend, out string? refusal)
    {
        refusal = null;
        if (request.Inputs.Count == 0) return null;
        if (request.Inputs.Count > 1)
        {
            refusal = $"{backend} reads one source image, and this request carries {request.Inputs.Count} inputs";
            return null;
        }

        var input = request.Inputs[0];
        if (input.Role is { Length: > 0 } role &&
            !string.Equals(role, MediaInputRoles.Init, StringComparison.OrdinalIgnoreCase))
        {
            refusal = $"{backend} reads its one input as the init image, and this one's role is '{role}'";
            return null;
        }
        return input;
    }
}
