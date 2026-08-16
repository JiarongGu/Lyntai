namespace Lyntai.Llm.Cli;

/// <summary>
/// Everything about a spawned-CLI backend that is SPECIFIC TO THAT CLI: what it's called, how you ask it
/// for a completion, how to read what it prints back, and which self-maintenance commands it has. Hand one
/// to <see cref="CliProviderEngine"/> and you have a working <see cref="ILlmProvider"/> — the engine owns
/// every invariant that is NOT backend-specific (command resolution, timeouts as inactivity clocks, verdict
/// classification, streaming order, empty-output-is-a-failure, probe → run → re-probe maintenance).
///
/// This is the extension point for "add a CLI provider": one dialect class plus a builder extension, rather
/// than a second copy of the spawn/verdict/streaming rules — which is precisely how they drifted apart
/// before (see `.claude/knowledge/pitfalls.md`). Prefer deriving from
/// <see cref="CliProviderDialectBase"/>, which supplies sane defaults for everything optional.
///
/// A dialect is a stateless description; the engine holds the resources (runner, options, tool provisioner).
/// </summary>
public interface ICliProviderDialect
{
    /// <summary>The provider id this dialect produces (<c>"claude-cli"</c>), used for routing candidates,
    /// keyed DI lookups and diagnostics.</summary>
    string Id { get; }

    /// <summary>The backend's own executable name, spawned when no override or environment variable answers
    /// (<c>"claude"</c>). Resolved on PATH by <see cref="Processes.ProcessRunner"/>, including its Windows
    /// launcher-shim handling.</summary>
    string DefaultCommand { get; }

    /// <summary>Environment variables consulted (in order) for a command override, before falling back to
    /// <see cref="DefaultCommand"/>. Conventionally starts with <c>LYNTAI_PROVIDER_CMD</c> — the shared seam
    /// that points tests/e2e at the deterministic provider stub — followed by the backend's own variable.</summary>
    IReadOnlyList<string> CommandEnvironmentVariables { get; }

    /// <summary>Whether this CLI accepts request-level tool DECLARATIONS
    /// (<see cref="LlmRequest.Tools"/>). False for CLIs that expose tools their own way (e.g. over MCP via
    /// an <see cref="Agents.ICliToolProvisioner"/>) — the engine then warns rather than dropping them
    /// silently. That warning is ALL this flag drives: a dialect returning true must have its composing
    /// <see cref="ILlmProvider"/> declare <c>SupportsToolCalls =&gt; true</c> itself (per <c>DECISIONS.md</c>
    /// D21 the provider is the capability declarer), or <see cref="ILlmRouter.SupportsToolCalls"/> answers
    /// false and the tool loop silently takes its prompt-based fallback.</summary>
    bool SupportsToolCalls { get; }

    /// <summary>How this CLI wants the prompt: stdin (the safe default) or a trailing argument.</summary>
    CliPromptDelivery PromptDelivery { get; }

    /// <summary>How long a maintenance spawn (version/update-style readout) may go SILENT before it's treated
    /// as unreachable. A stall detector, not a work budget — a probe must not hang a settings screen.</summary>
    TimeSpan MaintenanceTimeout { get; }

    /// <summary>The bounded budget for an INTERACTIVE sign-in. Applied to both clocks, because a login flow
    /// prints a URL and then goes legitimately silent while a human clicks.</summary>
    TimeSpan LoginTimeout { get; }

    /// <summary>The static argv for one completion — print/non-interactive mode, output format, model
    /// selection. The PROMPT is not included here: it is delivered per
    /// <see cref="CliPromptDelivery"/>.</summary>
    /// <param name="request">The call being built.</param>
    /// <param name="toolHostArgs">Args from an <see cref="Lyntai.Agents.ICliToolProvisioner"/> that point
    /// this CLI at the host's own MCP endpoint — empty when nothing is hosted.
    ///
    /// <para><b>The DIALECT places these, because only the dialect knows where they may legally go.</b> The
    /// engine used to append them after this method's return value, which is correct only for a CLI whose
    /// argv ends in options. It does not for <c>codex</c>, whose argv ends in the <c>-</c> stdin positional:
    /// everything after it is read as PROMPT text, and on that CLI a swallowed flag is a SPENT TURN rather
    /// than an error. That hazard was documented on <c>CodexExecArgs</c> — which takes its own
    /// <c>extraOptions</c> parameter for exactly this reason — and the agent path honoured it while the
    /// completion path had no way to. Appending is still the right answer for most CLIs; it is now a choice
    /// each dialect makes rather than one the engine makes for all of them.</para></param>
    IReadOnlyList<string> BuildCompletionArgs(LlmRequest request, IReadOnlyList<string> toolHostArgs);

    /// <summary>Flatten the request's messages into the single prompt this CLI takes.</summary>
    string BuildPrompt(LlmRequest request);

    /// <summary>Decode ONE line of the CLI's output. Must be tolerant: an unknown or malformed line is
    /// <see cref="CliOutputEvent.Ignored"/>, never a throw — a stream carries plenty that isn't the answer.
    /// The line arrives WITHOUT its terminator on both the buffered and the streamed path (no trailing
    /// <c>\n</c>, and no <c>\r</c> from a CRLF-emitting child), so an exact match is safe. A
    /// <see cref="CliOutputEventKind.Content"/> event MUST carry non-empty text — a line that turned out to
    /// hold none is <see cref="CliOutputEvent.Ignored"/>, because the engine's "did anything arrive?" test
    /// counts Content events rather than their length.</summary>
    CliOutputEvent ParseLine(string line);

    /// <summary>Read the backend's version banner (and a model id, only if the line explicitly labels one —
    /// never inferred).</summary>
    (string? Version, string? Model) ParseVersionLine(string line);

    /// <summary>Argv for the turn-free version readout (<c>["--version"]</c>), or null if this backend has
    /// none. MUST be flag-shaped or a verified subcommand: a CLI that treats an unrecognized token as a
    /// PROMPT will silently spend a turn answering it.</summary>
    IReadOnlyList<string>? VersionArgs { get; }

    /// <summary>Argv for the backend's own updater, or null if it has none.</summary>
    IReadOnlyList<string>? UpdateArgs { get; }

    /// <summary>Argv for a machine-readable auth readout, or null if this backend has none. Must not run a
    /// completion.</summary>
    IReadOnlyList<string>? AuthStatusArgs { get; }

    /// <summary>Argv for signing out, or null if this backend has no logout.</summary>
    IReadOnlyList<string>? LogoutArgs { get; }

    /// <summary>Build the argv that installs a NAMED version, or refuse. Refuse (rather than invent an
    /// argument) when this backend can't pin a version, or when the requested value would be read by the
    /// backend as a flag instead of a version.</summary>
    bool TryBuildInstallArgs(ProviderInstallRequest? request, out IReadOnlyList<string> args, out string? refusal);

    /// <summary>Build the argv for a sign-in, or refuse. A <see cref="ProviderLoginRequest.Mode"/> this
    /// backend doesn't have must be REFUSED, never forwarded as an invented flag.</summary>
    bool TryBuildLoginArgs(ProviderLoginRequest? request, out IReadOnlyList<string> args, out string? refusal);

    /// <summary>Interpret the auth readout's output, or null when it can't be read as one (the engine then
    /// reports "not authenticated" with the raw text, rather than guessing a signed-in state).</summary>
    ProviderAuthStatus? ParseAuthStatus(string output);
}
