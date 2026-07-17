using Lyntai.Agents;
using Lyntai.Tools.Mcp;
using ModelContextProtocol.Client;

namespace Lyntai.Tests.Tools;

/// <summary>
/// OPT-IN end-to-end proof against a REAL MCP server: spawns the reference
/// <c>@modelcontextprotocol/server-everything</c> over stdio (via npx), lists its tools through
/// <see cref="McpToolset"/>, and actually calls one — proving the adapter works against a live server,
/// not just constructed DTOs. Runs only when <c>LYNTAI_LIVE_MCP</c> is set (and npx can fetch the
/// server); otherwise a no-op pass. xUnit v2 has no dynamic <c>Assert.Skip</c>, hence the early return.
///
/// Enable:  set LYNTAI_LIVE_MCP=1   (Node/npx must be on PATH; first run downloads the server)
/// </summary>
public class McpLiveTests
{
    private static bool Live => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LYNTAI_LIVE_MCP"));

    private static StdioClientTransport EverythingServer() => new(new StdioClientTransportOptions
    {
        Command = "npx",
        Arguments = ["-y", "@modelcontextprotocol/server-everything"],
        Name = "everything",
    });

    [Fact]
    public async Task Lists_and_calls_a_real_mcp_servers_tool()
    {
        if (!Live) return;

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2)); // first run fetches the server
        await using var client = await McpClient.CreateAsync(EverythingServer(), cancellationToken: cts.Token);

        var tools = await McpToolset.FromClientAsync(client, cts.Token);

        Assert.NotEmpty(tools);
        var echo = tools.First(t => t.Name == "echo"); // the reference server exposes an "echo" tool
        var result = await echo.InvokeAsync("""{"message":"hello mcp"}""", cts.Token);

        Assert.Contains("hello mcp", result); // the server echoed our message back through the adapter
    }
}
