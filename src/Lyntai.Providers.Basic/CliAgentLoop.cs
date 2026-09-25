using System.Runtime.CompilerServices;
using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Inference.Cli;
using Lyntai.Inference.Streaming;
using Lyntai.Processes;
using Microsoft.Extensions.Logging;

namespace Lyntai.Providers.Basic;

/// <summary>One spawn of a CLI agent turn, as <see cref="IAgentSession.StreamAsync"/> promises it — the loop
/// every CLI agent session in this package shares, so they end a turn the same way. A session keeps only what
/// is its CLI's own: the argv, the stdin and the line reader.
/// <list type="bullet">
/// <item>Exactly ONE <see cref="SessionEnded"/>: the first the reader yields wins, and anything printed
/// after it may add events but never a second ending.</item>
/// <item>A process fault becomes that terminal through <see cref="CliFault"/>; a cancellation
/// propagates.</item>
/// <item>No terminal at all is a <see cref="ProviderVerdict.Failed"/> one, and the diagnostic tells
/// "printed nothing" (a bad invocation) from "streamed, then died before its terminal" (a crash mid-turn).</item>
/// </list>
/// No clock is armed here: the inactivity window is the runner's, and an agent turn has no wall clock.</summary>
internal static class CliAgentLoop
{
    /// <summary>The single terminal of a turn refused before anything was spawned — an input the CLI cannot
    /// be handed as given. <see cref="ProviderVerdict.Unsupported"/>, with <paramref name="subtype"/> naming
    /// WHICH input.</summary>
    public static SessionEnded Refused(string subtype, string? why) =>
        new(ProviderVerdict.Unsupported, true, subtype, null, null, why);

    /// <summary>Run one turn and stream its events.</summary>
    /// <param name="runner">Spawns the CLI.</param>
    /// <param name="exe">The resolved executable.</param>
    /// <param name="argv">Prefix args plus the turn's argv.</param>
    /// <param name="stdin">The prompt.</param>
    /// <param name="timeout">The runner's inactivity window.</param>
    /// <param name="options">The turn — its working directory.</param>
    /// <param name="environment">Extra environment variables for the spawn.</param>
    /// <param name="read">The backend's line reader: 0..N events per line, never a throw.</param>
    /// <param name="logger">Where a turn with no terminal is reported.</param>
    /// <param name="backend">The CLI's name, for the diagnostic.</param>
    /// <param name="ct">Cancels the turn and kills the process tree.</param>
    public static async IAsyncEnumerable<AgentStreamEvent> RunAsync(
        IProcessRunner runner, string exe, IReadOnlyList<string> argv, string stdin, TimeSpan timeout,
        AgentSessionOptions options, IReadOnlyDictionary<string, string>? environment,
        Func<string, IReadOnlyList<AgentStreamEvent>> read, ILogger logger, string backend,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var sawTerminal = false;
        var sawContent = false;
        string? sessionId = null;

        var lines = runner.StreamLinesAsync(exe, argv, stdin: stdin, inactivityTimeout: timeout,
            workingDirectory: options.WorkingDirectory, environment: environment, ct: ct);
        var e = lines.GetAsyncEnumerator(ct);
        await using (e.ConfigureAwait(false))
        {
            // the fault terminal reads the session id at FAULT time (the closure sees the loop's updates)
            var guarded = GuardedStream.ReadAll<string, SessionEnded>(
                async () => await e.MoveNextAsync().ConfigureAwait(false) ? e.Current : null,
                ex => CliFault.Classify(ex) is { } fault
                    ? new SessionEnded(fault.Verdict, true, ex is ProcessTimeoutException ? "timeout" : null,
                        sessionId, null, fault.Detail)
                    : null,
                ct);
            await foreach (var (line, terminal) in guarded.ConfigureAwait(false))
            {
                if (terminal is not null)
                {
                    // a CLI can report failure IN BAND and still exit non-zero: the reader's terminal stands
                    if (!sawTerminal) yield return terminal;
                    yield break;
                }

                foreach (var evt in read(line!))
                {
                    switch (evt)
                    {
                        case SessionStarted started:
                            sessionId = started.SessionId;
                            break;
                        case SessionEnded ended:
                            if (sawTerminal) continue;
                            sawTerminal = true;
                            sessionId = ended.SessionId ?? sessionId;
                            break;
                        case TextDelta or ToolCall or ToolResult or Thinking:
                            sawContent = true;
                            break;
                    }
                    yield return evt;
                }
            }
        }

        if (sawTerminal) yield break;
        var diagnostic = sawContent
            ? $"the turn never terminated: {backend} streamed output but no terminal event arrived before the " +
              "process ended"
            : "no output produced (no terminal result)";
        logger.LogWarning("{Backend} agent session produced no terminal event ({Diagnostic}); session={SessionId}",
            backend, diagnostic, sessionId);
        yield return new SessionEnded(ProviderVerdict.Failed, true, null, sessionId, null, diagnostic);
    }
}
