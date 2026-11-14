using Lyntai.Inference;

namespace Lyntai.Agents;

/// <summary>An optional capability of an <see cref="ITool"/>, found by type test: a tool whose answer can carry
/// images as well as text. <see cref="ToolLoop"/> calls <see cref="InvokeWithAttachmentsAsync"/> in place of
/// <see cref="ITool.InvokeAsync"/>, feeds the text back as the call's result, and hands the images to the model on
/// one user turn after the turn's last tool result, because the vision wires take images on a user turn only. A
/// provider that cannot show an image drops it, so the text must still say what the tool returned.</summary>
public interface IAttachmentTool : ITool
{
    /// <summary>Run the tool, as <see cref="ITool.InvokeAsync"/> does, returning the observation and any images
    /// beside it. The text is the whole observation a text-only model reads; the images are an addition.</summary>
    Task<ToolObservation> InvokeWithAttachmentsAsync(string argumentsJson, CancellationToken ct = default);
}

/// <summary>What an <see cref="IAttachmentTool"/> answered.</summary>
/// <param name="Text">The observation fed back as the call's result, exactly as <see cref="ITool.InvokeAsync"/>
/// would return it.</param>
/// <param name="Attachments">The images for the model; null or empty carries none.</param>
public sealed record ToolObservation(string Text, IReadOnlyList<TextAttachment>? Attachments = null);
