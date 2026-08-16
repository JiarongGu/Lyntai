using System.Security.Cryptography;
using Lyntai.Agents;

namespace Lyntai.Tools.Mcp.Hosting;

/// <summary>
/// The provider-neutral <see cref="ICliToolProvisioner"/>: on each CLI invocation it mints a bearer token,
/// stands up an <see cref="McpToolHost"/> exposing the registered <see cref="ITool"/>s, asks the
/// <see cref="IMcpCliDialect"/> for the CLI args that point at it, and returns a session that stops the
/// host and deletes every temp file the dialect wrote. With no tools registered it's a no-op (no host,
/// no args, dialect never consulted), so the CLI runs exactly as before.
/// </summary>
internal sealed class McpToolHostProvisioner(
    IEnumerable<ITool> tools, IMcpCliDialect dialect, McpToolHostOptions options,
    Lyntai.Guards.IGuardRail? guards = null) : ICliToolProvisioner
{
    public async Task<CliToolSession> ProvisionAsync(CancellationToken ct = default)
    {
        var toolList = tools.ToList();
        if (toolList.Count == 0) return new CliToolSession([]);

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); // per-host bearer
        // the rail travels with the tools: this host is a second door onto the same instances the in-process
        // tool loop runs, and a guard enforced on one door only is not enforced
        var host = await McpToolHost.StartAsync(toolList, token, options, guards, ct).ConfigureAwait(false);

        // every path the dialect asks for is tracked HERE, so cleanup can't be forgotten by a dialect
        var tempFiles = new List<string>();
        try
        {
            var context = new McpCliContext(
                new McpEndpoint(host.Url, token, options.ServerName),
                (kind, content) =>
                {
                    var path = WriteTemp(kind, content);
                    tempFiles.Add(path);
                    return path;
                });

            var args = await dialect.BuildArgsAsync(context, ct).ConfigureAwait(false);

            return new CliToolSession(args, async () =>
            {
                await host.DisposeAsync().ConfigureAwait(false);
                foreach (var path in tempFiles) TryDelete(path);
            });
        }
        catch
        {
            // never leak the started host (or a half-written temp file) if the dialect throws
            await host.DisposeAsync().ConfigureAwait(false);
            foreach (var path in tempFiles) TryDelete(path);
            throw;
        }
    }

    private static string WriteTemp(string kind, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lyntai-{kind}-{Guid.NewGuid():N}.json");
        // a CLI config file typically carries the loopback bearer token — create OWNER-ONLY on Unix so
        // another local user can't read the token and drive the tool host during the CLI window (Windows
        // %TEMP% is already per-user ACL'd; UnixCreateMode throws there)
        //
        // TWIN: `CliTempFile.Write` in Lyntai.Providers.Default does the same for an agent session's
        // --mcp-config document. It cannot be shared — a provider package must never reference this one
        // (docs/DECISIONS.md D17) — so if the permission logic changes here, change it there too.
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var writer = new StreamWriter(new FileStream(path, options));
        writer.Write(content);
        return path;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* temp file — OK if it lingers */ }
    }
}
