using Lyntai.Agents;
using Lyntai.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Providers.Basic;

/// <summary>Runs a host's own CLI agent program as one agent turn, under the rules the built-in CLI doors share, so the turn
/// ends the same way whatever program it was: the host supplies only an <see cref="ICliAgentLineReader"/>, the program's own
/// grammar. A built-in door reaches the same code through its registration; this is for a program the library does not
/// ship a door for.
/// <list type="bullet">
/// <item>Exactly ONE <see cref="SessionEnded"/>: the first the reader yields wins, and a later one is dropped though its
/// events are kept.</item>
/// <item>The program is spawned by <see cref="IProcessRunner"/> as the library spawns its own: no shell, the prompt on
/// stdin, the tree killed on cancellation. The inactivity window is the runner's, and a turn has no wall clock.</item>
/// <item>The reader is told once when the output ends, with the exit code. The terminal it returns ends a turn that has none;
/// a clean end with no terminal at all is a failed turn, and a non-zero exit with none is the process fault, as for a built-in
/// door. A non-zero exit after the reader's own terminal leaves that terminal standing.</item>
/// <item>A stall, an unreadable start or a non-zero exit with no terminal becomes the terminal itself, with its
/// <see cref="AgentFailure"/>; a cancellation propagates.</item>
/// <item>A failed terminal's <see cref="AgentFailure.Delivery"/> is stamped from what the turn showed: a tool step that had
/// begun is <see cref="AgentDelivery.Uncertain"/>, any other answer is <see cref="AgentDelivery.Taken"/>, and nothing at all is
/// <see cref="AgentDelivery.NeverSent"/>. Every terminal carries the turn's evidence and unread frames.</item>
/// </list></summary>
public static class CliAgentTurn
{
    /// <summary>Run one turn of the program and stream what it does.</summary>
    /// <param name="runner">Spawns the program.</param>
    /// <param name="executable">The program, resolved by the host.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <param name="reader">The program's grammar; it serves this turn alone.</param>
    /// <param name="inactivityTimeout">How long the program may be silent before the runner ends it.</param>
    /// <param name="prompt">The prompt, written to the program's stdin; null writes none.</param>
    /// <param name="workingDirectory">Where it runs; null = the host process's own.</param>
    /// <param name="environment">Extra environment variables for the spawn; a null value removes one the child would inherit.</param>
    /// <param name="logger">Where a turn with no terminal is reported; null = silent.</param>
    /// <param name="backend">The program's name, for the diagnostic of a turn with no terminal.</param>
    /// <param name="ct">Cancels the turn and kills the process tree.</param>
    public static IAsyncEnumerable<AgentStreamEvent> RunAsync(
        IProcessRunner runner,
        string executable,
        IReadOnlyList<string> arguments,
        ICliAgentLineReader reader,
        TimeSpan inactivityTimeout,
        string? prompt = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        ILogger? logger = null,
        string backend = "cli",
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(inactivityTimeout, TimeSpan.Zero);
        return CliAgentLoop.RunLinesAsync(
            runner.StreamLinesAsync(executable, arguments, stdin: prompt, inactivityTimeout: inactivityTimeout,
                workingDirectory: workingDirectory, environment: environment, ct: ct),
            endAtTerminal: false, reader.Read, reader.Unread, logger ?? NullLogger.Instance, backend,
            announcesBeforeWrite: false, written: static () => null, answered: static () => false, ct,
            end: reader.ReadEnd);
    }
}
