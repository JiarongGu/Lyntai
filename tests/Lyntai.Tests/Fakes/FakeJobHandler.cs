using Lyntai.Jobs;

namespace Lyntai.Tests.Fakes;

/// <summary>Scripted <see cref="IJobHandler"/>: a delegate decides the outcome; counts its calls.</summary>
public sealed class FakeJobHandler(string type, Func<JobContext, Task<JobOutcome>> handle) : IJobHandler
{
    private int _calls;

    public int Calls => _calls;
    public string Type => type;

    public Task<JobOutcome> HandleAsync(JobContext ctx, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _calls);
        return handle(ctx);
    }
}
