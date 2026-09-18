using System.Text;
using Lyntai;
using Lyntai.Llm;
using Lyntai.Providers.Http.Payloads;
using Lyntai.Tests.Fakes;
using Lyntai.Inference;

namespace Lyntai.Tests.Providers;

/// <summary>Vision/multimodal: attachments on a message render as image content parts on the OpenAI HTTP
/// payload and map to MEAI image content through the bridge.</summary>
public class MultimodalTests
{
    private static readonly byte[] Png = Encoding.UTF8.GetBytes("fake-png-bytes");

    [Fact]
    public void UserWithImage_carries_the_inline_attachment()
    {
        var m = TextMessage.UserWithImage("what is this?", Png, "image/png");
        var a = Assert.Single(m.Attachments!);
        Assert.Equal("image/png", a.MediaType);
        Assert.StartsWith("data:image/png;base64,", a.DataUrl());
    }

    [Fact]
    public void Openai_payload_renders_text_then_image_url_parts()
    {
        var req = new TextRequest { Messages = [TextMessage.UserWithImage("describe", Png, "image/png")] };

        var msg = OpenAiPayload.Build(req, "gpt-4o", stream: false)["messages"]!.AsArray()[0]!;
        var parts = msg["content"]!.AsArray();

        Assert.Equal("text", (string)parts[0]!["type"]!);
        Assert.Equal("describe", (string)parts[0]!["text"]!);
        Assert.Equal("image_url", (string)parts[1]!["type"]!);
        Assert.StartsWith("data:image/png;base64,", (string)parts[1]!["image_url"]!["url"]!);
    }

    [Fact]
    public void Openai_payload_uses_a_remote_image_url_when_given()
    {
        var req = new TextRequest { Messages = [TextMessage.UserWithImageUrl("describe", "https://example.com/i.jpg")] };
        var parts = OpenAiPayload.Build(req, "m", stream: false)["messages"]!.AsArray()[0]!["content"]!.AsArray();
        Assert.Equal("https://example.com/i.jpg", (string)parts[1]!["image_url"]!["url"]!);
    }

    [Fact]
    public void Attachment_with_neither_data_nor_uri_throws_rather_than_send_empty()
    {
        Assert.Throws<InvalidOperationException>(() => new TextAttachment("image/png").Url());
    }

    [Fact]
    public void Openai_drops_images_on_a_non_user_role()
    {
        // OpenAI rejects image parts on assistant/system; the payload must fall back to plain text
        var req = new TextRequest
        {
            Messages = [new TextMessage("assistant", "sure") { Attachments = [new TextAttachment("image/png", Png)] }],
        };
        var content = OpenAiPayload.Build(req, "m", stream: false)["messages"]!.AsArray()[0]!["content"];
        Assert.Equal("sure", (string)content!); // a plain string, not a parts array
    }
}
