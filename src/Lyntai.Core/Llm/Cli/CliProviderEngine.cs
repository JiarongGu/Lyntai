using System.Runtime.CompilerServices;
using Lyntai.Agents;
using Lyntai.Llm.Streaming;
using Lyntai.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Llm.Cli;

/// <summary>
/// The generic engine behind every spawned-CLI provider. Give it an <see cref="ICliProviderDialect"/> (what
/// this particular CLI is called, how to ask it, how to read it) and it supplies everything else — the parts
/// that are the SAME for every CLI backend and must not be re-derived per provider:
///
/// <list type="bullet">
/// <item>command resolution (explicit override → the dialect's environment variables → its default exe),</item>
/// <item>spawn hygiene: no shell, <c>ArgumentList</c> only, a neutral working directory, prompt over stdin
///   (or a trailing argument) — Windows launcher-shim handling included, via
///   <see cref="ProcessRunner"/>,</item>
/// <item>timeouts as an INACTIVITY clock, never one wall clock over a whole call, plus an absolute
///   <see cref="LyntaiOptions.MaxProviderTimeout"/> backstop on BOTH completion paths (a long-running AGENT
///   turn is a different seam: it drives <see cref="IProcessRunner"/> directly and has no ceiling),</item>
/// <item>verdict classification through <see cref="LlmVerdictClassifier"/> — no per-provider heuristics,</item>
/// <item>empty output is a <see cref="LlmVerdict.Failed"/>, never an empty Ok, so the router can fall over,</item>
/// <item>streaming order: content chunks, then exactly one terminal <c>Final</c> or <c>Error</c> — where
///   "content" is the router's gate, <c>Kind == Content &amp;&amp; Text.Length &gt; 0</c>, so an EMPTY event
///   neither reaches the caller nor commits the stream,</item>
/// <item>self-maintenance as probe → run → re-probe, so "did anything change?" is a version comparison,</item>
/// <item>fail-safe on every maintenance path (a value, never a throw) except caller cancellation.</item>
/// </list>
///
/// A provider package therefore contains a dialect plus a thin <see cref="ILlmProvider"/> forwarding to this
/// engine, declaring which OPTIONAL capability interfaces its backend actually has.
/// </summary>
/// <param name="dialect">The backend-specific vocabulary.</param>
/// <param name="runner">Process execution — BYO to sandbox, audit or remote the spawn.</param>
/// <param name="options">Timeout/model configuration.</param>
/// <param name="logger">Optional diagnostics.</param>
/// <param name="command">Explicit command override; wins over the dialect's environment variables. This is
/// how a host points at a PORTABLE copy of a CLI (a binary it ships or unpacks itself) instead of a global
/// PATH install — quote a path containing spaces.</param>
/// <param name="provisioner">Optional tool host (e.g. MCP) consulted per call, whose session lives for the
/// length of the call. Its args are handed to the DIALECT (<c>BuildCompletionArgs</c>) rather than appended
/// here: where they may legally sit depends on that CLI's own argv grammar, and appending is wrong for one
/// whose argv ends in a positional (<c>docs/DECISIONS.md</c> D65).</param>
/// <param name="environment">Extra environment variables for EVERY spawn (completions and maintenance
/// alike). The other half of portable support: a bundled CLI usually needs its own home/config directory
/// (<c>CODEX_HOME</c>, <c>CLAUDE_CONFIG_DIR</c>) so it doesn't read — or mutate — the machine-wide install's
/// state. Applies to the maintenance spawns too, so a probe/auth check reports the PORTABLE install's state
/// rather than the global one's.</param>
public sealed class CliProviderEngine(
    ICliProviderDialect dialect,
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

    /// <summary>The provider id this engine's dialect produces.</summary>
    public string Id => dialect.Id;

    /// <summary>Whether the backend looks callable: for the built-in <see cref="ProcessRunner"/>, whether the
    /// resolved command is actually present — a bare name on PATH, or a PORTABLE copy at the path the host
    /// pointed at (including an extensionless shim rescued by its spawnable sibling).</summary>
    /// <remarks>
    /// <para>OPTIMISTIC for a BYO <see cref="IProcessRunner"/>: a custom runner (sandbox / remote / audited
    /// execution) resolves the command in ITS OWN environment, not the host's local PATH — so this returns
    /// true without probing rather than skip the provider and never reach the runner. A truly missing binary
    /// then surfaces as a <see cref="LlmVerdict.Failed"/> verdict on the actual call, and the router falls
    /// over to the next candidate.</para>
    /// <para>An explicit command is CHECKED rather than trusted: a host that ships its own CLI copy wants a
    /// deleted/never-unpacked binary to make this candidate unavailable — the router then skips it, instead of
    /// discovering the absence as a failed turn.</para>
    /// </remarks>
    public bool IsAvailable => runner is not ProcessRunner || ProcessRunner.CommandExists(ResolveCommand().Exe);

    /// <summary>Resolve the command override / environment seams into exe + prefix args.</summary>
    public (string Exe, IReadOnlyList<string> PrefixArgs) ResolveCommand() =>
        CliCommand.Resolve(command, dialect.DefaultCommand, dialect.CommandEnvironmentVariables);

    // ── completion ───────────────────────────────────────────────────────────

    /// <summary>Run one buffered completion.</summary>
    public async Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default)
    {
        WarnIfRequestToolsIgnored(req);
        // when a provisioner is registered, it stands up a tool host and hands back the CLI args; the
        // session is torn down after the process exits
        await using var session = provisioner is null ? null : await provisioner.ProvisionAsync(ct).ConfigureAwait(false);
        var (exe, prefixArgs) = ResolveCommand();
        var (argv, stdin) = BuildInvocation(req, prefixArgs, session?.ExtraArgs);

        // The buffered path treats `timeout` as an INACTIVITY window (a slow-but-alive turn — a big prompt,
        // a long tool loop — keeps re-arming it), with `maxDuration` an absolute backstop so a chatty child
        // that never stalls is still bounded. The backstop is MaxProviderTimeout, but never below the
        // inactivity window (a consumer budget above the ceiling raises it, not the reverse).
        var timeout = options.ResolveTimeout(req);
        var maxDuration = options.MaxProviderTimeout < timeout ? timeout : options.MaxProviderTimeout;
        ProcessResult result;
        try
        {
            result = await runner.RunAsync(exe, argv, stdin: stdin, inactivityTimeout: timeout, maxDuration: maxDuration,
                workingDirectory: NeutralWorkingDirectory, environment: environment, ct: ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new LlmReply("", LlmVerdict.Failed, Detail: $"spawn failed: {ex.Message}");
        }

        if (result.TimedOut)
            return new LlmReply("", LlmVerdict.Timeout,
                Detail: result.TimeoutKind == ProcessTimeoutKind.MaxDuration
                    ? $"{dialect.Id} exceeded max duration {maxDuration}"
                    : $"{dialect.Id} stalled — no output for {timeout}");

        var stderrTail = Tail(result.StdErr);

        string text = "", contentText = "";
        LlmUsage? usage = null;
        string? failure = null;
        var sawResult = false;
        foreach (var raw in result.StdOut.Split('\n'))
        {
            // strip the terminator so a dialect sees the SAME line both paths hand it — the streamed path's
            // ReadLineAsync already drops it, so a CRLF-emitting child would otherwise leave a trailing '\r'
            // here and only here (an exact-match ParseLine would then work streamed and fail buffered)
            var evt = dialect.ParseLine(raw.TrimEnd('\r'));
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

        // A backend that reported its OWN failure is authoritative, even at exit 0 and even over partial
        // content: half an answer plus "the turn failed" is not an answer, and returning Ok would be the
        // empty-Ok mistake in a different costume (the router would never retry). The backend's wording is
        // classified so the verdict is actionable (401 → AuthFailed cools the host; 429 → RateLimited).
        //
        // It is authoritative over a NON-ZERO EXIT too, which is why this is read before the exit code below
        // — the same "parse first, then fall back to the exit code" ordering StatusAsync already uses, and
        // for the same reason. MEASURED on codex-cli 0.146.0 (2026-08-05, an expired login): a turn can
        // print {"type":"turn.failed","error":{"message":"… 401 …"}} AND exit non-zero, with nothing but
        // ordinary startup chatter ("Reading prompt from stdin...") on stderr. Classifying the chatter loses
        // the only account of what went wrong AND downgrades AuthFailed to a bare Failed, so the router
        // advances instead of cooling the host and the caller is told the CLI is broken when the remedy is
        // to log in again. The exit code is kept in the detail — it is context, not the reason.
        if (failure is { Length: > 0 })
            return new LlmReply("", LlmVerdictClassifier.FromErrorText(failure),
                Detail: result.ExitCode != 0 ? $"exit {result.ExitCode}: {failure}" : failure);

        // No in-band account of the failure: the exit code and stderr are all there is. (A backend that
        // exits non-zero after printing a complete answer is still a failed run — a truncated answer
        // labelled complete is the outcome this prevents.)
        if (result.ExitCode != 0)
            return new LlmReply("", LlmVerdictClassifier.FromErrorText(stderrTail), Detail: $"exit {result.ExitCode}: {stderrTail}");

        if (text.Length == 0)
        {
            _logger.LogWarning("{Provider} produced no content ({SawResult}); stderr: {Tail}", dialect.Id, sawResult, stderrTail);
            return new LlmReply("", LlmVerdict.Failed,
                Detail: stderrTail.Length > 0 ? stderrTail : "no output produced");
        }
        return new LlmReply(text, LlmVerdict.Ok, usage);
    }

    /// <summary>Stream one completion. Content chunks arrive as the CLI prints them, followed by exactly one
    /// terminal chunk (<c>Final</c> when anything was delivered, otherwise <c>Error</c>).</summary>
    /// <remarks>Bounded by the same two clocks as <see cref="CompleteAsync"/> — the resolved timeout as an
    /// inactivity window, <see cref="LyntaiOptions.MaxProviderTimeout"/> as the absolute backstop — plus
    /// caller cancellation, which also kills the process tree when the enumerator is abandoned.</remarks>
    public async IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, [EnumeratorCancellation] CancellationToken ct = default)
    {
        WarnIfRequestToolsIgnored(req);
        // the host lives for the whole stream (the CLI calls tools throughout); torn down when this iterator
        // is disposed
        await using var session = provisioner is null ? null : await provisioner.ProvisionAsync(ct).ConfigureAwait(false);
        var (exe, prefixArgs) = ResolveCommand();
        var (argv, stdin) = BuildInvocation(req, prefixArgs, session?.ExtraArgs);

        var sawContent = false;
        string resultText = "";
        LlmUsage? usage = null;

        // Two clocks, the same pair the buffered path uses. `timeout` is the INACTIVITY window, so a
        // slow-but-alive turn (a long tool loop, a big prompt) re-arms it and finishes; `maxDuration` is the
        // absolute backstop, because inactivity ALONE cannot see the other failure — a CHATTY child that
        // never stalls and never finishes re-arms the window on every line and would stream forever. The
        // backstop is MaxProviderTimeout, but never below the inactivity window (a consumer budget above the
        // ceiling raises it, not the reverse). Caller cancellation still wins, and abandoning the enumerator
        // still kills the process tree.
        //
        // This is a COMPLETION's ceiling, and it belongs only here: the long-running agent turns
        // (ClaudeAgentSession / CodexAgentSession) drive IProcessRunner directly rather than this engine,
        // and must keep NO wall clock — an hour-long healthy session is exactly what one would kill.
        var timeout = options.ResolveTimeout(req);
        var maxDuration = options.MaxProviderTimeout < timeout ? timeout : options.MaxProviderTimeout;
        var lines = runner.StreamLinesAsync(exe, argv, stdin: stdin, inactivityTimeout: timeout, maxDuration: maxDuration,
            workingDirectory: NeutralWorkingDirectory, environment: environment, ct: ct);
        var enumerator = lines.GetAsyncEnumerator(ct);
        await using (enumerator.ConfigureAwait(false))
        {
            // the guarded loop lives once in Core. No clock here — the inactivity window is the RUNNER's
            // (its timeout arrives as ProcessTimeoutException), so ANY OperationCanceledException is
            // cancellation and PROPAGATES: onFault returns null for it (the router decides — T8).
            var guarded = GuardedStream.ReadAll<string, LlmChunk>(
                async () => await enumerator.MoveNextAsync().ConfigureAwait(false) ? enumerator.Current : null,
                ex => ex switch
                {
                    OperationCanceledException => null,
                    ProcessTimeoutException => LlmChunk.Error(LlmVerdict.Timeout, ex.Message),
                    ProcessRunException pre => LlmChunk.Error(
                        LlmVerdictClassifier.FromErrorText(pre.StdErrTail), $"exit {pre.ExitCode}: {pre.StdErrTail}"),
                    _ => LlmChunk.Error(LlmVerdict.Failed, $"spawn failed: {ex.Message}"),
                },
                ct);
            await foreach (var (line, terminal) in guarded.ConfigureAwait(false))
            {
                if (terminal is not null)
                {
                    yield return terminal;
                    yield break;
                }

                var evt = dialect.ParseLine(line!);
                // The commit gate is the ROUTER's, to the letter: Kind == Content AND Text.Length > 0. An
                // EMPTY content event is not delivered content, so it falls through as if the dialect had
                // reported Ignored — which is what a line carrying no content is supposed to be. Counting it
                // did three wrong things at once: it disabled fallback for a zero-content FIRST chunk (the
                // one case the router's gate exists for), it ended a no-output stream as `Final` — a fully
                // successful empty answer — and it suppressed the result-only delivery below, dropping the
                // answer of a stream whose text arrived on the terminal result line. A dialect is asked to
                // report an empty line as Ignored, but a dialect is third-party code, so the engine holds
                // whether it does or not.
                if (evt.Kind == CliOutputEventKind.Content && evt.Text.Length > 0)
                {
                    sawContent = true;
                    yield return LlmChunk.Content(evt.Text);
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
                    yield return LlmChunk.Error(LlmVerdictClassifier.FromErrorText(evt.Text), evt.Text);
                    yield break;
                }
            }
        }

        if (!sawContent && resultText.Length > 0)
        {
            yield return LlmChunk.Content(resultText); // result-only stream still delivers the text
            sawContent = true;
        }

        // content that arrived without a terminal result event is still a successful stream — a trailing
        // Error here would mark a fully-delivered answer as a failed run
        if (sawContent)
            yield return LlmChunk.Final(usage);
        else
            yield return LlmChunk.Error(LlmVerdict.Failed, "no output produced");
    }

    /// <summary>Assemble argv + stdin for one call: prefix args, the dialect's completion args, any tool-host
    /// args, and the prompt — which goes LAST when this CLI takes it positionally.</summary>
    private (List<string> Argv, string? Stdin) BuildInvocation(
        LlmRequest req, IReadOnlyList<string> prefixArgs, IReadOnlyList<string>? extraArgs)
    {
        var prompt = dialect.BuildPrompt(req);
        // the tool-host args go THROUGH the dialect, never around it: only the dialect knows whether its
        // argv ends in options (append is fine) or in a positional (append feeds them to the model as
        // prompt text, and on codex a swallowed flag is a spent turn)
        var argv = prefixArgs.Concat(dialect.BuildCompletionArgs(req, extraArgs ?? [])).ToList();
        if (dialect.PromptDelivery != CliPromptDelivery.Argument) return (argv, prompt);
        argv.Add(prompt);
        return (argv, null);
    }

    // ── self-maintenance: version, update, pinned install ────────────────────

    /// <summary>Report the installed backend without running a turn.</summary>
    public async Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        if (dialect.VersionArgs is not { } versionArgs)
            return new ProviderProbeResult(false, Detail: $"{dialect.Id} has no turn-free version readout");

        var result = await RunMaintenanceAsync(versionArgs, dialect.MaintenanceTimeout, dialect.MaintenanceTimeout, ct)
            .ConfigureAwait(false);
        if (result.Failure is { } failure) return new ProviderProbeResult(false, Detail: $"probe failed: {failure}");
        if (result.Process!.TimedOut)
            return new ProviderProbeResult(false,
                Detail: $"{dialect.Id} reported no version within {dialect.MaintenanceTimeout}");
        if (result.Process.ExitCode != 0)
            return new ProviderProbeResult(false, Detail: $"exit {result.Process.ExitCode}: {Tail(result.Process.StdErr)}");

        // some launchers print the banner on stderr; the first non-empty line is the version line
        var line = CliVersionLine.FirstLine(result.Process.StdOut);
        if (line.Length == 0) line = CliVersionLine.FirstLine(result.Process.StdErr);
        var (version, model) = dialect.ParseVersionLine(line);
        return new ProviderProbeResult(true, version, model, line.Length > 0 ? line : null);
    }

    /// <summary>Run the backend's own updater.</summary>
    public Task<ProviderUpdateResult> UpdateAsync(CancellationToken ct = default) =>
        dialect.UpdateArgs is { } updateArgs
            ? SelfMaintainAsync(updateArgs, $"{dialect.Id} update", ct)
            : Task.FromResult(new ProviderUpdateResult(false, false, Detail: $"{dialect.Id} has no self-updater to drive"));

    /// <summary>Ask the backend to install a named version of itself.</summary>
    public Task<ProviderUpdateResult> InstallAsync(ProviderInstallRequest? request = null, CancellationToken ct = default) =>
        dialect.TryBuildInstallArgs(request, out var installArgs, out var refusal)
            ? SelfMaintainAsync(installArgs, $"{dialect.Id} install", ct)
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
        var maxDuration = options.MaxProviderTimeout < inactivity ? inactivity : options.MaxProviderTimeout;

        var run = await RunMaintenanceAsync(maintenanceArgs, inactivity, maxDuration, ct).ConfigureAwait(false);
        if (run.Failure is { } failure) return Unchanged($"{label} failed: {failure}");
        if (run.Process!.TimedOut) return Unchanged($"{label} stalled — no output for {inactivity}");
        if (run.Process.ExitCode != 0) return Unchanged($"exit {run.Process.ExitCode}: {Tail(run.Process.StdErr)}");

        // the tool's own wording is the diagnostic; whether anything CHANGED is the version comparison
        var after = await ProbeAsync(ct).ConfigureAwait(false);
        var updated = before.Version is { } from && after.Version is { } to &&
            !string.Equals(from, to, StringComparison.OrdinalIgnoreCase);
        var output = Tail(run.Process.StdOut.Length > 0 ? run.Process.StdOut : run.Process.StdErr);
        return new ProviderUpdateResult(true, updated, before.Version, after.Version,
            output.Length > 0 ? output : null);

        // a failed run installed nothing: the "after" version is the "before" one, not a fresh probe
        ProviderUpdateResult Unchanged(string detail) => new(false, false, before.Version, before.Version, detail);
    }

    // ── self-maintenance: auth ───────────────────────────────────────────────

    /// <summary>Report whether the backend is authenticated — and as whom — without running a turn.</summary>
    /// <remarks>The dialect's parsed state WINS over the exit code: a signed-out backend may report its state
    /// and still exit non-zero, and that is an answer, not a broken backend. An output shape the dialect can't
    /// read reports <c>Authenticated: false</c> with the raw text — it never guesses a signed-in state.</remarks>
    public async Task<ProviderAuthStatus> StatusAsync(CancellationToken ct = default)
    {
        if (dialect.AuthStatusArgs is not { } statusArgs)
            return new ProviderAuthStatus(false, Detail: $"{dialect.Id} has no turn-free auth readout");

        var result = await RunMaintenanceAsync(statusArgs, dialect.MaintenanceTimeout, dialect.MaintenanceTimeout, ct)
            .ConfigureAwait(false);
        if (result.Failure is { } failure)
            return new ProviderAuthStatus(false, Detail: $"auth status failed: {failure}");
        if (result.Process!.TimedOut)
            return new ProviderAuthStatus(false,
                Detail: $"{dialect.Id} reported no auth status within {dialect.MaintenanceTimeout}");

        // parse the WHOLE body (Tail() keeps the LAST N chars and would decapitate a document)
        var body = result.Process.StdOut.Length > 0 ? result.Process.StdOut : result.Process.StdErr;
        if (dialect.ParseAuthStatus(body) is { } status)
            return status with { Detail = Tail(body) };

        if (result.Process.ExitCode != 0)
            return new ProviderAuthStatus(false, Detail: $"exit {result.Process.ExitCode}: {Tail(result.Process.StdErr)}");

        var tail = Tail(body);
        return new ProviderAuthStatus(false, Detail: tail.Length > 0
            ? $"unrecognized auth status output: {tail}"
            : $"{dialect.Id} reported no auth status");
    }

    /// <summary>Start the backend's sign-in flow, then report the state it left behind. BLOCKS until the flow
    /// completes, fails, or <see cref="ICliProviderDialect.LoginTimeout"/> expires; cancelling
    /// <paramref name="ct"/> abandons the wait (and kills the process tree).</summary>
    public Task<ProviderAuthResult> LoginAsync(ProviderLoginRequest? request = null, CancellationToken ct = default) =>
        dialect.TryBuildLoginArgs(request, out var loginArgs, out var refusal)
            // a login prints a URL and then goes SILENT while a human clicks, so the per-chunk inactivity
            // window a probe uses would kill a live flow — both clocks are the same bounded budget here
            ? RunAuthCommandAsync(loginArgs, $"{dialect.Id} login", dialect.LoginTimeout, dialect.LoginTimeout, ct)
            : Task.FromResult(new ProviderAuthResult(false, Detail: refusal));

    /// <summary>Sign the backend out, and report the state it left behind.</summary>
    public Task<ProviderAuthResult> LogoutAsync(CancellationToken ct = default) =>
        dialect.LogoutArgs is { } logoutArgs
            ? RunAuthCommandAsync(logoutArgs, $"{dialect.Id} logout", dialect.MaintenanceTimeout, dialect.MaintenanceTimeout, ct)
            : Task.FromResult(new ProviderAuthResult(false, Detail: $"{dialect.Id} has no logout to drive"));

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
            return new ProviderAuthResult(false, Detail: $"exit {result.Process.ExitCode}: {Tail(result.Process.StdErr)}");

        var status = await StatusAsync(ct).ConfigureAwait(false);
        var output = Tail(result.Process.StdOut.Length > 0 ? result.Process.StdOut : result.Process.StdErr);
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

    private static string Tail(string text, int max = 500)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[^max..];
    }

    /// <summary>A CLI that doesn't take request-level tool declarations must not drop them SILENTLY — a
    /// caller that put tools on the request and routed here gets a diagnostic instead of a mystery.</summary>
    private void WarnIfRequestToolsIgnored(LlmRequest req)
    {
        if (!dialect.SupportsToolCalls && req.Tools is { Count: > 0 })
            _logger.LogWarning(
                "{Provider} ignores LlmRequest.Tools ({Count} declaration(s) dropped) — this CLI provider " +
                "doesn't take request-level tool declarations; expose tools via AddMcpToolHost(<its dialect>) instead.",
                dialect.Id, req.Tools.Count);
    }
}
