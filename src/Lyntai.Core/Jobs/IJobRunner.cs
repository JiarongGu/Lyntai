namespace Lyntai.Jobs;

/// <summary>
/// Executes durable jobs. The APP owns the pump: call <see cref="RunAsync"/> from your own
/// <c>IHostedService</c>/background loop, or drive <see cref="RunOnceAsync"/> yourself — Lyntai starts no
/// threads. Each runner instance has a distinct worker id; run several (in one or many processes) and the
/// atomic claim hands each job to exactly one.
/// </summary>
public interface IJobRunner
{
    /// <summary>One pass: for every lane with work, claim up to its concurrency limit and run that batch,
    /// mapping each outcome back to the store. Returns how many jobs it ran (0 = nothing was runnable).</summary>
    Task<int> RunOnceAsync(CancellationToken ct = default);

    /// <summary>Loop <see cref="RunOnceAsync"/> until cancelled, waiting <c>PollInterval</c> only after an
    /// empty pass. Returns when <paramref name="ct"/> is cancelled.</summary>
    Task RunAsync(CancellationToken ct = default);
}
