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
                new AgentAllowanceWindow(AgentAllowanceWindow.FiveHour, 23, TimeSpan.FromHours(5), new DateTimeOffset(2026, 10, 12, 14, 0, 0, TimeSpan.Zero)) { UsedStep = 1 },
                new AgentAllowanceWindow(AgentAllowanceWindow.Weekly, 79, TimeSpan.FromDays(7), new DateTimeOffset(2026, 10, 15, 9, 30, 0, TimeSpan.Zero)) { UsedStep = 1 },
            ],
            reading.Windows);
        Assert.True(reading.HasRoom);
    }

    [Fact]
    public void Claude_a_full_week_has_no_room_and_a_window_not_yet_opened_states_no_reset()
    {
        var reading = Claude.Read([UsageCaptures.ClaudeWeekFull_2_1_296], Now);

        Assert.True(reading.IsKnown);
        Assert.Equal(new AgentAllowanceWindow(AgentAllowanceWindow.FiveHour, 0, TimeSpan.FromHours(5), null) { UsedStep = 1 }, reading.Windows[0]);
        Assert.Equal(100, reading.Windows[1].UsedPercent);
        Assert.False(reading.HasRoom);
    }

    // a stamp with no offset is read as UTC, not as the host's local time (the captures all carry +00:00)
    [Fact]
    public void Claude_a_reset_with_no_offset_is_read_as_utc()
    {
        const string line = """{"type":"control_response","response":{"subtype":"success","request_id":"usage","response":{"rate_limits_available":true,"rate_limits":{"seven_day":{"utilization":79,"resets_at":"2026-10-15T09:30:00"}}}}}""";

        var reading = Claude.Read([line], Now);

        Assert.True(reading.IsKnown);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 9, 30, 0, TimeSpan.Zero), reading.Windows[0].ResetsAt);
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
    [InlineData("""{"type":"control_response","response":{"subtype":"success","request_id":"usage","response":{"rate_limits_available":true,"rate_limits":{"seven_day":{"utilization":-0.5}}}}}""")]
    [InlineData("""{"type":"control_response","response":{"subtype":"success","request_id":"usage","response":{"rate_limits_available":true,"rate_limits":{"seven_day":{"utilization":100.1}}}}}""")]
    [InlineData("""{"type":"control_response","response":{"subtype":"success","request_id":"usage","response":{"rate_limits_available":true,"rate_limits":{"seven_day":{"utilization":79,"resets_at":"soon"}}}}}""")]
    [InlineData("""{"type":"control_response","response":{"subtype":"success","request_id":"usage","response":{"rate_limits_available":true,"rate_limits":{"five_hour":{"utilization":5},"seven_day_opus":{"utilization":"x"}}}}}""")]
    [InlineData("""{"type":"control_response","response":{"subtype":"success","request_id":"usage","response":{"rate_limits_available":true,"rate_limits":{"five_hour":{"utilization":5},"seven_day_opus":7}}}}""")]
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
                new AgentAllowanceWindow(AgentAllowanceWindow.FiveHour, 1, TimeSpan.FromMinutes(300), DateTimeOffset.FromUnixTimeSeconds(1792000000)) { UsedStep = 1 },
                new AgentAllowanceWindow(AgentAllowanceWindow.Weekly, 15, TimeSpan.FromMinutes(10080), DateTimeOffset.FromUnixTimeSeconds(1792400000)) { UsedStep = 1 },
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
    [InlineData("""{"id":2,"result":{"rateLimits":{"primary":{"usedPercent":-0.5,"windowDurationMins":300}}}}""")]
    [InlineData("""{"id":2,"result":{"rateLimits":{"primary":{"usedPercent":100.5,"windowDurationMins":300}}}}""")]
    [InlineData("""{"id":2,"result":{"rateLimits":{"primary":{"usedPercent":1}}}}""")]
    [InlineData("""{"id":2,"result":{"rateLimits":{"primary":{"resetsAt":1792000000}}}}""")]
    [InlineData("""{"id":2,"result":{"rateLimits":{"limitId":"codex","primary":{"usedPercent":1,"windowDurationMins":300},"secondary":null},"rateLimitsByLimitId":[]}}""")]
    [InlineData("""{"id":2,"result":{"rateLimits":{"limitId":"codex","primary":{"usedPercent":1,"windowDurationMins":300}},"rateLimitsByLimitId":{"other":7}}}""")]
    [InlineData("""{"id":2,"result":{"rateLimits":{"limitId":"codex","primary":{"usedPercent":1,"windowDurationMins":300}},"rateLimitsByLimitId":{"other":{"primary":{"usedPercent":"x","windowDurationMins":300}}}}}""")]
    [InlineData("""{"id":2,"result":{"rateLimits":{"limitId":"codex","primary":{"usedPercent":1,"windowDurationMins":300}},"rateLimitsByLimitId":{"other":{"primary":{"usedPercent":1}}}}}""")]
    [InlineData("""{"id":2,"result":{"rateLimits":{"limitId":"codex","primary":{"usedPercent":1,"windowDurationMins":300}},"rateLimitsByLimitId":{"other":{"secondary":[]}}}}""")]
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

    [Fact]
    public void Codex_states_the_exchange_as_two_steps_with_the_notification_beside_the_usage_request()
    {
        var steps = Codex.StepsFor("lyntai", "1");
        Assert.Equal(2, steps.Count);
        Assert.Single(steps[0].Writes);
        Assert.Contains("\"initialize\"", steps[0].Writes[0]);
        Assert.Equal(["""{"method":"initialized"}""", steps[1].Writes[1]], steps[1].Writes);
        Assert.Contains("account/rateLimits/read", steps[1].Writes[1]);
        Assert.True(steps[0].IsAnswerTo(UsageCaptures.CodexSignedOut_0_163[0]));
        Assert.False(steps[0].IsAnswerTo(UsageCaptures.CodexSignedOut_0_163[2]));
        Assert.True(steps[1].IsAnswerTo(UsageCaptures.CodexSignedOut_0_163[2]));
    }

    [Theory]
    [InlineData("""{"id":1,"error":{"code":-32600,"message":"codex account authentication required"}}""", AgentAllowanceUnknown.NotSignedIn)]
    [InlineData("""{"id":1,"error":{"code":-32603,"message":"something else broke"}}""", AgentAllowanceUnknown.Unrecognised)]
    [InlineData("""{"id":1,"error":"flat"}""", AgentAllowanceUnknown.Unrecognised)]
    [InlineData("""{"id":1,"result":{}}""", null)]
    [InlineData("{broken", null)]
    public void Codex_reads_an_error_answer_to_the_initialize_step(string line, string? expected) =>
        Assert.Equal(expected, Codex.FailureOf(line));

    [Fact]
    public void Codex_says_the_default_client_exactly_as_it_always_has()
    {
        Assert.Equal(
            """{"id":1,"method":"initialize","params":{"clientInfo":{"name":"lyntai","version":"1"}}}""",
            Codex.StepsFor("lyntai", "1")[0].Writes[0]);
    }

    [Theory]
    [InlineData("host-app", "2.0")]
    [InlineData("say \"hi\"", "back\\slash")]
    [InlineData("two\nlines", "cr\rlf")]
    [InlineData("灵台 {\"x\":1}", "tab\there")]
    public void Codex_writes_the_client_through_a_json_writer_and_never_splices_it(string name, string version)
    {
        var line = Codex.StepsFor(name, version)[0].Writes[0];

        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        using var request = System.Text.Json.JsonDocument.Parse(line);
        var client = request.RootElement.GetProperty("params").GetProperty("clientInfo");
        Assert.Equal(name, client.GetProperty("name").GetString());
        Assert.Equal(version, client.GetProperty("version").GetString());
        Assert.Equal("initialize", request.RootElement.GetProperty("method").GetString());
        Assert.Equal(1, request.RootElement.GetProperty("id").GetInt32());
    }

    [Fact]
    public void Claude_carries_no_client_so_its_requests_are_the_same_for_any_name()
    {
        var standing = Claude.StepsFor("lyntai", "1");
        var renamed = Claude.StepsFor("host-app", "9.9");

        Assert.Equal(standing.Select(s => s.Writes), renamed.Select(s => s.Writes));
        Assert.DoesNotContain("host-app", string.Concat(renamed.SelectMany(s => s.Writes)));
    }

    [Fact]
    public void Claude_states_the_exchange_as_two_steps_each_answered_by_its_own_request_id()
    {
        var steps = Claude.StepsFor("lyntai", "1");
        Assert.Equal(2, steps.Count);
        Assert.True(steps[0].IsAnswerTo(UsageCaptures.ClaudeSignedOut_2_1_312[0]));
        Assert.False(steps[0].IsAnswerTo(UsageCaptures.ClaudeSignedOut_2_1_312[1]));
        Assert.True(steps[1].IsAnswerTo(UsageCaptures.ClaudeSignedOut_2_1_312[1]));
    }

    [Theory]
    [InlineData("""{"type":"control_response","response":{"subtype":"error","request_id":"init","error":"no"}}""", AgentAllowanceUnknown.Unrecognised)]
    [InlineData("""{"type":"control_response","response":{"subtype":"success","request_id":"init","response":{}}}""", null)]
    [InlineData("{broken", null)]
    public void Claude_reads_an_error_answer_to_the_initialize_step(string line, string? expected) =>
        Assert.Equal(expected, Claude.FailureOf(line));

    [Theory]
    [InlineData("codex-cli 0.160.0", "0.160.0")]
    [InlineData("codex-cli 0.163.0-alpha.10.2\n", "0.163.0-alpha.10.2")]
    [InlineData("0.160.0", "0.160.0")]
    [InlineData("codex-cli", null)]
    [InlineData("", null)]
    public void Codex_reads_the_version_the_binary_prints(string output, string? expected) =>
        Assert.Equal(expected, Codex.ParseVersion(output));

    // ── what the server stated: precision, scope, absence (D237) ──────────────────────────────────

    private static string ClaudeLimits(string limits) =>
        """{"type":"control_response","response":{"subtype":"success","request_id":"usage","response":{"rate_limits_available":true,"rate_limits":""" + limits + "}}}";

    [Fact]
    public void Claude_a_fractional_use_is_kept_as_written_with_the_step_its_digits_show()
    {
        var reading = Claude.Read([UsageCaptures.ClaudeFractional_2_1_296], Now);

        Assert.True(reading.IsKnown);
        Assert.Equal(23.5, reading.Windows[0].UsedPercent);
        Assert.Equal(0.1, reading.Windows[0].UsedStep);
        Assert.Equal(79, reading.Windows[1].UsedPercent);
        Assert.Equal(1, reading.Windows[1].UsedStep);
        Assert.All(reading.Windows, w => Assert.Null(w.Scope));
        var row = Assert.Single(reading.Limits);
        Assert.Equal(12.5, row.UsedPercent);
        Assert.Equal(0.01, row.UsedStep);
        Assert.True(reading.HasRoom);
    }

    [Fact]
    public void Claude_a_use_written_in_exponent_form_has_a_value_and_no_step()
    {
        var reading = Claude.Read([ClaudeLimits("""{"five_hour":{"utilization":1.5e1}}""")], Now);

        var window = Assert.Single(reading.Windows);
        Assert.Equal(15, window.UsedPercent);
        Assert.Null(window.UsedStep);
    }

    [Fact]
    public void Claude_a_full_scoped_week_is_a_scoped_window_and_the_accounts_own_windows_still_have_room()
    {
        var reading = Claude.Read([UsageCaptures.ClaudeScopedWeekFull_2_1_296], Now);

        Assert.True(reading.IsKnown);
        Assert.Equal(
            [
                (AgentAllowanceWindow.FiveHour, (string?)null, 40.0),
                (AgentAllowanceWindow.Weekly, null, 60.0),
                (AgentAllowanceWindow.Weekly, "opus", 100.0),
            ],
            reading.Windows.Select(w => (w.Name, w.Scope, w.UsedPercent!.Value)).ToArray());
        Assert.Equal(TimeSpan.FromDays(7), reading.Windows[2].Length);
        Assert.True(reading.HasRoom);
    }

    [Fact]
    public void Claude_reads_the_limit_rows_by_kind_and_scope_and_never_their_severity()
    {
        var reading = Claude.Read([UsageCaptures.ClaudeScopedWeekFull_2_1_296], Now);

        Assert.Equal(
            [
                new AgentAllowanceLimit("weekly_all", null, 60, 1, new DateTimeOffset(2026, 10, 15, 9, 30, 0, TimeSpan.Zero)),
                new AgentAllowanceLimit("weekly_scoped", "opus", 100, 1, new DateTimeOffset(2026, 10, 15, 9, 30, 0, TimeSpan.Zero)),
            ],
            reading.Limits);
        Assert.DoesNotContain("critical", reading.ToString());
        Assert.Equal(2, Claude.Read([UsageCaptures.ClaudeSignedIn_2_1_296], Now).Limits.Count);
        Assert.Empty(Claude.Read([UsageCaptures.ClaudeWeekFull_2_1_296], Now).Limits);
    }

    [Fact]
    public void Claude_a_row_scoped_by_an_object_keeps_its_model_and_surface_and_the_windows_stay()
    {
        var reading = Claude.Read([UsageCaptures.ClaudeObjectScope_2_1_296], Now);
        var reset = new DateTimeOffset(2026, 10, 15, 9, 30, 0, TimeSpan.Zero);

        Assert.True(reading.IsKnown);
        Assert.Equal([AgentAllowanceWindow.FiveHour, AgentAllowanceWindow.Weekly], reading.Windows.Select(w => w.Name).ToArray());
        Assert.Equal(
            [
                new AgentAllowanceLimit("weekly_all", null, 60, 1, reset),
                new AgentAllowanceLimit("weekly_scoped", "claude-opus-example", 100, 1, reset) { ScopeName = "Opus Example" },
                new AgentAllowanceLimit("weekly_scoped", null, 12, 1, reset) { Surface = "example-surface" },
                new AgentAllowanceLimit("session", null, 40, 1, new DateTimeOffset(2026, 10, 12, 14, 0, 0, TimeSpan.Zero)),
            ],
            reading.Limits);
    }

    [Fact]
    public void Claude_a_model_with_a_null_id_is_kept_by_its_display_name_and_the_windows_stay()
    {
        var reading = Claude.Read(
            [ClaudeLimits("""{"five_hour":{"utilization":5},"seven_day":{"utilization":6},"limits":[{"kind":"weekly_scoped","percent":100,"resets_at":null,"scope":{"model":{"id":null,"display_name":"Opus Example"},"surface":null}}]}""")],
            Now);

        Assert.True(reading.IsKnown);
        Assert.Equal(2, reading.Windows.Count);
        Assert.Equal(
            [new AgentAllowanceLimit("weekly_scoped", null, 100, 1, null) { ScopeName = "Opus Example" }],
            reading.Limits);
    }

    [Theory]
    [InlineData("""{"model":3,"surface":null}""")]
    [InlineData("""{"model":{"id":null,"display_name":null},"surface":null}""")]
    [InlineData("""{"model":{},"surface":null}""")]
    [InlineData("""{"model":null,"surface":7}""")]
    [InlineData("""[]""")]
    public void Claude_a_scope_this_reader_cannot_read_skips_that_row_and_keeps_the_rest(string scope)
    {
        var reading = Claude.Read(
            [ClaudeLimits("""{"five_hour":{"utilization":5},"limits":[{"kind":"session","percent":9,"scope":""" + scope
                + """},{"kind":"weekly_all","percent":60,"scope":null}]}""")], Now);

        Assert.True(reading.IsKnown);
        Assert.Equal(AgentAllowanceWindow.FiveHour, Assert.Single(reading.Windows).Name);
        Assert.Equal([new AgentAllowanceLimit("weekly_all", null, 60, 1, null)], reading.Limits);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""["row"]""")]
    [InlineData("""[{"percent":5}]""")]
    [InlineData("""[{"kind":"session","percent":105}]""")]
    [InlineData("""[{"kind":"session","scope":3}]""")]
    [InlineData("""[{"kind":"session","resets_at":"soon"}]""")]
    public void Claude_a_limit_row_it_cannot_read_is_skipped_and_the_windows_are_still_read(string limits)
    {
        var reading = Claude.Read([ClaudeLimits("""{"five_hour":{"utilization":5},"limits":""" + limits + "}")], Now);

        Assert.True(reading.IsKnown);
        Assert.Equal(5, Assert.Single(reading.Windows).UsedPercent);
        Assert.Empty(reading.Limits);
        Assert.True(reading.HasRoom);
    }

    [Fact]
    public void Claude_a_window_with_a_use_and_no_reset_is_kept_and_so_is_one_with_a_reset_and_no_use()
    {
        var reading = Claude.Read(
            [ClaudeLimits("""{"five_hour":{"utilization":30},"seven_day":{"resets_at":"2026-10-15T09:30:00+00:00"}}""")], Now);

        Assert.Equal(2, reading.Windows.Count);
        Assert.Equal(new AgentAllowanceWindow(AgentAllowanceWindow.FiveHour, 30, TimeSpan.FromHours(5), null) { UsedStep = 1 }, reading.Windows[0]);
        Assert.Null(reading.Windows[1].UsedPercent);
        Assert.Null(reading.Windows[1].UsedStep);
        Assert.NotNull(reading.Windows[1].ResetsAt);
        Assert.Null(reading.HasRoom);
    }

    [Fact]
    public void Claude_a_window_stating_neither_a_use_nor_a_reset_says_nothing_and_is_left_out()
    {
        var reading = Claude.Read([ClaudeLimits("""{"five_hour":{"utilization":30},"seven_day":{},"seven_day_opus":{}}""")], Now);

        Assert.Equal([AgentAllowanceWindow.FiveHour], reading.Windows.Select(w => w.Name).ToArray());
        Assert.True(reading.HasRoom);
    }

    [Fact]
    public void Codex_a_fractional_use_is_kept_as_written_with_the_step_its_digits_show()
    {
        var reading = Codex.Read([UsageCaptures.CodexFractional_0_160_0], Now);

        Assert.True(reading.IsKnown);
        Assert.Equal("codex", reading.LimitId);
        Assert.Equal([(1.5, 0.1), (15.25, 0.01)], reading.Windows.Select(w => (w.UsedPercent!.Value, w.UsedStep!.Value)).ToArray());
        Assert.True(reading.HasRoom);
    }

    [Fact]
    public void Codex_a_second_limit_reads_as_scoped_windows_and_its_full_week_does_not_end_the_accounts_room()
    {
        var reading = Codex.Read([UsageCaptures.CodexSecondLimit_0_160_0], Now);

        Assert.True(reading.IsKnown);
        Assert.Equal("codex", reading.LimitId);
        Assert.Equal(
            [
                (AgentAllowanceWindow.FiveHour, (string?)null, 1.0),
                (AgentAllowanceWindow.Weekly, null, 15.0),
                (AgentAllowanceWindow.Weekly, "codex_other", 100.0),
            ],
            reading.Windows.Select(w => (w.Name, w.Scope, w.UsedPercent!.Value)).ToArray());
        Assert.True(reading.HasRoom);
        Assert.Empty(reading.Limits);
    }

    [Fact]
    public void Codex_without_an_own_limit_id_reads_no_other_limit_because_the_own_one_cannot_be_told_apart()
    {
        var reading = Codex.Read(
            ["""{"id":2,"result":{"rateLimits":{"primary":{"usedPercent":1,"windowDurationMins":300}},"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":1,"windowDurationMins":300}}}}}"""], Now);

        Assert.Single(reading.Windows);
        Assert.Null(reading.LimitId);
    }

    [Fact]
    public void Codex_a_window_with_a_use_and_no_reset_is_kept_and_so_is_one_with_a_reset_and_no_use()
    {
        var reading = Codex.Read(
            ["""{"id":2,"result":{"rateLimits":{"primary":{"usedPercent":30,"windowDurationMins":300},"secondary":{"windowDurationMins":10080,"resetsAt":1792400000}}}}"""], Now);

        Assert.Equal(2, reading.Windows.Count);
        Assert.Equal(30, reading.Windows[0].UsedPercent);
        Assert.Null(reading.Windows[0].ResetsAt);
        Assert.Null(reading.Windows[1].UsedPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792400000), reading.Windows[1].ResetsAt);
        Assert.Null(reading.HasRoom);
    }

    [Fact]
    public void Codex_a_window_stating_neither_a_use_nor_a_reset_says_nothing_and_is_left_out()
    {
        var reading = Codex.Read(
            ["""{"id":2,"result":{"rateLimits":{"primary":{"usedPercent":30,"windowDurationMins":300},"secondary":{"windowDurationMins":10080}}}}"""], Now);

        Assert.Equal([AgentAllowanceWindow.FiveHour], reading.Windows.Select(w => w.Name).ToArray());
    }

    [Fact]
    public void HasRoom_reads_the_accounts_own_windows_only()
    {
        AgentAllowanceReading Of(params AgentAllowanceWindow[] windows) => new() { MeasuredAt = Now, Windows = windows };
        var own = new AgentAllowanceWindow(AgentAllowanceWindow.Weekly, 20, null, null);
        var full = new AgentAllowanceWindow(AgentAllowanceWindow.Weekly, 100, null, null) { Scope = "opus" };

        Assert.True(Of(own, full).HasRoom);
        Assert.False(Of(own with { UsedPercent = 100 }, full).HasRoom);
        Assert.Null(Of(full).HasRoom); // no own window stated: unknown, never "room"
        Assert.Null(Of(own with { UsedPercent = null }, full).HasRoom);
    }

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
