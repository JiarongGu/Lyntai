namespace Lyntai.Llm;

/// <summary>A non-text part of a message (an image, mostly) — either inline <paramref name="Data"/> bytes
/// or a remote <paramref name="Uri"/>. <paramref name="MediaType"/> is the MIME type (e.g. "image/png").
/// Providers that support vision translate these to their native content parts; text-only providers
/// ignore them and send just the text.</summary>
public sealed record LlmAttachment(string MediaType, byte[]? Data = null, string? Uri = null)
{
    /// <summary>The inline bytes as a <c>data:</c> URL (for providers that take image_url). Empty when the
    /// attachment is a remote <see cref="Uri"/> instead.</summary>
    public string DataUrl() => Data is null ? "" : $"data:{MediaType};base64,{Convert.ToBase64String(Data)}";

    /// <summary>The URL to send: the remote <see cref="Uri"/> if set, else the inline <see cref="DataUrl"/>.</summary>
    public string Url() => Uri ?? DataUrl();
}
