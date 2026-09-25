using Lyntai.Processes;

namespace Lyntai.Inference.Cli;

/// <summary>How a spawned CLI's FAULT reads as a verdict — the one map every CLI seam shares:
/// <see cref="CliProviderEngine"/>'s completion paths and any agent session that drives
/// <see cref="IProcessRunner"/> itself. A second copy is how two seams come to answer the same exception
/// differently.</summary>
public static class CliFault
{
    /// <summary>Classify a fault raised while running a CLI, or return null when it is CANCELLATION and must
    /// propagate. The runner owns the inactivity window and reports it as
    /// <see cref="ProcessTimeoutException"/>, so any <see cref="OperationCanceledException"/> is the
    /// caller's.</summary>
    /// <param name="fault">The exception the spawn or the read raised.</param>
    /// <returns><see cref="ProviderVerdict.Timeout"/> for the runner's timeout; a non-zero exit classified
    /// from its stderr (<see cref="Exited"/>); otherwise <see cref="ProviderVerdict.Failed"/>, "spawn
    /// failed".</returns>
    public static (ProviderVerdict Verdict, string Detail)? Classify(Exception fault)
    {
        ArgumentNullException.ThrowIfNull(fault);
        return fault switch
        {
            OperationCanceledException => null,
            ProcessTimeoutException => (ProviderVerdict.Timeout, fault.Message),
            ProcessRunException exited => Exited(exited.ExitCode, exited.StdErrTail),
            _ => (ProviderVerdict.Failed, $"spawn failed: {fault.Message}"),
        };
    }

    /// <summary>A run that exited non-zero with no in-band account of its failure: the verdict is classified
    /// from the stderr tail, and the detail keeps the exit code as context.</summary>
    /// <param name="exitCode">The process exit code.</param>
    /// <param name="stdErrTail">The end of what the process wrote to stderr.</param>
    public static (ProviderVerdict Verdict, string Detail) Exited(int exitCode, string stdErrTail) =>
        (ProviderVerdictClassifier.FromErrorText(stdErrTail), $"exit {exitCode}: {stdErrTail}");
}
