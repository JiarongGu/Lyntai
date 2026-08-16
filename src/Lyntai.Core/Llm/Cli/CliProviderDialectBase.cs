namespace Lyntai.Llm.Cli;

/// <summary>Convenience base for an <see cref="ICliProviderDialect"/>: everything OPTIONAL already has a
/// sane default, so a new CLI backend is typically four members — <see cref="Id"/>,
/// <see cref="DefaultCommand"/>, <see cref="BuildCompletionArgs"/>, <see cref="ParseLine"/> — plus whichever
/// self-maintenance commands that backend actually has.
///
/// The defaults are deliberately CONSERVATIVE: no self-updater, no pinned install, no auth. A capability is
/// only claimed by a dialect that names the command for it, so a backend never gets credited with a
/// capability it hasn't been verified to have.</summary>
public abstract class CliProviderDialectBase : ICliProviderDialect
{
    /// <inheritdoc/>
    public abstract string Id { get; }

    /// <inheritdoc/>
    public abstract string DefaultCommand { get; }

    /// <summary>The shared test/e2e seam only. Override to append the backend's own variable — e.g.
    /// <c>["LYNTAI_PROVIDER_CMD", "CLAUDE_CMD"]</c>.</summary>
    public virtual IReadOnlyList<string> CommandEnvironmentVariables => ["LYNTAI_PROVIDER_CMD"];

    /// <summary>False: a CLI typically exposes tools its own way rather than accepting request-level
    /// declarations.</summary>
    public virtual bool SupportsToolCalls => false;

    /// <summary>Stdin — a prompt carries newlines and metacharacters, and argv has length limits.</summary>
    public virtual CliPromptDelivery PromptDelivery => CliPromptDelivery.Stdin;

    /// <summary>30 seconds: a version/auth readout is sub-second work, so this is a stall detector rather
    /// than a work budget (deliberately NOT the provider timeout — a probe must not hang a settings screen
    /// for two minutes).</summary>
    public virtual TimeSpan MaintenanceTimeout => TimeSpan.FromSeconds(30);

    /// <summary>10 minutes: a human has to read a URL, switch to a browser and approve — generous, but
    /// bounded, so a caller can never hang forever on a flow nobody completed.</summary>
    public virtual TimeSpan LoginTimeout => TimeSpan.FromMinutes(10);

    /// <inheritdoc/>
    public abstract IReadOnlyList<string> BuildCompletionArgs(
        LlmRequest request, IReadOnlyList<string> toolHostArgs);

    /// <summary>The shared flattening: a lone user message verbatim, otherwise role-labeled blocks, plus the
    /// structured-output instruction for a <see cref="LlmRequest.JsonSchema"/> request.</summary>
    public virtual string BuildPrompt(LlmRequest request) => CliPrompt.Flatten(request);

    /// <inheritdoc/>
    public abstract CliOutputEvent ParseLine(string line);

    /// <summary>The shared banner reader: a dotted version number, plus a model id only where the line
    /// explicitly labels one.</summary>
    public virtual (string? Version, string? Model) ParseVersionLine(string line) => CliVersionLine.Parse(line);

    /// <summary><c>--version</c> — near-universal, and safe because it is a FLAG (an unrecognized
    /// subcommand can cost a turn on CLIs that treat one as a prompt). Override with null if the backend
    /// has no version readout.</summary>
    public virtual IReadOnlyList<string>? VersionArgs => ["--version"];

    /// <summary>None by default — claim a self-updater only when the backend verifiably ships one.</summary>
    public virtual IReadOnlyList<string>? UpdateArgs => null;

    /// <summary>None by default — claim an auth readout only when the backend verifiably has one.</summary>
    public virtual IReadOnlyList<string>? AuthStatusArgs => null;

    /// <summary>None by default.</summary>
    public virtual IReadOnlyList<string>? LogoutArgs => null;

    /// <summary>Refuses: pinning a version is not assumed to exist. Override for a backend whose own
    /// installer can take a version.</summary>
    public virtual bool TryBuildInstallArgs(ProviderInstallRequest? request, out IReadOnlyList<string> args, out string? refusal)
    {
        args = [];
        refusal = $"{Id} cannot install a named version of itself";
        return false;
    }

    /// <summary>Refuses: a login flow is not assumed to exist. Override for a backend that has one.</summary>
    public virtual bool TryBuildLoginArgs(ProviderLoginRequest? request, out IReadOnlyList<string> args, out string? refusal)
    {
        args = [];
        refusal = $"{Id} has no sign-in flow to drive";
        return false;
    }

    /// <summary>Null: no auth readout to interpret by default.</summary>
    public virtual ProviderAuthStatus? ParseAuthStatus(string output) => null;

    /// <summary>Whether a caller-supplied VALUE would be read as an option by the backend. Values travel as
    /// separate <c>ArgumentList</c> entries (never a shell), so this isn't shell injection — but a value like
    /// <c>--force</c> in a version or email slot would still be parsed as a flag. Use it in
    /// <see cref="TryBuildInstallArgs"/> / <see cref="TryBuildLoginArgs"/> before accepting free-form
    /// input.</summary>
    protected static bool FlagShaped(string? value) => value is { Length: > 0 } && value[0] == '-';
}
