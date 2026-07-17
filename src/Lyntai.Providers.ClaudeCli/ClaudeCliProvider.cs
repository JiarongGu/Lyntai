using System.Runtime.CompilerServices;
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
    string? command = null) : ILlmProvider
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
        var (exe, prefixArgs) = ResolveCommand();
        var argv = prefixArgs.Concat(ClaudeArgs.Build(req.Model)).ToList();
        var prompt = ClaudeArgs.BuildPrompt(req);

        ProcessResult result;
        try
        {
            result = await runner.RunAsync(exe, argv, stdin: prompt, timeout: options.ProviderTimeout,
                workingDirectory: NeutralWorkingDirectory, ct: ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new LlmReply("", LlmVerdict.Failed, Detail: $"spawn failed: {ex.Message}");
        }

        if (result.TimedOut)
            return new LlmReply("", LlmVerdict.Timeout, Detail: $"claude CLI exceeded {options.ProviderTimeout}");

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
        var (exe, prefixArgs) = ResolveCommand();
        var argv = prefixArgs.Concat(ClaudeArgs.Build(req.Model)).ToList();
        var prompt = ClaudeArgs.BuildPrompt(req);

        var sawContent = false;
        string resultText = "";
        LlmUsage? usage = null;

        var lines = runner.StreamLinesAsync(exe, argv, stdin: prompt, timeout: options.ProviderTimeout,
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

    /// <summary>Parse the (possibly multi-token, possibly quoted) command override into exe + prefix
    /// args — e.g. <c>node /path/provider-stub.mjs</c> spawns node with the stub as its first arg.</summary>
    internal (string Exe, IReadOnlyList<string> PrefixArgs) ResolveCommand()
    {
        var cmd = command
            ?? Environment.GetEnvironmentVariable("LYNTAI_PROVIDER_CMD")
            ?? Environment.GetEnvironmentVariable("CLAUDE_CMD")
            ?? "claude";
        var tokens = Tokenize(cmd);
        return tokens.Count == 0 ? ("claude", []) : (tokens[0], tokens.Skip(1).ToList());
    }

    internal static List<string> Tokenize(string commandLine)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        foreach (var c in commandLine)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }

    private static string Tail(string text, int max = 500)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[^max..];
    }
}
