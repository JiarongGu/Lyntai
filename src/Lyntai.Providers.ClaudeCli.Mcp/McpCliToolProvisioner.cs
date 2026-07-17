using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Lyntai.Agents;

namespace Lyntai.Providers.ClaudeCli.Mcp;

/// <summary>
/// <see cref="ICliToolProvisioner"/> for the claude CLI: on each invocation it stands up an
/// <see cref="McpToolHost"/> exposing the registered <see cref="ITool"/>s, writes the temp
/// <c>--mcp-config</c> (pointing the CLI at the host) and a <c>--settings</c> allow-list, and returns the
/// CLI args + a session that stops the host and deletes the temp files. With no tools registered it's a
/// no-op (no host, no args), so the CLI runs exactly as before.
/// </summary>
internal sealed class McpCliToolProvisioner(IEnumerable<ITool> tools) : ICliToolProvisioner
{
    public async Task<CliToolSession> ProvisionAsync(CancellationToken ct = default)
    {
        var toolList = tools.ToList();
        if (toolList.Count == 0) return new CliToolSession([]);

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); // per-host bearer
        var host = await McpToolHost.StartAsync(toolList, token, ct).ConfigureAwait(false);
        string? mcpConfigPath = null, settingsPath = null;
        try
        {
            mcpConfigPath = WriteTemp("mcp", McpConfigJson(host.Url, token));
            settingsPath = WriteTemp("settings", SettingsJson());

            // allow-list ONLY our server's tools so they run non-interactively in print mode; built-ins stay off
            string[] args = ["--mcp-config", mcpConfigPath, "--settings", settingsPath, "--allowedTools", $"mcp__{McpToolHost.ServerName}__*"];

            return new CliToolSession(args, async () =>
            {
                await host.DisposeAsync().ConfigureAwait(false);
                TryDelete(mcpConfigPath);
                TryDelete(settingsPath);
            });
        }
        catch
        {
            // never leak the started host (or a half-written temp file) if writing config throws
            await host.DisposeAsync().ConfigureAwait(false);
            if (mcpConfigPath is not null) TryDelete(mcpConfigPath);
            if (settingsPath is not null) TryDelete(settingsPath);
            throw;
        }
    }

    internal static string McpConfigJson(string url, string authToken) => new JsonObject
    {
        ["mcpServers"] = new JsonObject
        {
            [McpToolHost.ServerName] = new JsonObject
            {
                ["type"] = "http",
                ["url"] = url,
                ["headers"] = new JsonObject { ["Authorization"] = $"Bearer {authToken}" },
            },
        },
    }.ToJsonString();

    internal static string SettingsJson() => new JsonObject
    {
        ["permissions"] = new JsonObject
        {
            ["allow"] = new JsonArray($"mcp__{McpToolHost.ServerName}__*"),
        },
    }.ToJsonString();

    private static string WriteTemp(string kind, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lyntai-{kind}-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* temp file — OK if it lingers */ }
    }
}
