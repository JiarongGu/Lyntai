namespace Lyntai.Jobs;

/// <summary>
/// The app's work for a job <see cref="Type"/>. Registered into a DI collection keyed by
/// <see cref="Type"/> (via <c>builder.AddJobHandler&lt;T&gt;()</c>) — adding a job type is a new class +
/// one registration, never a switch.
///
/// <para><b>Idempotency contract (at-least-once):</b> a job can run more than once — a process may die
/// after a side effect but before its checkpoint persists, and a crashed job is re-claimed and resumed.
/// So a handler MUST be idempotent from its <see cref="JobContext.Checkpoint"/>: checkpoint AFTER a
/// durable side effect (or key the effect so repeating it is a no-op), and on entry use the checkpoint to
/// skip work already done. Lyntai guarantees at-least-once, not exactly-once.</para>
/// </summary>
public interface IJobHandler
{
    /// <summary>The job type this handles (matches <see cref="JobSpec.Type"/>). Unique per registry.</summary>
    string Type { get; }

    Task<JobOutcome> HandleAsync(JobContext ctx, CancellationToken ct = default);
}
