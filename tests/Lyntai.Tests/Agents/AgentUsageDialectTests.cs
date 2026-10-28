using Lyntai.Agents;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Providers.CodexCli;

namespace Lyntai.Tests.Agents;

/// <summary>The two doors' readers of a usage answer, held to the recorded responses in <see cref="UsageCaptures"/>:
/// a response that matches reads as windows, and every response that does not reads unknown — never a partial
/// guess. The answer's shape is validated per version, so a changed shape must fail here, not in a host.</summary>
public class AgentUsageDialectTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);
    private static readonly ClaudeUsageDialect Claude = new("claude-cli");
    private static readonly CodexUsageDialect Codex = new("codex-cli");

    // ── claude ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Claude_2_1_296_signed_in_reads_both_windows_with_their_use_length_and_reset()
    {
        var reading = Claude.Read([UsageCaptures.ClaudeSignedIn_2_1_296], Now);

        Assert.True(reading.IsKnown);
        Assert.Equal(Now, reading.MeasuredAt);
        Assert.Equal("team", reading.Plan);
        Assert.Equal(
            [
                new AgentAllowanceWindow(AgentAllowanceWindow.FiveHour, 23, TimeSpan.FromHours(5), new DateTimeOffset(2026, 10, 12, 14, 0, 0, TimeSpan.Zero)),
                new AgentAllowanceWindow(AgentAllowanceWindow.Weekly, 79, TimeSpan.FromDays(7), new DateTimeOffset(2026, 10, 15, 9, 30, 0, TimeSpan.Zero)),
            ],
            reading.Windows);
        Assert.True(reading.HasRoom);
    }

    [Fact]
    public void Claude_a_full_week_has_no_room_and_a_window_not_yet_opened_states_no_reset()
    {
        var reading = Claude.Read([UsageCaptures.ClaudeWeekFull_2_1_296], Now);

        Assert.True(reading.IsKnown);
        Assert.Equal(new AgentAllowanceWindow(AgentAllowanceWindow.FiveHour, 0, TimeSpan.FromHours(5), null), reading.Windows[0]);
        Assert.Equal(100, reading.Windows[1].UsedPercent);
        Assert.False(reading.HasRoom);
    }

    [Fact]
    public void Claude_signed_out_real_capture_reads_unknown_not_reported()
    {
        var reading = Claude.Read(UsageCaptures.ClaudeSignedOut_2_1_312, Now);

        Assert.False(reading.IsKnown);
        Assert.Equal(AgentAllowanceUnknown.NotReported, reading.UnknownReason);
        Assert.Empty(reading.Windows);
        Assert.Null(reading.HasRoom);
    }

    [Fact]
    public void Claude_reads_the_answer_to_the_usage_request_only_and_past_other_lines()
    {
        var reading = Claude.Read(
        [
            "not json at all",
            """{"type":"system","subtype":"init"}""",
            """{"type":"control_response","response":{"subtype":"success","request_id":"init","response":{"rate_limits_available":false}}}""",
            UsageCaptures.ClaudeSignedIn_2_1_296,
        ], Now);

        Assert.True(reading.IsKnown);
        Assert.Equal(23, reading.Windows[0].UsedPercent);
    }

    [Theory]
    [InlineData("""{"type":"control_response","response":{"subtype":"error","request_id":"usage","error":"nope"}}""")]
    [InlineData("""{"type":"control_response","response":{"subtype":"success","request_id":"usage","response":{"rate_limits":{}}}}""")]
    [InlineData("""{"type":"control_response","response":{"subtype":"success","request_id":"usage","response":{"rate_limits_available":"yes"}}}""")]
    [InlineData("""{"type":"control_response","response":{"subtype":"success","request_id":"usage","response":{"rate_limits_available":true,"rate_limits":null}}}""")]
    [InlineData("""{"type":"control_response","response":{"subtype":"success","request_id":"usage","response":{"rate_limits_available":true,"rate_limits":{"five_hour":null,"seven_day":null}}}}""")]
    [InlineData("""{"type":"control_response","response":{"subtype":"success","request_id":"usage","response":{"rate_limits_available":true,"rate_limits":{"seven_day":{"utilization":"79"}}}}}""")]
    [InlineData("""{"type":"control_response","response":{"subtype":"success","request_id":"usage","response":{"rate_limits_available":true,"rate_limits":{"seven_day":{"utilization":101}}}}}""")]
    [InlineData("""{"type":"control_response","response":{"subtype":"success","request_id":"usage","response":{"rate_limits_available":true,"rate_limits":{"seven_day":{"utilization":79.5}}}}}""")]
    [InlineData("""{"type":"control_response","response":{"subtype":"success","request_id":"usage","response":{"rate_limits_available":true,"rate_limits":{"seven_day":{"utilization":79,"resets_at":"soon"}}}}}""")]
    [InlineData("""{"type":"control_response","response":{"subtype":"success","request_id":"usage","response":{"rate_limits_available":true,"rate_limits":{"seven_day":{}}}}}""")]
    [InlineData("""{"type":"control_response","response":{"subtype":"success","request_id":"usage","response":{"rate_limits_available":true,"rate_limits":{"five_hour":{"utilization":5},"seven_day":[]}}}}""")]
    [InlineData("""{"type":"control_response","response":"gone"}""")]
    [InlineData("""{"type":"control_response","response":{"request_id":"usage"}}""")]
    public void Claude_an_answer_that_does_not_match_reads_unknown_never_a_partial_guess(string line)
    {
        var reading = Claude.Read([line], Now);

        Assert.False(reading.IsKnown);
        Assert.Equal(AgentAllowanceUnknown.Unrecognised, reading.UnknownReason);
        Assert.Empty(reading.Windows);
    }

    [Fact]
    public void Claude_no_answer_line_reads_unknown()
    {
        Assert.Equal(AgentAllowanceUnknown.Unrecognised, Claude.Read([], Now).UnknownReason);
        Assert.Equal(AgentAllowanceUnknown.Unrecognised,
            Claude.Read([UsageCaptures.ClaudeSignedOut_2_1_312[0]], Now).UnknownReason);
    }

    [Fact]
    public void Claude_ends_the_exchange_on_the_usage_answer_and_not_on_the_initialize_one()
    {
        Assert.False(Claude.IsAnswer(UsageCaptures.ClaudeSignedOut_2_1_312[0]));
        Assert.True(Claude.IsAnswer(UsageCaptures.ClaudeSignedOut_2_1_312[1]));
        Assert.False(Claude.IsAnswer("{broken"));
        Assert.False(Claude.IsAnswer(""));
    }

    [Theory]
    [InlineData("2.1.296 (Claude Code)", "2.1.296")]
    [InlineData("2.1.312 (Claude Code)\n", "2.1.312")]
    [InlineData("  2.1.296", "2.1.296")]
    [InlineData("claude 2.1.296", null)]
    [InlineData("2.1 (Claude Code)", null)]
    [InlineData("", null)]
    public void Claude_reads_the_version_the_binary_prints(string output, string? expected) =>
        Assert.Equal(expected, Claude.ParseVersion(output));

    // ── codex ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Codex_0_160_0_signed_in_reads_both_windows_by_their_length_and_surfaces_no_account_id()
    {
        var reading = Codex.Read([UsageCaptures.CodexSignedIn_0_160_0], Now);

        Assert.True(reading.IsKnown);
        Assert.Equal("plus", reading.Plan);
        Assert.Equal(
            [
                new AgentAllowanceWindow(AgentAllowanceWindow.FiveHour, 1, TimeSpan.FromMinutes(300), DateTimeOffset.FromUnixTimeSeconds(1792000000)),
                new AgentAllowanceWindow(AgentAllowanceWindow.Weekly, 15, TimeSpan.FromMinutes(10080), DateTimeOffset.FromUnixTimeSeconds(1792400000)),
            ],
            reading.Windows);
        Assert.True(reading.HasRoom);
        Assert.DoesNotContain("redacted", reading.ToString());
    }

    [Fact]
    public void Codex_a_plan_with_only_a_full_week_reads_that_window_alone_and_has_no_room()
    {
        var reading = Codex.Read([UsageCaptures.CodexWeekOnly_0_160_0], Now);

        Assert.True(reading.IsKnown);
        var week = Assert.Single(reading.Windows);
        Assert.Equal(AgentAllowanceWindow.Weekly, week.Name);
        Assert.False(reading.HasRoom);
    }

    [Fact]
    public void Codex_a_window_of_another_length_is_named_by_that_length_and_not_guessed()
    {
        var reading = Codex.Read(
            ["""{"id":2,"result":{"rateLimits":{"primary":{"usedPercent":40,"windowDurationMins":60,"resetsAt":null}}}}"""], Now);

        var window = Assert.Single(reading.Windows);
        Assert.Equal("60-minutes", window.Name);
        Assert.Equal(TimeSpan.FromHours(1), window.Length);
        Assert.Null(window.ResetsAt);
    }

    [Fact]
    public void Codex_signed_out_real_capture_reads_unknown_not_signed_in()
    {
        var reading = Codex.Read(UsageCaptures.CodexSignedOut_0_163, Now);

        Assert.False(reading.IsKnown);
        Assert.Equal(AgentAllowanceUnknown.NotSignedIn, reading.UnknownReason);
    }

    [Theory]
    [InlineData("""{"id":2,"error":{"code":-32603,"message":"something else broke"}}""")]
    [InlineData("""{"id":2,"error":"flat"}""")]
    [InlineData("""{"id":2,"result":null}""")]
    [InlineData("""{"id":2,"result":{"rateLimits":null}}""")]
    [InlineData("""{"id":2,"result":{"rateLimits":{"primary":null,"secondary":null}}}""")]
    [InlineData("""{"id":2,"result":{"rateLimits":{"primary":{"usedPercent":"1","windowDurationMins":300}}}}""")]
    [InlineData("""{"id":2,"result":{"rateLimits":{"primary":{"usedPercent":101,"windowDurationMins":300}}}}""")]
    [InlineData("""{"id":2,"result":{"rateLimits":{"primary":{"usedPercent":1.5,"windowDurationMins":300}}}}""")]
    [InlineData("""{"id":2,"result":{"rateLimits":{"primary":{"usedPercent":1}}}}""")]
    [InlineData("""{"id":2,"result":{"rateLimits":{"primary":{"usedPercent":1,"windowDurationMins":0}}}}""")]
    [InlineData("""{"id":2,"result":{"rateLimits":{"primary":{"usedPercent":1,"windowDurationMins":300,"resetsAt":"tomorrow"}}}}""")]
    [InlineData("""{"id":2,"result":{"rateLimits":{"primary":{"usedPercent":1,"windowDurationMins":300,"resetsAt":99999999999999}}}}""")]
    [InlineData("""{"id":2,"result":{"rateLimits":{"secondary":[]}}}""")]
    public void Codex_an_answer_that_does_not_match_reads_unknown_never_a_partial_guess(string line)
    {
        var reading = Codex.Read([line], Now);

        Assert.False(reading.IsKnown);
        Assert.Equal(AgentAllowanceUnknown.Unrecognised, reading.UnknownReason);
        Assert.Empty(reading.Windows);
    }

    [Fact]
    public void Codex_ends_the_exchange_on_the_response_to_the_usage_request_only()
    {
        Assert.False(Codex.IsAnswer(UsageCaptures.CodexSignedOut_0_163[0])); // the initialize answer, id 1
        Assert.False(Codex.IsAnswer(UsageCaptures.CodexSignedOut_0_163[1])); // a notification
        Assert.True(Codex.IsAnswer(UsageCaptures.CodexSignedOut_0_163[2]));
        Assert.True(Codex.IsAnswer(UsageCaptures.CodexSignedIn_0_160_0));
        // a request the SERVER makes can reuse the id; it carries a method, so it is not the answer
        Assert.False(Codex.IsAnswer("""{"id":2,"method":"item/tool/call","params":{}}"""));
        Assert.False(Codex.IsAnswer("{broken"));
    }

    [Theory]
    [InlineData("codex-cli 0.160.0", "0.160.0")]
    [InlineData("codex-cli 0.163.0-alpha.10.2\n", "0.163.0-alpha.10.2")]
    [InlineData("0.160.0", "0.160.0")]
    [InlineData("codex-cli", null)]
    [InlineData("", null)]
    public void Codex_reads_the_version_the_binary_prints(string output, string? expected) =>
        Assert.Equal(expected, Codex.ParseVersion(output));

    // ── the validated set ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Only_versions_with_a_signed_in_response_on_record_are_validated()
    {
        // The installed binaries on the machines that wrote the signed-out captures are NOT in the set: a
        // signed-out answer is not a usage response, so it validates nothing (D222).
        Assert.Equal(["2.1.296"], ClaudeUsageDialect.Validated.ToArray());
        Assert.Equal(["0.160.0"], CodexUsageDialect.Validated.ToArray());
    }

    [Fact]
    public void The_window_set_and_the_use_survive_a_reading_with_unrelated_fields_changed()
    {
        // a reading is a record: equal windows are equal readings' windows, and HasRoom is derived, never stored
        var a = Claude.Read([UsageCaptures.ClaudeSignedIn_2_1_296], Now);
        var b = Claude.Read([UsageCaptures.ClaudeSignedIn_2_1_296], Now.AddHours(1));
        Assert.Equal(a.Windows, b.Windows);
        Assert.NotEqual(a.MeasuredAt, b.MeasuredAt);
    }

    [Fact]
    public void A_window_that_states_no_use_leaves_room_unknown()
    {
        var reading = new AgentAllowanceReading
        {
            MeasuredAt = Now,
            Windows = [new AgentAllowanceWindow("weekly", null, null, null)],
        };

        Assert.True(reading.IsKnown);
        Assert.Null(reading.HasRoom);
    }
}
