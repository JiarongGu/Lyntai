using Lyntai.Inference;
using Lyntai;
using Lyntai.Agents;
using Lyntai.Tests.Fakes;
using Lyntai.Tools.Mcp;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace Lyntai.Tests.Tools;

/// <summary>The MCP→ITool adapter, tested without a real server: the call is a delegate seam so the
/// SDK's concrete client stays out of the way; the result-flattening is tested against constructed
/// protocol result types; and AddMcpTools is proven end-to-end through the tool loop.</summary>
public class McpToolsTests
{
    [Fact]
    public async Task McpTool_adapts_name_schema_and_delegates_the_call()
    {
        var tool = new McpTool("echo", "echoes", """{"type":"object"}""",
            (args, _) => Task.FromResult($"got:{args}"));

        Assert.Equal("echo", tool.Name);
        Assert.Equal("echoes", tool.Description);
        Assert.Equal("""{"type":"object"}""", tool.ParametersJsonSchema);
        Assert.Equal("""got:{"x":1}""", await tool.InvokeAsync("""{"x":1}"""));
    }

    [Fact]
    public void ToText_joins_text_blocks_and_flags_server_errors()
    {
        var ok = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "hello" }, new TextContentBlock { Text = "world" }],
        };
        Assert.Equal("hello\nworld", McpToolset.ToText(ok));

        var err = new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = "boom" }] };
        Assert.Equal("error: boom", McpToolset.ToText(err));
    }

    [Fact]
    public void ToText_reads_an_embedded_text_resource_and_names_a_resource_link_beside_the_text()
    {
        // a file-read tool answers its content as an embedded resource, and a listing answers links: a result
        // that held any text block used to drop both, and a resource alone read as "[resource content]"
        var mixed = new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = "read ok" },
                new EmbeddedResourceBlock { Resource = new TextResourceContents { Uri = "file:///a.txt", Text = "the file body" } },
                new ResourceLinkBlock { Uri = "file:///b.txt", Name = "b.txt" },
            ],
        };
        Assert.Equal("read ok\nthe file body\n[resource link: b.txt file:///b.txt]", McpToolset.ToText(mixed));

        var resourceOnly = new CallToolResult
        {
            Content = [new EmbeddedResourceBlock { Resource = new TextResourceContents { Uri = "file:///a.txt", Text = "the file body" } }],
        };
        Assert.Equal("the file body", McpToolset.ToText(resourceOnly));
    }

    [Fact]
    public void ToText_describes_each_non_text_block_in_order_beside_the_text_with_its_media_type_and_size()
    {
        // a chart-drawing tool answers text AND a picture: the picture used to vanish whenever any text came with it
        var result = new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = "chart" },
                ImageContentBlock.FromBytes(new byte[1500], "image/png"),
                new TextContentBlock { Text = "and a clip" },
                AudioContentBlock.FromBytes(new byte[3], "audio/wav"),
                new EmbeddedResourceBlock { Resource = BlobResourceContents.FromBytes(new byte[2048], "file:///a.pdf", "application/pdf") },
            ],
        };

        Assert.Equal(
            "chart\n[image content: image/png, 1500 bytes]\nand a clip\n[audio content: audio/wav, 3 bytes]\n" +
            "[resource content: file:///a.pdf, application/pdf, 2048 bytes]",
            McpToolset.ToText(result));
    }

    [Fact]
    public void ToText_describes_a_picture_alone_and_keeps_structured_content_ahead_of_it()
    {
        var alone = new CallToolResult { Content = [ImageContentBlock.FromBytes(new byte[10], "image/jpeg")] };
        Assert.Equal("[image content: image/jpeg, 10 bytes]", McpToolset.ToText(alone));

        var structured = new CallToolResult
        {
            Content = [ImageContentBlock.FromBytes(new byte[10], "image/jpeg")],
            StructuredContent = System.Text.Json.JsonDocument.Parse("""{"w":4}""").RootElement,
        };
        Assert.Equal("""{"w":4}""" + "\n[image content: image/jpeg, 10 bytes]", McpToolset.ToText(structured));

        var failed = new CallToolResult { IsError = true, Content = [ImageContentBlock.FromBytes(new byte[1], "image/png")] };
        Assert.Equal("error: [image content: image/png, 1 byte]", McpToolset.ToText(failed));
    }

    [Fact]
    public void An_image_block_is_attached_when_the_host_opts_in()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var result = new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = "chart" },
                ImageContentBlock.FromBytes(png, "image/png"),
                AudioContentBlock.FromBytes(new byte[3], "audio/wav"),
                new EmbeddedResourceBlock { Resource = BlobResourceContents.FromBytes(new byte[2], "file:///a.bin", "image/png") },
            ],
        };

        var observation = McpToolset.ToObservation(result, new McpToolsetOptions { MaxImageBytes = 1024 });

        // the line stays, so a model that is not shown the picture is still told of it; audio and a blob stay lines
        Assert.Equal(McpToolset.ToText(result), observation.Text);
        var image = Assert.Single(observation.Attachments!);
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal(png, image.Data);
    }

    [Fact]
    public void An_image_block_over_the_cap_is_described()
    {
        var small = new byte[10];
        var result = new CallToolResult
        {
            Content =
            [
                ImageContentBlock.FromBytes(new byte[2048], "image/png"),
                new ImageContentBlock { Data = "not base64!"u8.ToArray(), MimeType = "image/png" },
                ImageContentBlock.FromBytes(small, "image/jpeg"),
            ],
        };

        var observation = McpToolset.ToObservation(result, new McpToolsetOptions { MaxImageBytes = 1024 });

        Assert.Equal(
            "[image content: image/png, 2048 bytes]\n[image content: image/png, size unknown]\n[image content: image/jpeg, 10 bytes]",
            observation.Text);
        Assert.Equal("image/jpeg", Assert.Single(observation.Attachments!).MediaType);   // only the one that fits
    }

    [Fact]
    public void An_image_block_by_default_is_text_only()
    {
        var result = new CallToolResult { Content = [ImageContentBlock.FromBytes(new byte[10], "image/png")] };

        var observation = McpToolset.ToObservation(result, new McpToolsetOptions());

        Assert.Equal("[image content: image/png, 10 bytes]", observation.Text);
        Assert.True(observation.Attachments is null or { Count: 0 });
    }

    [Fact]
    public async Task McpTool_answers_the_text_door_and_the_attachment_door_alike()
    {
        var image = new TextAttachment("image/png", [1]);
        ITool tool = new McpTool("draw", null, null,
            (_, _) => Task.FromResult(new ToolObservation("[image content: image/png, 1 byte]", [image])));

        var attachments = Assert.IsAssignableFrom<IAttachmentTool>(tool);
        Assert.Equal("[image content: image/png, 1 byte]", await tool.InvokeAsync("{}"));
        var observation = await attachments.InvokeWithAttachmentsAsync("{}");
        Assert.Equal("[image content: image/png, 1 byte]", observation.Text);
        Assert.Same(image, Assert.Single(observation.Attachments!));
    }

    [Fact]
    public async Task FromClientAsync_with_options_adapts_each_tool_as_one_that_can_carry_images()
    {
        // a text-only server: the overload wires the same call, behind the attachment door
        ITool echo = new FunctionTool("echo", (args, _) => Task.FromResult($"echoed:{args}"), "echoes",
            """{"type":"object","properties":{"message":{"type":"string"}}}""");
        const string token = "test-bearer-token";
        await using var host = await Lyntai.Tools.Mcp.Hosting.McpToolHost.StartAsync([echo], token);
        await using var client = await ModelContextProtocol.Client.McpClient.CreateAsync(
            new ModelContextProtocol.Client.HttpClientTransport(new ModelContextProtocol.Client.HttpClientTransportOptions
            {
                Endpoint = new Uri(host.Url),
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
            }));

        var tool = Assert.IsAssignableFrom<IAttachmentTool>(Assert.Single(
            await McpToolset.FromClientAsync(client, new McpToolsetOptions { MaxImageBytes = 1024 })));

        var observation = await tool.InvokeWithAttachmentsAsync("""{"message":"hi"}""");
        Assert.Contains("echoed", observation.Text);
        Assert.True(observation.Attachments is null or { Count: 0 });
    }

    [Fact]
    public async Task AddMcpTools_registers_them_so_the_loop_can_call_them()
    {
        var mcpTools = new ITool[]
        {
            new McpTool("shout", "uppercases its args", null, (args, _) => Task.FromResult(args.ToUpperInvariant())),
        };

        // FakeTextProvider (no native tools) → the loop takes the prompt path; script its protocol turns
        var provider = new FakeTextProvider("p");
        provider.Replies.Enqueue(new TextResponse("""{"tool":"shout","arguments":{"s":"hi"}}""", ProviderVerdict.Ok));
        provider.Replies.Enqueue(new TextResponse("""{"final":"done"}""", ProviderVerdict.Ok));

        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddProvider(_ => provider).AddMcpTools(mcpTools).UseDefaultCandidates("p"));
        using var sp = services.BuildServiceProvider();

        Assert.Contains(sp.GetRequiredService<IToolRegistry>().Tools, t => t.Name == "shout");

        var result = await sp.GetRequiredService<IToolLoop>()
            .RunAsync(new TextRequest { Messages = [TextMessage.User("shout hi")] });

        Assert.True(result.Ok);
        Assert.Equal("done", result.Answer);
        Assert.Equal("shout", Assert.Single(result.Steps).Tool);
    }

    [Fact]
    public void AddMcpTools_takes_tools_inline_as_well_as_a_sequence()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeTextProvider("p"))
            .AddMcpTools(
                new McpTool("alpha", null, null, (args, _) => Task.FromResult(args)),
                new McpTool("beta", null, null, (args, _) => Task.FromResult(args)))
            .UseDefaultCandidates("p"));
        using var sp = services.BuildServiceProvider();

        var names = sp.GetRequiredService<IToolRegistry>().Tools.Select(t => t.Name).ToArray();
        Assert.Contains("alpha", names);
        Assert.Contains("beta", names);
    }
}
