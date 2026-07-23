using System.Runtime.CompilerServices;
using Lyntai.Agents;
using Lyntai.Llm;
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
    ICliToolProvisioner? provisioner = null) : ILlmProvider
{
    public const string ProviderId = "claude-cli";

    /// <summary>Design §6 CLI hygiene: spawn from a NEUTRAL cwd — never the host app's inherited
    /// working directory, whose project config (CLAUDE.md, hooks, memory) the claude CLI would
    /// otherwise load into every library completion and judge call, silently skewing them.</summary>
    internal static readonly string NeutralWorkingDirectory = Path.GetTempPath();

    private readonly ILogger _logger = logger ?? NullLogger<ClaudeCliProvider>.Instance;

    public string Id => ProviderId;

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
        // when a provisioner is registered (the ClaudeCli.Mcp add-on), it stands up an MCP host exposing
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
            result = await runner.RunAsync(exe, argv, stdin: prompt, timeout: timeout, maxDuration: maxDuration,
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

        var lines = runner.StreamLinesAsync(exe, argv, stdin: prompt, timeout: options.ResolveTimeout(req),
            workingDirectory: NeutralWorkingDirectory, ct: ct);
        var enumerator = lines.GetAsyncEnumerator(ct);
        await using (enumerator.ConfigureAwait(false))
        {
            while (true)
            {
                string? line = null;
                LlmChunk? error = null;
                try
                {
                    line = await enumerator.MoveNextAsync().ConfigureAwait(false) ? enumerator.Current : null;
                }
                catch (OperationCanceledException) { throw; }
                catch (ProcessTimeoutException ex)
                {
                    error = LlmChunk.Error(LlmVerdict.Timeout, ex.Message);
                }
                catch (ProcessRunException ex)
                {
                    error = LlmChunk.Error(LlmVerdictClassifier.FromErrorText(ex.StdErrTail), $"exit {ex.ExitCode}: {ex.StdErrTail}");
                }
                catch (Exception ex)
                {
                    error = LlmChunk.Error(LlmVerdict.Failed, $"spawn failed: {ex.Message}");
                }
                if (error is not null)
                {
                    yield return error;
                    yield break;
                }
                if (line is null) break;

                var evt = StreamJsonParser.Parse(line);
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
                "doesn't take request-level tool declarations; expose tools via the ClaudeCli.Mcp provisioner (AddMcp…) instead.",
                req.Tools.Count);
    }
}
