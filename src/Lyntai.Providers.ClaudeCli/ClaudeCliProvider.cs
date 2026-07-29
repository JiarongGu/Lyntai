using System.Runtime.CompilerServices;
using Lyntai.Agents;
using Lyntai.Llm;
using Lyntai.Llm.Streaming;
using Lyntai.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>
/// Spawns the authenticated `claude` CLI (no API key) through <see cref="ProcessRunner"/> and maps
/// stream-json to <see cref="LlmReply"/>/<see cref="LlmChunk"/> + verdict. The command resolves from
/// (in order): the ctor override, <c>LYNTAI_PROVIDER_CMD</c>, <c>CLAUDE_CMD</c>, then a plain
/// <c>claude</c> from PATH — the env seams are what let tests/e2e point at the deterministic stub.
/// </summary>
public sealed class ClaudeCliProvider(
    IProcessRunner runner,
    LyntaiOptions options,
    ILogger<ClaudeCliProvider>? logger = null,
    string? command = null,
    ICliToolProvisioner? provisioner = null) : ILlmProvider, IProviderInstallation, IProviderUpdater
{
    public const string ProviderId = "claude-cli";

    /// <summary>Design §6 CLI hygiene: spawn from a NEUTRAL cwd — never the host app's inherited
    /// working directory, whose project config (CLAUDE.md, hooks, memory) the claude CLI would
    /// otherwise load into every library completion and judge call, silently skewing them.</summary>
    internal static readonly string NeutralWorkingDirectory = Path.GetTempPath();

    private readonly ILogger _logger = logger ?? NullLogger<ClaudeCliProvider>.Instance;

    public string Id => ProviderId;

    /// <summary>Whether the <c>claude</c> CLI looks callable. For the built-in <see cref="ProcessRunner"/>
    /// this probes the resolved command on the local PATH (an explicit override is trusted without probing).</summary>
    /// <remarks>OPTIMISTIC for a BYO <see cref="IProcessRunner"/>: a custom runner (sandbox / remote / audited
    /// execution) resolves the command in ITS OWN environment, not the host's local PATH — so this returns
    /// <c>true</c> without probing rather than skip the provider and never reach the runner. A truly missing
    /// binary then surfaces as a <see cref="LlmVerdict.Failed"/> verdict on the actual call, and the router
    /// falls over to the next candidate.</remarks>
    public bool IsAvailable
    {
        get
        {
            // A custom IProcessRunner (sandbox / remote / audited execution) resolves the command in
            // ITS environment, not the host's local PATH — so don't probe the local PATH and skip the
            // provider, or the BYO runner would never be reached. Be optimistic; a truly missing binary
            // then surfaces as a Failed verdict on the actual call, and the router falls over.
            if (runner is not ProcessRunner) return true;

            var (exe, _) = ResolveCommand();
            if (!string.Equals(exe, "claude", StringComparison.OrdinalIgnoreCase)) return true; // explicit override
            var resolved = ProcessRunner.ResolveCommandPath("claude");
            return !string.Equals(resolved, "claude", StringComparison.OrdinalIgnoreCase) && File.Exists(resolved);
        }
    }

    public async Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default)
    {
        WarnIfRequestToolsIgnored(req);
        // when a provisioner is registered (AddMcpToolHost + the claude dialect), it stands up an MCP host exposing
        // the app's tools and hands back the CLI args; the session tears it down after the process exits
        await using var session = provisioner is null ? null : await provisioner.ProvisionAsync(ct).ConfigureAwait(false);
        var (exe, prefixArgs) = ResolveCommand();
        var argv = prefixArgs.Concat(ClaudeArgs.Build(req.Model)).Concat(session?.ExtraArgs ?? []).ToList();
        var prompt = ClaudeArgs.BuildPrompt(req);

        // The buffered path treats `timeout` as an INACTIVITY window (a slow-but-alive turn — a big prompt,
        // a long tool loop — keeps re-arming it), with `maxDuration` an absolute backstop so a chatty child
        // that never stalls is still bounded. The backstop is MaxProviderTimeout, but never below the
        // inactivity window (a consumer budget above the ceiling raises it, not the reverse).
        var timeout = options.ResolveTimeout(req);
        var maxDuration = options.MaxProviderTimeout < timeout ? timeout : options.MaxProviderTimeout;
        ProcessResult result;
        try
        {
            result = await runner.RunAsync(exe, argv, stdin: prompt, inactivityTimeout: timeout, maxDuration: maxDuration,
                workingDirectory: NeutralWorkingDirectory, ct: ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new LlmReply("", LlmVerdict.Failed, Detail: $"spawn failed: {ex.Message}");
        }

        if (result.TimedOut)
            return new LlmReply("", LlmVerdict.Timeout,
                Detail: result.TimeoutKind == ProcessTimeoutKind.MaxDuration
                    ? $"claude CLI exceeded max duration {maxDuration}"
                    : $"claude CLI stalled — no output for {timeout}");

        var stderrTail = Tail(result.StdErr);
        if (result.ExitCode != 0)
            return new LlmReply("", LlmVerdictClassifier.FromErrorText(stderrTail), Detail: $"exit {result.ExitCode}: {stderrTail}");

        string text = "", assistantText = "";
        LlmUsage? usage = null;
        var sawResult = false;
        foreach (var line in result.StdOut.Split('\n'))
        {
            var evt = StreamJsonParser.Parse(line);
            if (evt.Kind == StreamJsonEventKind.AssistantText) assistantText += evt.Text;
            if (evt.Kind == StreamJsonEventKind.Result)
            {
                sawResult = true;
                text = evt.Text;
                usage = evt.Usage;
            }
        }
        if (text.Length == 0) text = assistantText; // result-less streams still carry assistant text

        if (text.Length == 0)
        {
            _logger.LogWarning("claude-cli produced no content ({SawResult}); stderr: {Tail}", sawResult, stderrTail);
            return new LlmReply("", LlmVerdict.Failed,
                Detail: stderrTail.Length > 0 ? stderrTail : "no output produced");
        }
        return new LlmReply(text, LlmVerdict.Ok, usage);
    }

    public async IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, [EnumeratorCancellation] CancellationToken ct = default)
    {
        WarnIfRequestToolsIgnored(req);
        // host lives for the whole stream (the CLI calls tools throughout); torn down when this iterator
        // is disposed
        await using var session = provisioner is null ? null : await provisioner.ProvisionAsync(ct).ConfigureAwait(false);
        var (exe, prefixArgs) = ResolveCommand();
        var argv = prefixArgs.Concat(ClaudeArgs.Build(req.Model)).Concat(session?.ExtraArgs ?? []).ToList();
        var prompt = ClaudeArgs.BuildPrompt(req);

        var sawContent = false;
        string resultText = "";
        LlmUsage? usage = null;

        var lines = runner.StreamLinesAsync(exe, argv, stdin: prompt, inactivityTimeout: options.ResolveTimeout(req),
            workingDirectory: NeutralWorkingDirectory, ct: ct);
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

                var evt = StreamJsonParser.Parse(line!);
                if (evt.Kind == StreamJsonEventKind.AssistantText)
                {
                    sawContent = true;
                    yield return LlmChunk.Content(evt.Text);
                }
                else if (evt.Kind == StreamJsonEventKind.Result)
                {
                    resultText = evt.Text;
                    usage = evt.Usage;
                }
            }
        }

        if (!sawContent && resultText.Length > 0)
        {
            yield return LlmChunk.Content(resultText); // result-only stream still delivers the text
            sawContent = true;
        }

        // content that arrived without a terminal result event is still a successful stream — a
        // trailing Error here would mark a fully-delivered answer as a failed run
        if (sawContent)
            yield return LlmChunk.Final(usage);
        else
            yield return LlmChunk.Error(LlmVerdict.Failed, "no output produced");
    }

    /// <summary>How long a maintenance spawn may go SILENT before it's treated as unreachable. A version
    /// readout is sub-second work, so this is a stall detector, not a work budget — deliberately not the
    /// provider timeout (a probe must not hang a settings screen for two minutes).</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Report the installed CLI without running a turn: <c>claude --version</c> through the same
    /// BYO <see cref="IProcessRunner"/> / <see cref="ClaudeCommand"/> seams as a completion, from the
    /// neutral working directory, with no stdin.</summary>
    /// <remarks>
    /// <para><c>--version</c> is the ONLY turn-free question asked, deliberately: the CLI treats an
    /// unrecognized token as a PROMPT and spends a turn answering it, so probing by guessing subcommands
    /// would silently cost tokens.</para>
    /// <para><see cref="ProviderProbeResult.Model"/> is therefore null against today's CLI — it has no
    /// turn-free way to report its resolved model. Read the model the CLI ACTUALLY used from
    /// <see cref="UsageFinal.Model"/> on an agent run; this fills in only if a future build labels one on
    /// its version line.</para>
    /// </remarks>
    public async Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        var (exe, prefixArgs) = ResolveCommand();
        ProcessResult result;
        try
        {
            result = await runner.RunAsync(exe, [.. prefixArgs, "--version"], stdin: null,
                inactivityTimeout: ProbeTimeout, maxDuration: ProbeTimeout,
                workingDirectory: NeutralWorkingDirectory, ct: ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ProviderProbeResult(false, Detail: $"probe failed: {ex.Message}");
        }

        if (result.TimedOut)
            return new ProviderProbeResult(false, Detail: $"claude CLI reported no version within {ProbeTimeout}");
        if (result.ExitCode != 0)
            return new ProviderProbeResult(false, Detail: $"exit {result.ExitCode}: {Tail(result.StdErr)}");

        // some launchers print the banner on stderr; the first non-empty line is the version line
        var line = ClaudeVersionLine.FirstLine(result.StdOut);
        if (line.Length == 0) line = ClaudeVersionLine.FirstLine(result.StdErr);
        var (version, model) = ClaudeVersionLine.Parse(line);
        return new ProviderProbeResult(true, version, model, line.Length > 0 ? line : null);
    }

    /// <summary>Run the CLI's OWN updater (<c>claude update</c>) and report what changed: probe → update →
    /// re-probe. The CLI has no check-only mode, so "an update was available" is reported after the fact as
    /// <see cref="ProviderUpdateResult.Updated"/> (the version moved). Installing/pinning a binary is NOT
    /// done here — this only drives the updater the CLI already ships.</summary>
    /// <remarks>Unlike a probe this can legitimately run for minutes (it downloads), so it gets the
    /// configured provider clocks: <see cref="LyntaiOptions.ProviderTimeout"/> as the inactivity window and
    /// <see cref="LyntaiOptions.MaxProviderTimeout"/> as the absolute backstop, never below it.</remarks>
    public async Task<ProviderUpdateResult> UpdateAsync(CancellationToken ct = default)
    {
        var before = await ProbeAsync(ct).ConfigureAwait(false);
        var (exe, prefixArgs) = ResolveCommand();
        var inactivity = options.ProviderTimeout;
        var maxDuration = options.MaxProviderTimeout < inactivity ? inactivity : options.MaxProviderTimeout;

        ProcessResult result;
        try
        {
            result = await runner.RunAsync(exe, [.. prefixArgs, "update"], stdin: null,
                inactivityTimeout: inactivity, maxDuration: maxDuration,
                workingDirectory: NeutralWorkingDirectory, ct: ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return Unchanged($"update failed: {ex.Message}");
        }

        if (result.TimedOut)
            return Unchanged($"claude update stalled — no output for {inactivity}");
        if (result.ExitCode != 0)
            return Unchanged($"exit {result.ExitCode}: {Tail(result.StdErr)}");

        // the updater's own wording is the diagnostic; whether anything CHANGED is the version comparison
        var after = await ProbeAsync(ct).ConfigureAwait(false);
        var updated = before.Version is { } from && after.Version is { } to &&
            !string.Equals(from, to, StringComparison.OrdinalIgnoreCase);
        var output = Tail(result.StdOut.Length > 0 ? result.StdOut : result.StdErr);
        return new ProviderUpdateResult(true, updated, before.Version, after.Version,
            output.Length > 0 ? output : null);

        // a failed update installed nothing: the "after" version is the "before" one, not a fresh probe
        ProviderUpdateResult Unchanged(string detail) =>
            new(false, false, before.Version, before.Version, detail);
    }

    /// <summary>Resolve the command override / env seams into exe + prefix args (see <see cref="ClaudeCommand"/>).</summary>
    internal (string Exe, IReadOnlyList<string> PrefixArgs) ResolveCommand() => ClaudeCommand.Resolve(command);

    private static string Tail(string text, int max = 500)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[^max..];
    }

    /// <summary>The CLI provider does NOT consume <see cref="LlmRequest.Tools"/> (native tool declarations
    /// on the request): <c>SupportsToolCalls</c> is false and <see cref="ClaudeArgs.Build"/> ignores them.
    /// Tool-calling on this provider goes through the separate MCP provisioner (the app's registered
    /// <c>ITool</c>s hosted as an ephemeral MCP server), not request-level declarations. Warn rather than
    /// drop them silently, so a caller that put tools on the request + routed here gets a diagnostic.</summary>
    private void WarnIfRequestToolsIgnored(LlmRequest req)
    {
        if (req.Tools is { Count: > 0 })
            _logger.LogWarning(
                "claude-cli ignores LlmRequest.Tools ({Count} declaration(s) dropped) — the CLI provider " +
                "doesn't take request-level tool declarations; expose tools via AddMcpToolHost(new ClaudeCliMcpDialect()) instead.",
                req.Tools.Count);
    }
}
