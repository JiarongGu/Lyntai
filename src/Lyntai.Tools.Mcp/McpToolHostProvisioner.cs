using System.Security.Cryptography;
using Lyntai.Agents;
using Lyntai.Processes;

namespace Lyntai.Tools.Mcp.Hosting;

/// <summary>
/// The provider-neutral <see cref="ICliToolProvisioner"/>: on each CLI invocation it mints a bearer token,
/// stands up an <see cref="McpToolHost"/> exposing the registered <see cref="ITool"/>s its consumer is mapped to in
/// <see cref="McpToolHostOptions.ToolsByConsumer"/> (all of them when unmapped), asks the
/// <see cref="IMcpCliConnector"/> for the CLI args that point at it, and returns a session that stops the
/// host and deletes every temp file the connector wrote. With no tools registered it's a no-op (no host,
/// no args, connector never consulted), so the CLI runs exactly as before.
/// </summary>
internal sealed class McpToolHostProvisioner(
    IEnumerable<ITool> tools, IMcpCliConnector connector, McpToolHostOptions options,
    Lyntai.Guards.IGuardRail? guards = null,
    Microsoft.Extensions.Logging.ILogger<McpToolHostProvisioner>? logger = null) : ICliToolProvisioner
{
    // the DI tool collection is fixed once the container is built, so it is read — and checked — once
    private readonly IReadOnlyList<ITool> _registered = Registered(tools, options);

    public Task<CliToolSession> ProvisionAsync(CancellationToken ct = default) => HostAsync(_registered, ct);

    public Task<CliToolSession> ProvisionAsync(CliToolRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return HostAsync(options.ToolsByConsumer.TryGetValue(request.Request.Consumer, out var names)
            ? [.. _registered.Where(t => names.Contains(t.Name, StringComparer.Ordinal))]
            : _registered, ct);
    }

    /// <summary>The registered tools, after refusing a <see cref="McpToolHostOptions.ToolsByConsumer"/> name none of
    /// them has — a typo would otherwise host a smaller set than configured, silently.</summary>
    private static IReadOnlyList<ITool> Registered(IEnumerable<ITool> tools, McpToolHostOptions options)
    {
        var list = tools.ToList();
        var unknown = options.ToolsByConsumer.Values.SelectMany(n => n)
            .Where(n => !list.Any(t => string.Equals(t.Name, n, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException(
                $"McpToolHostOptions.ToolsByConsumer names {string.Join(", ", unknown.Select(n => $"'{n}'"))}, which no "
                + $"registered ITool has; registered: {string.Join(", ", list.Select(t => t.Name))}.");
        return list;
    }

    private async Task<CliToolSession> HostAsync(IReadOnlyList<ITool> toolList, CancellationToken ct)
    {
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
