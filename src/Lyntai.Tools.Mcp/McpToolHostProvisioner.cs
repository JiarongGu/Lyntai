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
    // the tools and the map are read — and checked — once, when the provisioner is built: the DI tool collection
    // is fixed by then, and the map is COPIED, so a list the caller still holds cannot gain an unchecked name
    private readonly (IReadOnlyList<ITool> Tools, Dictionary<string, string[]> ByConsumer) _checked =
        Check(tools, options.ToolsByConsumer);

    public Task<CliToolSession> ProvisionAsync(CancellationToken ct = default) => HostAsync(_checked.Tools, ct);

    public Task<CliToolSession> ProvisionAsync(CliToolRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (registered, byConsumer) = _checked;
        // the tiering every *ByConsumer map here uses: the consumer's own entry, then "default", then every tool
        return HostAsync(byConsumer.TryGetValue(request.Request.Consumer, out var names)
            || byConsumer.TryGetValue(Lyntai.Inference.ProviderConsumers.Default, out names)
            ? [.. registered.Where(t => names.Contains(t.Name, StringComparer.Ordinal))]
            : registered, ct);
    }

    /// <summary>The registered tools and a copy of <see cref="McpToolHostOptions.ToolsByConsumer"/>, after refusing
    /// a null list and a name no registered tool has — naming the consumer key that held it, since a typo would
    /// otherwise host a smaller set than configured, silently.</summary>
    private static (IReadOnlyList<ITool>, Dictionary<string, string[]>) Check(
        IEnumerable<ITool> tools, Dictionary<string, IReadOnlyList<string>> toolsByConsumer)
    {
        var list = tools.ToList();
        var byConsumer = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        List<string> refused = [];
        foreach (var (consumer, names) in toolsByConsumer)
        {
            byConsumer[consumer] = names is null
                ? throw new InvalidOperationException($"McpToolHostOptions.ToolsByConsumer[\"{consumer}\"] is null; "
                    + "an empty list hosts none of the tools.")
                : [.. names];
            var unknown = byConsumer[consumer]
                .Where(n => !list.Any(t => string.Equals(t.Name, n, StringComparison.Ordinal)))
                .Distinct(StringComparer.Ordinal).ToList();
            if (unknown.Count > 0)
                refused.Add($"ToolsByConsumer[\"{consumer}\"] names {string.Join(", ", unknown.Select(n => $"'{n}'"))}");
        }
        if (refused.Count > 0)
            throw new InvalidOperationException(
                $"McpToolHostOptions.{string.Join("; ", refused)}, which no registered ITool has; registered: "
                + $"{string.Join(", ", list.Select(t => t.Name))}.");
        return (list, byConsumer);
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
