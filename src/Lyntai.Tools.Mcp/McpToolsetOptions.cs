namespace Lyntai.Tools.Mcp;

/// <summary>How <see cref="McpToolset.FromClientAsync(ModelContextProtocol.Client.McpClient, McpToolsetOptions, CancellationToken)"/>
/// adapts a server's tools. The defaults are today's: every result reaches the model as text.</summary>
public sealed class McpToolsetOptions
{
    /// <summary>The largest image, in decoded bytes, handed to the model beside the observation; <c>0</c>, the
    /// default, hands none. Opting in sends each image block at or under it as an attachment, which a
    /// vision-capable provider shows the model and a text-only one drops. The line naming the block stays in the
    /// text either way, and an image over the cap, or one whose data does not decode, is that line alone. Audio
    /// and blobs are always lines. Every image is billed as input on each later turn of the run.</summary>
    public long MaxImageBytes { get; set; }
}
