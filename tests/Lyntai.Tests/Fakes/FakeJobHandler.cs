using Lyntai.Jobs;

namespace Lyntai.Tests.Fakes;

/// <summary>Scripted <see cref="IJobHandler"/>: a delegate decides the outcome; counts its calls.</summary>
public sealed class FakeJobHandler(string type, Func<JobContext, Task<JobOutcome>> handle) : IJobHandler
{
    private int _calls;

    public int Calls => _calls;
    public string Type => type;

    /// <summary>The outcome delegate — settable so a test can change behavior mid-run (e.g. dead-letter,
    /// then replay with a succeeding handler).</summary>
    public Func<JobContext, Task<JobOutcome>> Result { get; set; } = handle;

    public Task<JobOutcome> HandleAsync(JobContext ctx, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _calls);
        return Result(ctx);
    }
}
