using Lyntai.Inference;
namespace Lyntai.Agents;

/// <summary>
/// Provisions access to the registered <see cref="ITool"/>s for a CLI-spawning provider whose model
/// runs its OWN agent loop (e.g. the <c>claude</c> CLI, which can't hand tool calls back to the caller
/// and reaches custom tools only over MCP). An implementation stands up whatever the CLI needs — for the
/// claude CLI, an in-process HTTP MCP server exposing the tools plus a temp <c>--mcp-config</c> — and
/// returns the extra process args, with a session that tears it all down afterward.
///
/// Registered as an OPTIONAL DI service (via an add-on package); when absent, CLI providers behave
/// exactly as before (no tools). This seam keeps host/transport dependencies out of the base provider.
/// </summary>
public interface ICliToolProvisioner
{
    /// <summary>Stand up tool access for one CLI invocation without seeing the call. The engine no longer calls
    /// it; it stays because a provisioner written before the request-aware member implements only this one, and
    /// that member's default forwards here — so a provisioner that overrides both answers here as if no call were
    /// known, typically with every tool. Dispose the returned session after the process exits to release the host
    /// and temp files.</summary>
    Task<CliToolSession> ProvisionAsync(CancellationToken ct = default);

    /// <summary>Stand up tool access for one CLI invocation, seeing the call it serves — what the engine calls.
    /// Defaults to <see cref="ProvisionAsync(CancellationToken)"/>, so a provisioner written before this member
    /// still runs; override this one to choose per call (the shipped MCP host reads the request's consumer).
    /// <para><b>A decorator must forward BOTH members.</b> One that forwards only the request-blind member takes
    /// this default, so every call reaches its inner provisioner request-blind and a per-call choice such as
    /// <c>McpToolHostOptions.ToolsByConsumer</c> silently stops applying.</para></summary>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    Task<CliToolSession> ProvisionAsync(CliToolRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProvisionAsync(ct);
    }
}

/// <summary>The result of <see cref="ICliToolProvisioner.ProvisionAsync(CliToolRequest, CancellationToken)"/>:
/// the extra CLI args the spawn needs (e.g. <c>--mcp-config &lt;file&gt;</c>), and an async-disposable that
/// tears the host and temp files down. <paramref name="dispose"/> runs on <see cref="DisposeAsync"/>.
/// <para><b>These are HANDED TO THE BACKEND, never appended to its argv</b> — they reach
/// <see cref="Lyntai.Inference.Cli.ICliBackend.BuildCompletionArgs(Lyntai.Inference.TextRequest, IReadOnlyList{string})"/>
/// and the backend decides where they may legally go (<c>docs/DECISIONS.md</c> D65). Only the backend knows
/// its own argv grammar: claude's ends in options, so appending is right there, while codex's ends in the
/// <c>-</c> stdin positional, where anything after it is read as PROMPT TEXT — and on that CLI a swallowed
/// flag is a SPENT TURN rather than an error.</para></summary>
public sealed class CliToolSession(IReadOnlyList<string> extraArgs, Func<ValueTask>? dispose = null) : IAsyncDisposable
{
    public IReadOnlyList<string> ExtraArgs => extraArgs;

    public ValueTask DisposeAsync() => dispose?.Invoke() ?? ValueTask.CompletedTask;
}

/// <summary>What <see cref="ICliToolProvisioner.ProvisionAsync(CliToolRequest, CancellationToken)"/> is asked
/// for: the call being served and which CLI is spawning for it — one unkeyed provisioner may serve several CLIs.
/// A record, so a later field is an additive property rather than another overload.</summary>
/// <param name="Request">The call the spawn serves.</param>
/// <param name="BackendId">Which CLI is spawning — <c>claude-cli</c>, <c>codex-cli</c> — the SAME for every
/// registration of that CLI, so it is not the registration's id; a per-registration choice keys the provisioner
/// itself.</param>
public sealed record CliToolRequest(TextRequest Request, string BackendId);
