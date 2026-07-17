namespace Lyntai.Agents;

/// <summary>
/// Provisions access to the registered <see cref="ITool"/>s for a CLI-spawning provider whose model
/// runs its OWN agent loop (e.g. the <c>claude</c> CLI, which can't hand tool calls back to the caller
/// and reaches custom tools only over MCP). An implementation stands up whatever the CLI needs — for the
/// claude CLI, an in-process HTTP MCP server exposing the tools plus a temp <c>--mcp-config</c> — and
/// returns the extra process args to pass, with a session that tears it all down afterward.
///
/// Registered as an OPTIONAL DI service (via an add-on package); when absent, CLI providers behave
/// exactly as before (no tools). This seam keeps host/transport dependencies out of the base provider.
/// </summary>
public interface ICliToolProvisioner
{
    /// <summary>Stand up tool access for one CLI invocation. Dispose the returned session after the
    /// process exits to release the host and temp files.</summary>
    Task<CliToolSession> ProvisionAsync(CancellationToken ct = default);
}

/// <summary>The result of <see cref="ICliToolProvisioner.ProvisionAsync"/>: the extra CLI args to append
/// to the spawn (e.g. <c>--mcp-config &lt;file&gt;</c>), and an async-disposable that tears the host and
/// temp files down. <paramref name="dispose"/> runs on <see cref="DisposeAsync"/>.</summary>
public sealed class CliToolSession(IReadOnlyList<string> extraArgs, Func<ValueTask>? dispose = null) : IAsyncDisposable
{
    public IReadOnlyList<string> ExtraArgs => extraArgs;

    public ValueTask DisposeAsync() => dispose?.Invoke() ?? ValueTask.CompletedTask;
}
