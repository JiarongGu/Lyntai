using System.Text;
using Lyntai;
using Lyntai.Llm;
using Lyntai.Providers.ExtensionsAi;
using Lyntai.Providers.OpenAiCompatible.Payloads;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.AI;

namespace Lyntai.Tests.Providers;

/// <summary>Vision/multimodal: attachments on a message render as image content parts on the OpenAI HTTP
/// payload and map to MEAI image content through the bridge.</summary>
public class MultimodalTests
{
    private static readonly byte[] Png = Encoding.UTF8.GetBytes("fake-png-bytes");

    [Fact]
    public void UserWithImage_carries_the_inline_attachment()
    {
        var m = LlmMessage.UserWithImage("what is this?", Png, "image/png");
        var a = Assert.Single(m.Attachments!);
        Assert.Equal("image/png", a.MediaType);
        Assert.StartsWith("data:image/png;base64,", a.DataUrl());
    }

    [Fact]
    public void Openai_payload_renders_text_then_image_url_parts()
    {
        var req = new LlmRequest { Messages = [LlmMessage.UserWithImage("describe", Png, "image/png")] };

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
        var req = new LlmRequest { Messages = [LlmMessage.UserWithImageUrl("describe", "https://example.com/i.jpg")] };
        var parts = OpenAiPayload.Build(req, "m", stream: false)["messages"]!.AsArray()[0]!["content"]!.AsArray();
        Assert.Equal("https://example.com/i.jpg", (string)parts[1]!["image_url"]!["url"]!);
    }

    [Fact]
    public async Task Meai_bridge_maps_attachments_to_image_content()
    {
        var client = new FakeChatClient();
        var provider = new ExtensionsAiProvider("meai", client, new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(30) });

        await provider.CompleteAsync(new LlmRequest { Messages = [LlmMessage.UserWithImage("hi", Png, "image/png")] });

        var contents = client.Calls.Single().Messages.Single().Contents;
        Assert.Contains(contents, c => c is TextContent);
        Assert.Contains(contents, c => c is DataContent);
    }
}
