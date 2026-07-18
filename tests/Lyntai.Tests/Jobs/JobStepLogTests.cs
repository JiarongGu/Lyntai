using Lyntai.Jobs;

namespace Lyntai.Tests.Jobs;

public class JobStepLogTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Append_then_parse_round_trips_in_order()
    {
        var json = JobStepLog.Append(null, "first", T0);
        json = JobStepLog.Append(json, "second", T0.AddSeconds(1));

        var steps = JobStepLog.Parse(json);
        Assert.Equal(["first", "second"], steps.Select(s => s.Message));
        Assert.Equal(T0, steps[0].At);
        Assert.Equal(T0.AddSeconds(1), steps[1].At);
    }

    [Fact]
    public void Parse_tolerates_null_blank_and_malformed()
    {
        Assert.Empty(JobStepLog.Parse(null));
        Assert.Empty(JobStepLog.Parse(""));
        Assert.Empty(JobStepLog.Parse("   "));
        Assert.Empty(JobStepLog.Parse("not json"));
        Assert.Empty(JobStepLog.Parse("{}"));       // object, not array
        Assert.Empty(JobStepLog.Parse("""[{"no":"msg"}]""")); // entries without a msg are skipped
    }

    [Fact]
    public void Append_caps_to_the_most_recent_entries()
    {
        string? json = null;
        for (var i = 0; i < 10; i++) json = JobStepLog.Append(json, $"step-{i}", T0.AddSeconds(i), cap: 3);

        var steps = JobStepLog.Parse(json);
        Assert.Equal(3, steps.Count);
        Assert.Equal(["step-7", "step-8", "step-9"], steps.Select(s => s.Message)); // oldest dropped
    }
}
