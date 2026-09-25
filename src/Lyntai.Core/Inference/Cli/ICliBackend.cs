using Lyntai.Inference;
namespace Lyntai.Inference.Cli;

/// <summary>
/// Everything about a spawned-CLI backend that is SPECIFIC TO THAT CLI: what it's called, how you ask it
/// for a completion, how to read what it prints back, and which self-maintenance commands it has. Hand one
/// to <see cref="CliProviderEngine"/> and you have a working <see cref="IModelProvider"/> — the engine owns
/// every invariant that is NOT backend-specific (command resolution, timeouts as inactivity clocks, verdict
/// classification, streaming order, empty-output-is-a-failure, probe → run → re-probe maintenance).
///
/// This is the extension point for "add a CLI provider": one backend class, a thin provider composing the
/// engine, and a builder extension, rather
/// than a second copy of the spawn/verdict/streaming rules — which is precisely how they drifted apart
/// before (see `.claude/knowledge/pitfalls.md`). Prefer deriving from
/// <see cref="CliBackendBase"/>, which supplies sane defaults for everything optional.
///
/// An <see cref="ICliBackend"/> is a stateless description; the engine holds the resources (runner,
/// options, tool provisioner).
/// </summary>
public interface ICliBackend
{
    /// <summary>The provider id this backend answers to (<c>"claude-cli"</c>), used for routing candidates,
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
    /// (<see cref="TextRequest.Tools"/>). False for CLIs that expose tools their own way (e.g. over MCP via
    /// an <see cref="Agents.ICliToolProvisioner"/>) — the engine then warns rather than dropping them
    /// silently. The engine reads it for that warning only; the composing <see cref="IModelProvider"/> is the
    /// capability declarer (<c>DECISIONS.md</c> D21), so it DERIVES its
    /// <see cref="ProviderCapabilities.SupportsToolCalls"/> from this flag — as the shipped CLI providers do —
    /// or <see cref="ITextRouter.GetCapabilitiesAsync"/> reports none and the tool loop takes its prompt-based
    /// fallback.</summary>
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
    /// <para><b>The BACKEND places these, because only it knows where they may legally go</b>
    /// (<c>docs/DECISIONS.md</c> D65). Appending suits a CLI whose argv ends in options; it is wrong for one
    /// whose argv ends in a positional — <c>codex</c>'s ends in the <c>-</c> stdin marker, after which a flag
    /// is read as PROMPT text and spends a turn.</para></param>
    IReadOnlyList<string> BuildCompletionArgs(TextRequest request, IReadOnlyList<string> toolHostArgs);

    /// <summary>Flatten the request's messages into the single prompt this CLI takes.</summary>
    string BuildPrompt(TextRequest request);

    /// <summary>Decode ONE line of the CLI's output. Must be tolerant: an unknown or malformed line is
    /// <see cref="CliOutputEvent.Ignored"/>, never a throw — a stream carries plenty that isn't the answer.
    /// The line arrives WITHOUT its terminator on both the buffered and the streamed path (no trailing
    /// <c>\n</c>, and no <c>\r</c> from a CRLF-emitting child), so an exact match is safe. Report a line
    /// that turned out to hold no content as <see cref="CliOutputEvent.Ignored"/>; an EMPTY
    /// <see cref="CliOutputEventKind.Content"/> event is tolerated, since the engine counts content by its
    /// length (<see cref="CliOutputEvent.Content"/>).</summary>
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
