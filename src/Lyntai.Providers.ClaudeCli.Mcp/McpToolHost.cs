using Lyntai.Agents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Lyntai.Providers.ClaudeCli.Mcp;

/// <summary>
/// An ephemeral, localhost-only HTTP MCP server (Kestrel) exposing the given <see cref="ITool"/>s as MCP
/// tools under the server name <c>lyntai</c>. Started on an OS-assigned port and stopped on dispose —
/// it lives only for the duration of one claude-CLI invocation, so nothing is exposed beyond that call.
/// </summary>
internal sealed class McpToolHost : IAsyncDisposable
{
    public const string ServerName = "lyntai";

    private readonly WebApplication _app;

    private McpToolHost(WebApplication app, string url)
    {
        _app = app;
        Url = url;
    }

    /// <summary>The endpoint the CLI's MCP client connects to, e.g. <c>http://127.0.0.1:PORT/mcp</c>.</summary>
    public string Url { get; }

    public static async Task<McpToolHost> StartAsync(IReadOnlyList<ITool> tools, CancellationToken ct = default)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();                 // stay silent — this is an internal transport
        builder.WebHost.UseUrls("http://127.0.0.1:0");    // 0 → OS assigns a free port

        builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation { Name = ServerName, Version = "1.0.0" };
            options.ToolCollection ??= [];
            foreach (var tool in tools)
                options.ToolCollection.Add(McpServerTool.Create(new ToolFunction(tool)));
        }).WithHttpTransport();

        var app = builder.Build();
        app.MapMcp("/mcp");
        await app.StartAsync(ct).ConfigureAwait(false);

        // after Start, Urls reflects the actual bound address (with the assigned port)
        var address = app.Urls.First().TrimEnd('/');
        return new McpToolHost(app, $"{address}/mcp");
    }

    public async ValueTask DisposeAsync()
    {
        try { await _app.StopAsync().ConfigureAwait(false); } catch { /* best-effort shutdown */ }
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}
