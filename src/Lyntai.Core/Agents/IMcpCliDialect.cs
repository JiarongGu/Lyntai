namespace Lyntai.Agents;

/// <summary>
/// The vendor-specific half of <see cref="ICliToolProvisioner"/>: how ONE CLI is told to connect to a
/// running MCP tool endpoint. Everything else about hosting the app's <see cref="ITool"/>s — starting the
/// server, minting the bearer token, writing and deleting temp files, tearing down after the process
/// exits — is provider-neutral and belongs to the host package; a dialect contributes only the argv and
/// config-file shapes.
///
/// <para>The variation across CLIs is structural, not cosmetic, which is why this is an interface rather
/// than a format string: flag names differ, and the config file is JSON for some CLIs and TOML for others.
/// Implementing one is the whole cost of supporting a new CLI — there is no new package and no change to
/// the host.</para>
///
/// <para>Lives in Core so a provider package can ship its own dialect WITHOUT taking a dependency on the
/// MCP host (a loopback <c>HttpListener</c> server that pulls in <c>ModelContextProtocol.Core</c>) — that
/// separation is the point of the <see cref="ICliToolProvisioner"/> seam.</para>
/// </summary>
public interface IMcpCliDialect
{
    /// <summary>The <see cref="Llm.ILlmProvider"/>'s <see cref="Lyntai.Lifecycle.IProviderIdentity.Id"/> this
    /// dialect configures (e.g. <c>claude-cli</c>).
    /// The provisioner is registered keyed on this, so several CLI providers can host tools side by side
    /// with different dialects.</summary>
    string ProviderId { get; }

    /// <summary>Produce the extra process args that point this CLI at <paramref name="context"/>'s live
    /// endpoint. Any config file the CLI needs is written through
    /// <see cref="McpCliContext.WriteTempFile"/> so the caller can delete it when the session ends —
    /// never write one directly.</summary>
    ValueTask<IReadOnlyList<string>> BuildArgsAsync(McpCliContext context, CancellationToken ct = default);
}

/// <summary>The running tool host a CLI should connect to: its <paramref name="Url"/>, the bearer token
/// every request must carry, and the MCP server name it is published under (which some CLIs also use to
/// build tool-permission patterns, e.g. <c>mcp__&lt;server&gt;__*</c>).</summary>
public sealed record McpEndpoint(string Url, string AuthToken, string ServerName);

/// <summary>What <see cref="IMcpCliDialect.BuildArgsAsync"/> is handed: the live
/// <see cref="Endpoint"/> plus a temp-file writer whose output the caller tracks and deletes.</summary>
/// <param name="endpoint">The running host the CLI should connect to.</param>
/// <param name="writeTempFile">Given a short <c>kind</c> tag and the file's content, writes a temp file
/// and returns its path. Supplied by the host so file permissions and cleanup stay in ONE place.</param>
public sealed class McpCliContext(McpEndpoint endpoint, Func<string, string, string> writeTempFile)
{
    /// <summary>The running host the CLI should connect to.</summary>
    public McpEndpoint Endpoint => endpoint;

    /// <summary>Write a config file for the CLI to read and get its path back. The file is created with
    /// owner-only permissions (it typically carries <see cref="McpEndpoint.AuthToken"/>) and is deleted
    /// when the <see cref="CliToolSession"/> is disposed.</summary>
    /// <param name="kind">A short tag used in the file name, e.g. <c>mcp</c> or <c>settings</c>.</param>
    /// <param name="content">The file's full content.</param>
    public string WriteTempFile(string kind, string content) => writeTempFile(kind, content);
}
