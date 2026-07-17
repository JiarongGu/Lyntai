using Lyntai;
using Lyntai.Agents;
using Lyntai.Llm;
using Lyntai.Tests.Fakes;
using Lyntai.Tools.Mcp;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace Lyntai.Tests.Tools;

/// <summary>The MCP→ITool adapter, tested without a real server: the call is a delegate seam so the
/// SDK's concrete client stays out of the way; the result-flattening is tested against constructed
/// protocol DTOs; and AddMcpTools is proven end-to-end through the tool loop.</summary>
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
    public async Task AddMcpTools_registers_them_so_the_loop_can_call_them()
    {
        var mcpTools = new ITool[]
        {
            new McpTool("shout", "uppercases its args", null, (args, _) => Task.FromResult(args.ToUpperInvariant())),
        };

        // FakeLlmProvider (no native tools) → the loop takes the prompt path; script its protocol turns
        var provider = new FakeLlmProvider("p");
        provider.Replies.Enqueue(new LlmReply("""{"tool":"shout","arguments":{"s":"hi"}}""", LlmVerdict.Ok));
        provider.Replies.Enqueue(new LlmReply("""{"final":"done"}""", LlmVerdict.Ok));

        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddProvider(_ => provider).AddMcpTools(mcpTools).DefaultCandidates("p"));
        using var sp = services.BuildServiceProvider();

        Assert.Contains(sp.GetRequiredService<IToolRegistry>().Tools, t => t.Name == "shout");

        var result = await sp.GetRequiredService<IToolLoop>()
            .RunAsync(new LlmRequest { Messages = [LlmMessage.User("shout hi")] });

        Assert.True(result.Ok);
        Assert.Equal("done", result.Answer);
        Assert.Equal("shout", Assert.Single(result.Steps).Tool);
    }
}
