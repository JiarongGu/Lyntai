using Lyntai.Agents;
using Lyntai.Llm;
using Lyntai.Processes;

namespace Lyntai.Providers;

/// <summary>The fault → terminal translation the two CLI agent sessions share. Both spawn a CLI, both read
/// its output through <c>GuardedStream.ReadAll</c>, and both owe the caller exactly one
/// <see cref="SessionEnded"/> when the process faults — so the four arms live here once instead of in two
/// copies that could start answering differently for the same exception.</summary>
internal static class CliAgentTerminal
{
    /// <summary>Translate a stream fault into the session's terminal event — or null when the fault is
    /// CANCELLATION and must propagate instead. Neither session arms a clock of its own: the inactivity
    /// window is the runner's, and it arrives as <see cref="ProcessTimeoutException"/>, so any other
    /// <see cref="OperationCanceledException"/> is the caller's.</summary>
    /// <param name="ex">The exception the guarded loop caught.</param>
    /// <param name="sessionId">The session/thread id as of FAULT time. Each session passes its own
    /// expression, which is why this reads the value at the moment of the fault rather than the one that
    /// existed when the loop was built.</param>
    internal static SessionEnded? FromFault(Exception ex, string? sessionId) => ex switch
    {
        OperationCanceledException => null,
        ProcessTimeoutException => new SessionEnded(LlmVerdict.Timeout, true, "timeout", sessionId, null, ex.Message),
        ProcessRunException pre => new SessionEnded(
            LlmVerdictClassifier.FromErrorText(pre.StdErrTail), true, null,
            sessionId, null, $"exit {pre.ExitCode}: {pre.StdErrTail}"),
        _ => new SessionEnded(LlmVerdict.Failed, true, null, sessionId, null, $"spawn failed: {ex.Message}"),
    };
}
