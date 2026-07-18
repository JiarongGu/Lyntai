using Lyntai.Cortex;
using Lyntai.Cortex.Scorers;

namespace Lyntai.Tests.Cortex;

public class ScorersTests
{
    private static ScoreContext Ctx(string? output, Dictionary<string, string>? extra = null) =>
        new() { SessionId = "s", Input = "in", Output = output, Extra = extra };

    private sealed class DescribedScorer : IScorer
    {
        public string Id => "described";
        public string Name => "Described";
        public string Description => "measures the thing";
        public string Group => "g";
        public bool IsLlm => false;
        public Task<ScoreResult?> ScoreAsync(ScoreContext ctx, CancellationToken ct) => Task.FromResult<ScoreResult?>(null);
    }

    [Fact]
    public void Scorer_description_defaults_to_empty_and_can_be_overridden()
    {
        Assert.Equal("", ((IScorer)new StructureScorer()).Description); // default interface member
        Assert.Equal("measures the thing", ((IScorer)new DescribedScorer()).Description);
    }

    [Theory]
    [InlineData(null, 0.0)]
    [InlineData("", 0.0)]
    [InlineData("   ", 0.0)]
    [InlineData("a real answer", 1.0)]
    public async Task Outcome_scores_output_presence(string? output, double expected)
    {
        var result = await new OutcomeScorer().ScoreAsync(Ctx(output), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Score);
    }

    [Fact]
    public async Task Outcome_downgrades_when_an_error_was_recorded()
    {
        var result = await new OutcomeScorer().ScoreAsync(
            Ctx("partial output", new() { ["error"] = "tool crashed" }), CancellationToken.None);

        Assert.Equal(0.2, result!.Score);
        Assert.Contains("tool crashed", result.Reason);
    }

    [Fact]
    public async Task Structure_is_not_applicable_without_a_declared_format()
    {
        var result = await new StructureScorer().ScoreAsync(Ctx("anything"), CancellationToken.None);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("""{"ok":true}""", 1.0)]                             // valid json
    [InlineData("Here you go:\n```json\n{\"ok\":true}\n```", 0.7)]   // extractable from fences
    [InlineData("no json anywhere", 0.0)]
    [InlineData("", 0.0)]
    public async Task Structure_scores_declared_json(string output, double expected)
    {
        var result = await new StructureScorer().ScoreAsync(
            Ctx(output, new() { ["format"] = "json" }), CancellationToken.None);

        Assert.Equal(expected, result!.Score);
    }

    [Fact]
    public async Task Structure_scores_declared_nonempty()
    {
        var scorer = new StructureScorer();

        Assert.Equal(1.0, (await scorer.ScoreAsync(Ctx("text", new() { ["format"] = "nonempty" }), CancellationToken.None))!.Score);
        Assert.Equal(0.0, (await scorer.ScoreAsync(Ctx("  ", new() { ["format"] = "nonempty" }), CancellationToken.None))!.Score);
    }
}
