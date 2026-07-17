using Lyntai.Cortex;

namespace Lyntai.Tests.Fakes;

public sealed class FakeScorer(string id, string group = "test", Func<ScoreContext, ScoreResult?>? score = null) : IScorer
{
    public string Id => id;
    public string Name => $"fake {id}";
    public string Group => group;
    public bool IsLlm => false;
    public int Invocations { get; private set; }

    public Task<ScoreResult?> ScoreAsync(ScoreContext ctx, CancellationToken ct)
    {
        Invocations++;
        return Task.FromResult(score is null ? new ScoreResult(0.5, "fake") : score(ctx));
    }
}
