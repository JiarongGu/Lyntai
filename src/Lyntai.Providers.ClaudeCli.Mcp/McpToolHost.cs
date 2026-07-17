using Lyntai.Agents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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

    /// <summary>Start the host. <paramref name="authToken"/> is required as a bearer token on every
    /// request — the endpoint EXECUTES the app's tools, so even on loopback another local process must
    /// not be able to invoke them.</summary>
    public static async Task<McpToolHost> StartAsync(IReadOnlyList<ITool> tools, string authToken, CancellationToken ct = default)
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
        try
        {
            // gate every request on the per-host bearer token before it reaches the MCP endpoint
            var expected = $"Bearer {authToken}";
            app.Use(async (ctx, next) =>
            {
                if (!string.Equals(ctx.Request.Headers.Authorization.ToString(), expected, StringComparison.Ordinal))
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
                await next().ConfigureAwait(false);
            });
            app.MapMcp("/mcp");
            await app.StartAsync(ct).ConfigureAwait(false);

            // after Start, Urls reflects the actual bound address (127.0.0.1 + the assigned port)
            var address = (app.Urls.FirstOrDefault()
                ?? throw new InvalidOperationException("MCP host bound no address")).TrimEnd('/');
            return new McpToolHost(app, $"{address}/mcp");
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(false); // never leak a built/partly-started host
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await _app.StopAsync().ConfigureAwait(false); } catch { /* best-effort shutdown */ }
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}
