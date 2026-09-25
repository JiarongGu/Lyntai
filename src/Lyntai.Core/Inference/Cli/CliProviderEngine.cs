using Lyntai.Inference;
using System.Runtime.CompilerServices;
using Lyntai.Agents;
using Lyntai.Inference.Streaming;
using Lyntai.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Inference.Cli;

/// <summary>
/// The generic engine behind every spawned-CLI provider. Give it an <see cref="ICliBackend"/> (what
/// this particular CLI is called, how to ask it, how to read it) and it supplies everything else — the parts
/// that are the SAME for every CLI backend and must not be re-derived per provider:
///
/// <list type="bullet">
/// <item>command resolution (explicit override → the backend's environment variables → its default exe),</item>
/// <item>spawn hygiene: no shell, <c>ArgumentList</c> only, a neutral working directory, prompt over stdin
///   (or a trailing argument) — Windows launcher-shim handling included, via
///   <see cref="ProcessRunner"/>,</item>
/// <item>timeouts as an INACTIVITY clock, never one wall clock over a whole call, plus an absolute
///   <see cref="LyntaiOptions.MaxProviderTimeout"/> backstop on BOTH completion paths (a long-running AGENT
///   turn is a different seam: it drives <see cref="IProcessRunner"/> directly and has no ceiling),</item>
/// <item>verdict classification through <see cref="ProviderVerdictClassifier"/> — no per-provider heuristics,</item>
/// <item>empty output is a <see cref="ProviderVerdict.Failed"/>, never an empty Ok, so the router can fall over,</item>
/// <item>streaming order: content chunks, then exactly one terminal <c>Final</c> or <c>Error</c> — where
///   "content" is the router's gate, <c>Kind == Content &amp;&amp; Text.Length &gt; 0</c>, so an EMPTY event
///   neither reaches the caller nor commits the stream,</item>
/// <item>self-maintenance as probe → run → re-probe, so "did anything change?" is a version comparison,</item>
/// <item>fail-safe on every maintenance path (a value, never a throw) except caller cancellation.</item>
/// </list>
///
/// A provider package therefore contains an <see cref="ICliBackend"/> plus a thin <see cref="IModelProvider"/>
/// forwarding to this engine, declaring which OPTIONAL capability interfaces its backend actually has.
/// </summary>
/// <param name="backend">The backend-specific half — everything about this particular CLI.</param>
/// <param name="runner">Process execution — BYO to sandbox, audit or remote the spawn.</param>
/// <param name="options">Timeout/model configuration.</param>
/// <param name="logger">Optional diagnostics.</param>
/// <param name="command">Explicit command override; wins over the backend's environment variables. This is
/// how a host points at a PORTABLE copy of a CLI (a binary it ships or unpacks itself) instead of a global
/// PATH install — quote a path containing spaces.</param>
/// <param name="provisioner">Optional tool host (e.g. MCP) consulted per call, whose session lives for the
/// length of the call. Its args are handed to the BACKEND (<c>BuildCompletionArgs</c>) rather than appended
/// here: where they may legally sit depends on that CLI's own argv grammar, and appending is wrong for one
/// whose argv ends in a positional (<c>docs/DECISIONS.md</c> D65).</param>
/// <param name="environment">Extra environment variables for EVERY spawn (completions and maintenance
/// alike). The other half of portable support: a bundled CLI usually needs its own home/config directory
/// (<c>CODEX_HOME</c>, <c>CLAUDE_CONFIG_DIR</c>) so it doesn't read — or mutate — the machine-wide install's
/// state. Applies to the maintenance spawns too, so a probe/auth check reports the PORTABLE install's state
/// rather than the global one's.</param>
public sealed class CliProviderEngine(
    ICliBackend backend,
    IProcessRunner runner,
    LyntaiOptions options,
    ILogger? logger = null,
    string? command = null,
    ICliToolProvisioner? provisioner = null,
    IReadOnlyDictionary<string, string>? environment = null)
{
    /// <summary>Design §6 CLI hygiene: spawn from a NEUTRAL cwd — never the host app's inherited working
    /// directory, whose project config (agent instructions, hooks, memory) a CLI would otherwise load into
    /// every library completion and judge call, silently skewing them.</summary>
    public static readonly string NeutralWorkingDirectory = Path.GetTempPath();

    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    /// <summary>The provider id this engine's backend produces.</summary>
    public string Id => backend.Id;

    /// <summary>Whether the backend looks callable: for the built-in <see cref="ProcessRunner"/>, whether the
    /// resolved command is actually present — a bare name on PATH, or a PORTABLE copy at the path the host
    /// pointed at (including an extensionless shim rescued by its spawnable sibling).</summary>
    /// <remarks>
    /// <para>OPTIMISTIC for a BYO <see cref="IProcessRunner"/>: a custom runner (sandbox / remote / audited
    /// execution) resolves the command in ITS OWN environment, not the host's local PATH — so this returns
    /// true without probing rather than skip the provider and never reach the runner. A truly missing binary
    /// then surfaces as a <see cref="ProviderVerdict.Failed"/> verdict on the actual call, and the router falls
    /// over to the next candidate.</para>
    /// <para>An explicit command is CHECKED rather than trusted: a host that ships its own CLI copy wants a
    /// deleted/never-unpacked binary to make this candidate unavailable — the router then skips it, instead of
    /// discovering the absence as a failed turn.</para>
    /// </remarks>
    public bool IsAvailable => runner is not ProcessRunner || ProcessRunner.CommandExists(ResolveCommand().Exe);

    /// <summary>Resolve the command override / environment seams into exe + prefix args.</summary>
    public (string Exe, IReadOnlyList<string> PrefixArgs) ResolveCommand() => CliCommand.Resolve(command, backend);

    /// <summary>The absolute backstop over an inactivity <paramref name="window"/>:
    /// <see cref="LyntaiOptions.MaxProviderTimeout"/>, but never below the window — a consumer budget above
    /// the ceiling raises it, not the reverse.</summary>
    private TimeSpan Backstop(TimeSpan window) =>
        options.MaxProviderTimeout < window ? window : options.MaxProviderTimeout;

    // ── completion ───────────────────────────────────────────────────────────

    /// <summary>Run one buffered completion.</summary>
    public async Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default)
    {
        WarnIfRequestToolsIgnored(req);
        // when a provisioner is registered, it stands up a tool host and hands back the CLI args; the
        // session is torn down after the process exits
        await using var session = provisioner is null ? null : await provisioner.ProvisionAsync(ct).ConfigureAwait(false);
        var (exe, prefixArgs) = ResolveCommand();
        var (argv, stdin) = BuildInvocation(req, prefixArgs, session?.ExtraArgs);

        // `timeout` is an INACTIVITY window (a slow-but-alive turn keeps re-arming it); the backstop bounds a
        // chatty child that never stalls
        var timeout = options.ResolveTimeout(req);
        var maxDuration = Backstop(timeout);
        ProcessResult result;
        try
        {
            result = await runner.RunAsync(exe, argv, stdin: stdin, inactivityTimeout: timeout, maxDuration: maxDuration,
                workingDirectory: NeutralWorkingDirectory, environment: environment, ct: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (CliFault.Classify(ex) is { } fault)
        {
            return new TextResponse("", fault.Verdict, Detail: fault.Detail);
        }

        if (result.TimedOut)
            return new TextResponse("", ProviderVerdict.Timeout,
                Detail: result.TimeoutKind == ProcessTimeoutKind.MaxDuration
                    ? $"{backend.Id} exceeded max duration {maxDuration}"
                    : $"{backend.Id} stalled — no output for {timeout}");

        var stderrTail = result.StdErrTail();

        string text = "", contentText = "";
        TextUsage? usage = null;
        string? failure = null;
        var sawResult = false;
        foreach (var raw in result.StdOut.Split('\n'))
        {
            // strip the terminator so a backend sees the SAME line both paths hand it — the streamed path's
            // ReadLineAsync already drops it, so a CRLF-emitting child would otherwise leave a trailing '\r'
            // here and only here (an exact-match ParseLine would then work streamed and fail buffered)
            var evt = backend.ParseLine(raw.TrimEnd('\r'));
            if (evt.Kind == CliOutputEventKind.Content) contentText += evt.Text;
            if (evt.Kind == CliOutputEventKind.Failure) failure = evt.Text;
            if (evt.Kind == CliOutputEventKind.Result)
            {
                sawResult = true;
                text = evt.Text;
                usage = evt.Usage;
            }
        }
        if (text.Length == 0) text = contentText; // result-less streams still carry content

        // The backend's OWN failure outranks partial content AND a non-zero exit, so it is read first: a
        // codex turn can print turn.failed(401) and exit non-zero with only startup chatter on stderr, and
        // classifying the chatter would turn AuthFailed into a bare Failed (pitfalls.md, "Trusting the exit
        // code over the machine-readable answer"). The exit code stays in the detail as context.
        if (failure is { Length: > 0 })
            return new TextResponse("", ProviderVerdictClassifier.FromErrorText(failure),
                Detail: result.ExitCode != 0 ? $"exit {result.ExitCode}: {failure}" : failure);

        // no in-band account: exit code and stderr are all there is — and a complete-looking answer from a
        // run that exited non-zero is still a failed run
        if (result.ExitCode != 0)
        {
            var exited = CliFault.Exited(result.ExitCode, stderrTail);
            return new TextResponse("", exited.Verdict, Detail: exited.Detail);
        }

        if (text.Length == 0)
        {
            _logger.LogWarning("{Provider} produced no content ({SawResult}); stderr: {Tail}", backend.Id, sawResult, stderrTail);
            return new TextResponse("", ProviderVerdict.Failed,
                Detail: stderrTail.Length > 0 ? stderrTail : "no output produced");
        }
        return new TextResponse(text, ProviderVerdict.Ok, usage);
    }

    /// <summary>Stream one completion. Content chunks arrive as the CLI prints them, followed by exactly one
    /// terminal chunk (<c>Final</c> when anything was delivered, otherwise <c>Error</c>).</summary>
    /// <remarks>Bounded by the same two clocks as <see cref="CompleteAsync"/> — the resolved timeout as an
    /// inactivity window, <see cref="LyntaiOptions.MaxProviderTimeout"/> as the absolute backstop — plus
    /// caller cancellation, which also kills the process tree when the enumerator is abandoned.</remarks>
    public async IAsyncEnumerable<TextChunk> StreamAsync(TextRequest req, [EnumeratorCancellation] CancellationToken ct = default)
    {
        WarnIfRequestToolsIgnored(req);
        // the host lives for the whole stream (the CLI calls tools throughout); torn down when this iterator
        // is disposed
        await using var session = provisioner is null ? null : await provisioner.ProvisionAsync(ct).ConfigureAwait(false);
        var (exe, prefixArgs) = ResolveCommand();
        var (argv, stdin) = BuildInvocation(req, prefixArgs, session?.ExtraArgs);

        var sawContent = false;
        string resultText = "";
        TextUsage? usage = null;

        // The same two clocks as the buffered path: inactivity ALONE cannot see a chatty child that never
        // stalls and never finishes. This ceiling is a COMPLETION's — the agent sessions drive IProcessRunner
        // directly and keep no wall clock, since an hour-long healthy session is what one would kill.
        var timeout = options.ResolveTimeout(req);
        var lines = runner.StreamLinesAsync(exe, argv, stdin: stdin, inactivityTimeout: timeout,
            maxDuration: Backstop(timeout), workingDirectory: NeutralWorkingDirectory, environment: environment, ct: ct);
        var enumerator = lines.GetAsyncEnumerator(ct);
        await using (enumerator.ConfigureAwait(false))
        {
            // no clock here: the inactivity window is the RUNNER's, so a cancellation propagates (CliFault)
            var guarded = GuardedStream.ReadAll<string, TextChunk>(
                async () => await enumerator.MoveNextAsync().ConfigureAwait(false) ? enumerator.Current : null,
                ex => CliFault.Classify(ex) is { } fault ? TextChunk.Error(fault.Verdict, fault.Detail) : null,
                ct);
            await foreach (var (line, terminal) in guarded.ConfigureAwait(false))
            {
                if (terminal is not null)
                {
                    yield return terminal;
                    yield break;
                }

                var evt = backend.ParseLine(line!);
                // the ROUTER's commit gate, to the letter: an EMPTY content event is not delivered content,
                // so it reads as Ignored — counting it would disable fallback on a zero-content first chunk
                // and end a no-output stream as a successful empty Final (pitfalls.md)
                if (evt.Kind == CliOutputEventKind.Content && evt.Text.Length > 0)
                {
                    sawContent = true;
                    yield return TextChunk.Content(evt.Text);
                }
                else if (evt.Kind == CliOutputEventKind.Result)
                {
                    resultText = evt.Text;
                    usage = evt.Usage;
                }
                else if (evt.Kind == CliOutputEventKind.Failure)
                {
                    // the backend reported its own terminal failure mid-stream. Content already yielded stays
                    // delivered (it can't be unsent — and per design §6 the router won't fall back after the
                    // first token anyway), but the stream must END as an Error so the consumer isn't handed a
                    // truncated answer labelled complete.
                    yield return TextChunk.Error(ProviderVerdictClassifier.FromErrorText(evt.Text), evt.Text);
                    yield break;
                }
            }
        }

        if (!sawContent && resultText.Length > 0)
        {
            yield return TextChunk.Content(resultText); // result-only stream still delivers the text
            sawContent = true;
        }

        // content that arrived without a terminal result event is still a successful stream — a trailing
        // Error here would mark a fully-delivered answer as a failed run
        if (sawContent)
            yield return TextChunk.Final(usage);
        else
            yield return TextChunk.Error(ProviderVerdict.Failed, "no output produced");
    }

    /// <summary>Assemble argv + stdin for one call: prefix args, the backend's completion args, any tool-host
    /// args, and the prompt — which goes LAST when this CLI takes it positionally.</summary>
    private (List<string> Argv, string? Stdin) BuildInvocation(
        TextRequest req, IReadOnlyList<string> prefixArgs, IReadOnlyList<string>? extraArgs)
    {
        var prompt = backend.BuildPrompt(req);
        // the tool-host args go THROUGH the backend, never around it: only the backend knows whether its
        // argv ends in options (append is fine) or in a positional (append feeds them to the model as
        // prompt text, and on codex a swallowed flag is a spent turn)
        var argv = prefixArgs.Concat(backend.BuildCompletionArgs(req, extraArgs ?? [])).ToList();
        if (backend.PromptDelivery != CliPromptDelivery.Argument) return (argv, prompt);
        argv.Add(prompt);
        return (argv, null);
    }

    // ── self-maintenance: version, update, pinned install ────────────────────

    /// <summary>Report the installed backend without running a turn.</summary>
    public async Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        if (backend.VersionArgs is not { } versionArgs)
            return new ProviderProbeResult(false, Detail: $"{backend.Id} has no turn-free version readout");

        var result = await RunMaintenanceAsync(versionArgs, backend.MaintenanceTimeout, backend.MaintenanceTimeout, ct)
            .ConfigureAwait(false);
        if (result.Failure is { } failure) return new ProviderProbeResult(false, Detail: $"probe failed: {failure}");
        if (result.Process!.TimedOut)
            return new ProviderProbeResult(false,
                Detail: $"{backend.Id} reported no version within {backend.MaintenanceTimeout}");
        if (result.Process.ExitCode != 0)
            return new ProviderProbeResult(false, Detail: $"exit {result.Process.ExitCode}: {result.Process.StdErrTail()}");

        // some launchers print the banner on stderr; the first non-empty line is the version line
        var line = CliVersionLine.FirstLine(result.Process.StdOut);
        if (line.Length == 0) line = CliVersionLine.FirstLine(result.Process.StdErr);
        var (version, model) = backend.ParseVersionLine(line);
        // NAMED, not positional: the merged record reorders what the LLM-side one declared, and every
        // field here is a string — so a positional call would still COMPILE and assign the wrong ones.
        return new ProviderProbeResult(
            true, Detail: line.Length > 0 ? line : null, Version: version, Model: model);
    }

    /// <summary>Run the backend's own updater.</summary>
    public Task<ProviderUpdateResult> UpdateAsync(CancellationToken ct = default) =>
        backend.UpdateArgs is { } updateArgs
            ? SelfMaintainAsync(updateArgs, $"{backend.Id} update", ct)
            : Task.FromResult(new ProviderUpdateResult(false, false, Detail: $"{backend.Id} has no self-updater to drive"));

    /// <summary>Ask the backend to install a named version of itself.</summary>
    public Task<ProviderUpdateResult> InstallAsync(ProviderInstallRequest? request = null, CancellationToken ct = default) =>
        backend.TryBuildInstallArgs(request, out var installArgs, out var refusal)
            ? SelfMaintainAsync(installArgs, $"{backend.Id} install", ct)
            : Task.FromResult(new ProviderUpdateResult(false, false, Detail: refusal));

    /// <summary>The shared shape of every self-maintenance spawn (update / pinned install): probe → run the
    /// backend's own tooling → re-probe, so "did anything change?" is a VERSION COMPARISON rather than a parse
    /// of the backend's prose. Fails safe on every path except cancellation.</summary>
    /// <remarks>Unlike a probe these can legitimately run for minutes (they download), so they get the
    /// configured provider clocks: <see cref="LyntaiOptions.ProviderTimeout"/> as the inactivity window and
    /// <see cref="LyntaiOptions.MaxProviderTimeout"/> as the absolute backstop, never below it.</remarks>
    private async Task<ProviderUpdateResult> SelfMaintainAsync(
        IReadOnlyList<string> maintenanceArgs, string label, CancellationToken ct)
    {
        var before = await ProbeAsync(ct).ConfigureAwait(false);
        var inactivity = options.ProviderTimeout;

        var run = await RunMaintenanceAsync(maintenanceArgs, inactivity, Backstop(inactivity), ct).ConfigureAwait(false);
        if (run.Failure is { } failure) return Unchanged($"{label} failed: {failure}");
        if (run.Process!.TimedOut) return Unchanged($"{label} stalled — no output for {inactivity}");
        if (run.Process.ExitCode != 0) return Unchanged($"exit {run.Process.ExitCode}: {run.Process.StdErrTail()}");

        // the tool's own wording is the diagnostic; whether anything CHANGED is the version comparison
        var after = await ProbeAsync(ct).ConfigureAwait(false);
        var updated = before.Version is { } from && after.Version is { } to &&
            !string.Equals(from, to, StringComparison.OrdinalIgnoreCase);
        var output = run.Process.OutputTail();
        return new ProviderUpdateResult(true, updated, before.Version, after.Version,
            output.Length > 0 ? output : null);

        // a failed run installed nothing: the "after" version is the "before" one, not a fresh probe
        ProviderUpdateResult Unchanged(string detail) => new(false, false, before.Version, before.Version, detail);
    }

    // ── self-maintenance: auth ───────────────────────────────────────────────

    /// <summary>Report whether the backend is authenticated — and as whom — without running a turn.</summary>
    /// <remarks>The backend's parsed state WINS over the exit code: a signed-out backend may report its state
    /// and still exit non-zero, and that is an answer, not a broken backend. An output shape the backend can't
    /// read reports <c>Authenticated: false</c> with the raw text — it never guesses a signed-in state.</remarks>
    public async Task<ProviderAuthStatus> StatusAsync(CancellationToken ct = default)
    {
        if (backend.AuthStatusArgs is not { } statusArgs)
            return new ProviderAuthStatus(false, Detail: $"{backend.Id} has no turn-free auth readout");

        var result = await RunMaintenanceAsync(statusArgs, backend.MaintenanceTimeout, backend.MaintenanceTimeout, ct)
            .ConfigureAwait(false);
        if (result.Failure is { } failure)
            return new ProviderAuthStatus(false, Detail: $"auth status failed: {failure}");
        if (result.Process!.TimedOut)
            return new ProviderAuthStatus(false,
                Detail: $"{backend.Id} reported no auth status within {backend.MaintenanceTimeout}");

        // parse the WHOLE body (OutputTail keeps the LAST N chars and would decapitate a document)
        var body = result.Process.StdOut.Length > 0 ? result.Process.StdOut : result.Process.StdErr;
        if (backend.ParseAuthStatus(body) is { } status)
            return status with { Detail = result.Process.OutputTail() };

        if (result.Process.ExitCode != 0)
            return new ProviderAuthStatus(false, Detail: $"exit {result.Process.ExitCode}: {result.Process.StdErrTail()}");

        var tail = result.Process.OutputTail();
        return new ProviderAuthStatus(false, Detail: tail.Length > 0
            ? $"unrecognized auth status output: {tail}"
            : $"{backend.Id} reported no auth status");
    }

    /// <summary>Start the backend's sign-in flow, then report the state it left behind. BLOCKS until the flow
    /// completes, fails, or <see cref="ICliBackend.LoginTimeout"/> expires; cancelling
    /// <paramref name="ct"/> abandons the wait (and kills the process tree).</summary>
    public Task<ProviderAuthResult> LoginAsync(ProviderLoginRequest? request = null, CancellationToken ct = default) =>
        backend.TryBuildLoginArgs(request, out var loginArgs, out var refusal)
            // a login prints a URL and then goes SILENT while a human clicks, so the per-chunk inactivity
            // window a probe uses would kill a live flow — both clocks are the same bounded budget here
            ? RunAuthCommandAsync(loginArgs, $"{backend.Id} login", backend.LoginTimeout, backend.LoginTimeout, ct)
            : Task.FromResult(new ProviderAuthResult(false, Detail: refusal));

    /// <summary>Sign the backend out, and report the state it left behind.</summary>
    public Task<ProviderAuthResult> LogoutAsync(CancellationToken ct = default) =>
        backend.LogoutArgs is { } logoutArgs
            ? RunAuthCommandAsync(logoutArgs, $"{backend.Id} logout", backend.MaintenanceTimeout, backend.MaintenanceTimeout, ct)
            : Task.FromResult(new ProviderAuthResult(false, Detail: $"{backend.Id} has no logout to drive"));

    /// <summary>Run one auth subcommand, then RE-READ the resulting state, so a caller learns what the backend
    /// is in rather than what the command claimed.</summary>
    private async Task<ProviderAuthResult> RunAuthCommandAsync(IReadOnlyList<string> authArgs, string label,
        TimeSpan inactivity, TimeSpan maxDuration, CancellationToken ct)
    {
        var result = await RunMaintenanceAsync(authArgs, inactivity, maxDuration, ct).ConfigureAwait(false);
        if (result.Failure is { } failure) return new ProviderAuthResult(false, Detail: $"{label} failed: {failure}");
        if (result.Process!.TimedOut)
            return new ProviderAuthResult(false, Detail: $"{label} did not complete within {maxDuration}");
        if (result.Process.ExitCode != 0)
            return new ProviderAuthResult(false, Detail: $"exit {result.Process.ExitCode}: {result.Process.StdErrTail()}");

        var status = await StatusAsync(ct).ConfigureAwait(false);
        var output = result.Process.OutputTail();
        return new ProviderAuthResult(true, status, output.Length > 0 ? output : null);
    }

    /// <summary>One maintenance spawn: no stdin, neutral cwd, the given clocks. Returns the process result OR
    /// a failure message — a spawn that throws (a missing binary) is a VALUE here, because every maintenance
    /// seam must fail safe rather than throw. Caller cancellation still propagates.</summary>
    private async Task<(ProcessResult? Process, string? Failure)> RunMaintenanceAsync(
        IReadOnlyList<string> maintenanceArgs, TimeSpan inactivity, TimeSpan maxDuration, CancellationToken ct)
    {
        var (exe, prefixArgs) = ResolveCommand();
        try
        {
            var result = await runner.RunAsync(exe, [.. prefixArgs, .. maintenanceArgs], stdin: null,
                inactivityTimeout: inactivity, maxDuration: maxDuration,
                workingDirectory: NeutralWorkingDirectory, environment: environment, ct: ct).ConfigureAwait(false);
            return (result, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>A CLI that doesn't take request-level tool declarations must not drop them SILENTLY — a
    /// caller that put tools on the request and routed here gets a diagnostic instead of a mystery.</summary>
    private void WarnIfRequestToolsIgnored(TextRequest req)
    {
        if (!backend.SupportsToolCalls && req.Tools is { Count: > 0 })
            _logger.LogWarning(
                "{Provider} ignores TextRequest.Tools ({Count} declaration(s) dropped) — this CLI provider " +
                "doesn't take request-level tool declarations; expose tools via AddMcpToolHost(<its connector>) instead.",
                backend.Id, req.Tools.Count);
    }
}
