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

    private static readonly JobMessage Coded = new("Copied 3 of 10 files")
    {
        Code = "copy.progress",
        Arguments = new Dictionary<string, string> { ["done"] = "3", ["total"] = "10" },
    };

    [Fact]
    public void A_coded_step_round_trips_its_code_and_every_argument()
    {
        var step = Assert.Single(JobStepLog.Parse(JobStepLog.Append(null, Coded, T0)));

        Assert.Equal("Copied 3 of 10 files", step.Message);
        Assert.Equal("copy.progress", step.Code);
        Assert.Equal(Coded.Arguments, step.Arguments);
    }

    [Fact]
    public void A_step_without_a_code_writes_exactly_the_old_shape()
    {
        Assert.Equal(JobStepLog.Append(null, "plain", T0), JobStepLog.Append(null, new JobMessage("plain"), T0));
        Assert.DoesNotContain("code", JobStepLog.Append(null, new JobMessage("plain"), T0), StringComparison.Ordinal);
    }

    [Fact]
    public void An_old_log_parses_with_no_code()
    {
        var step = Assert.Single(JobStepLog.Parse("""[{"at":"2026-07-18T12:00:00+00:00","msg":"old"}]"""));

        Assert.Equal("old", step.Message);
        Assert.Null(step.Code);
        Assert.Null(step.Arguments);
    }

    [Fact]
    public void Argument_values_with_quotes_newlines_and_CJK_round_trip_exactly()
    {
        var message = new JobMessage("x") { Code = "c", Arguments = new Dictionary<string, string> { ["v"] = "a \"quoted\"\nline 复制" } };

        Assert.Equal("a \"quoted\"\nline 复制", Assert.Single(JobStepLog.Parse(JobStepLog.Append(null, message, T0))).Arguments!["v"]);
    }

    [Fact]
    public void The_cap_trims_oldest_first_across_coded_and_plain_steps()
    {
        var json = JobStepLog.Append(null, Coded, T0);
        json = JobStepLog.Append(json, "plain", T0.AddSeconds(1));
        json = JobStepLog.Append(json, Coded, T0.AddSeconds(2), cap: 2);

        var steps = JobStepLog.Parse(json);
        Assert.Equal([null, "copy.progress"], steps.Select(s => s.Code));
    }

    [Fact]
    public void A_non_string_argument_is_skipped_rather_than_thrown()
    {
        var step = Assert.Single(JobStepLog.Parse("""[{"at":"2026-07-18T12:00:00+00:00","msg":"m","code":"c","args":{"n":3,"s":"ok"}}]"""));

        Assert.Equal(new Dictionary<string, string> { ["s"] = "ok" }, step.Arguments);
    }

    [Fact]
    public void A_message_needs_text() => Assert.Throws<ArgumentNullException>(() => new JobMessage(null!));
}
