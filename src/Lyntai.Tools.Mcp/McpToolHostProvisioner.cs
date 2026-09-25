using System.Security.Cryptography;
using Lyntai.Agents;
using Lyntai.Processes;

namespace Lyntai.Tools.Mcp.Hosting;

/// <summary>
/// The provider-neutral <see cref="ICliToolProvisioner"/>: on each CLI invocation it mints a bearer token,
/// stands up an <see cref="McpToolHost"/> exposing the registered <see cref="ITool"/>s, asks the
/// <see cref="IMcpCliConnector"/> for the CLI args that point at it, and returns a session that stops the
/// host and deletes every temp file the connector wrote. With no tools registered it's a no-op (no host,
/// no args, connector never consulted), so the CLI runs exactly as before.
/// </summary>
internal sealed class McpToolHostProvisioner(
    IEnumerable<ITool> tools, IMcpCliConnector connector, McpToolHostOptions options,
    Lyntai.Guards.IGuardRail? guards = null,
    Microsoft.Extensions.Logging.ILogger<McpToolHostProvisioner>? logger = null) : ICliToolProvisioner
{
    public async Task<CliToolSession> ProvisionAsync(CancellationToken ct = default)
    {
        var toolList = tools.ToList();
        if (toolList.Count == 0) return new CliToolSession([]);

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); // per-host bearer
        // the rail travels with the tools: this host is a second door onto the same instances the in-process
        // tool loop runs, and a guard enforced on one door only is not enforced
        var host = await McpToolHost.StartAsync(toolList, token, options, guards, logger, ct).ConfigureAwait(false);

        // every path the connector asks for is tracked HERE, so cleanup can't be forgotten by a connector
        var tempFiles = new List<string>();
        try
        {
            var context = new McpCliContext(
                new McpEndpoint(host.Url, token, options.ServerName),
                (kind, content) =>
                {
                    // a connector file typically carries the loopback bearer token
                    var path = OwnerOnlyTempFile.Write(kind, content);
                    tempFiles.Add(path);
                    return path;
                });

            var args = await connector.BuildArgsAsync(context, ct).ConfigureAwait(false);

            return new CliToolSession(args, async () =>
            {
                await host.DisposeAsync().ConfigureAwait(false);
                foreach (var path in tempFiles) OwnerOnlyTempFile.TryDelete(path);
            });
        }
        catch
        {
            // never leak the started host (or a half-written temp file) if the connector throws
            await host.DisposeAsync().ConfigureAwait(false);
            foreach (var path in tempFiles) OwnerOnlyTempFile.TryDelete(path);
            throw;
        }
    }
}
