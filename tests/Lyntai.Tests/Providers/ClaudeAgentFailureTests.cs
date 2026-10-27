using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Inference;

namespace Lyntai.Tests.Providers;

/// <summary>What the claude door says about a failed turn, read from its own fields before any sentence.
/// <b>The captures are SOURCE-DERIVED</b>, not recorded from a run: the CLI's limit and 529 results were read in
/// its shipped code and the SDK's declarations (<c>api_error_status</c>, the assistant message's <c>error</c>, a
/// <c>rejected</c> <c>rate_limit_event</c>), and no limit was met on a real turn. The 2.1.287 frames that
/// were recorded are all <c>allowed</c>.</summary>
public class ClaudeAgentFailureTests
{
    private const string Init = """{"type":"system","subtype":"init","session_id":"s-1","model":"m"}""";

    // 2026-10-28 16:00 at UTC+11 — the frame's own Unix seconds, so the reset is a field and not a sentence
    private const long ResetsAt = 1793163600;

    private static SessionEnded Last(params string[] lines)
    {
        var reader = new StreamJsonAgentReader(FixedLocalClock.Wednesday);
        return lines.SelectMany(reader.Read).OfType<SessionEnded>().Single();
    }

    private static string Result(string json) => $$"""{"type":"result","session_id":"s-1",{{json}}}""";

    [Fact] // a 529 under a `success` label with the error flag set: a failure, and transient
    public void A_success_result_with_an_error_flag_and_a_529_is_a_transient_failure()
    {
        var ended = Last(Init,
            """{"type":"assistant","error":"overloaded","message":{"model":"<synthetic>","content":[{"type":"text","text":"API Error: 529 Overloaded"}]}}""",
            Result("""
                "subtype":"success","is_error":true,"api_error_status":529,"result":"API Error: 529 {\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"Overloaded\"}}"
                """));

        Assert.True(ended.IsError);
        Assert.Equal("success", ended.Subtype);
        Assert.Equal(ProviderVerdict.Failed, ended.Verdict);
        var failure = ended.Failure!;
        Assert.Equal(AgentFailureKind.Transient, failure.Kind);
        Assert.Equal("claude.api_error_status=529", failure.By);
        Assert.Equal(RestrictionCause.Capacity, failure.Restriction!.Cause);
        Assert.Equal(RestrictionSubject.Endpoint, failure.Restriction.Subject);
    }

    [Fact] // the rejected frame states WHEN: a field, so no sentence is needed to know the reset
    public void A_rejected_rate_limit_frame_gives_the_limit_and_its_stated_reset()
    {
        var ended = Last(Init,
            $$$"""{"type":"rate_limit_event","rate_limit_info":{"status":"rejected","resetsAt":{{{ResetsAt}}},"rateLimitType":"five_hour","isUsingOverage":false}}""",
            Result("""
                "subtype":"success","is_error":true,"api_error_status":429,"result":"You've hit your limit · resets 4pm"
                """));

        Assert.Equal(ProviderVerdict.RateLimited, ended.Verdict);
        var failure = ended.Failure!;
        Assert.Equal(AgentFailureKind.Limit, failure.Kind);
        Assert.Equal("claude.rate_limit_event.rejected", failure.By);
        Assert.Equal(RestrictionCause.Quota, failure.Restriction!.Cause);
        Assert.Equal(RestrictionSubject.Account, failure.Restriction.Subject);
        Assert.Equal(RecoveryKind.ResetAt, failure.Restriction.Recovery.Kind);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(ResetsAt), failure.Restriction.Recovery.At);
    }

    [Fact] // a 429 with no frame: the status decides the kind and the sentence fills what it left open
    public void A_429_takes_its_cause_and_reset_from_the_sentence_that_agrees()
    {
        var ended = Last(Init, Result("""
            "subtype":"success","is_error":true,"api_error_status":429,"result":"You've hit your weekly limit · resets Mon 12:00am"
            """));

        var failure = ended.Failure!;
        Assert.Equal(AgentFailureKind.Limit, failure.Kind);
        Assert.Equal("claude.api_error_status=429", failure.By);
        Assert.Equal(RestrictionCause.Quota, failure.Restriction!.Cause);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.FromHours(11)), failure.Restriction.Recovery.At);
    }

    [Fact] // with no status and no category, the sentence is read, and says it was
    public void A_failed_result_with_only_a_sentence_is_read_by_the_sentence()
    {
        var failure = Last(Init, Result("""
            "subtype":"success","is_error":true,"result":"API Error: 529 overloaded_error"
            """)).Failure!;

        Assert.Equal(AgentFailureKind.Transient, failure.Kind);
        Assert.Equal("claude.result:sentence:overloaded", failure.By);
    }

    [Theory] // a near sentence reads as nothing, so the failure is fatal and the verdict stays Failed
    [InlineData("processed 529 records")]
    [InlineData("TypeError: x is undefined at cli.js:529:17")]
    public void A_near_sentence_in_a_failed_result_is_fatal(string text)
    {
        var ended = Last(Init, Result($$"""
            "subtype":"success","is_error":true,"result":"{{text}}"
            """));

        Assert.Equal(AgentFailureKind.Fatal, ended.Failure!.Kind);
        Assert.Equal(ProviderVerdict.Failed, ended.Verdict);
    }

    [Fact] // an error result that carries no words at all
    public void A_failed_result_with_no_words_and_no_fields_is_fatal()
    {
        var ended = Last(Init, Result("""
            "subtype":"error_during_execution","is_error":true
            """));

        Assert.Equal(AgentFailureKind.Fatal, ended.Failure!.Kind);
        Assert.Equal("claude.result", ended.Failure.By);
        Assert.Equal(ProviderVerdict.Failed, ended.Verdict);
    }

    [Fact] // limit before access before transient, across the door's fields
    public void The_most_severe_field_wins()
    {
        var ended = Last(Init,
            $$$"""{"type":"rate_limit_event","rate_limit_info":{"status":"rejected","resetsAt":{{{ResetsAt}}},"rateLimitType":"five_hour"}}""",
            Result("""
                "subtype":"success","is_error":true,"api_error_status":529,"result":"API Error: 529"
                """));

        Assert.Equal(AgentFailureKind.Limit, ended.Failure!.Kind);
    }

    [Theory] // the category on the failed assistant message, with no status
    [InlineData("rate_limit", AgentFailureKind.Limit)]
    [InlineData("billing_error", AgentFailureKind.Limit)]
    [InlineData("authentication_failed", AgentFailureKind.Access)]
    [InlineData("overloaded", AgentFailureKind.Transient)]
    [InlineData("invalid_request", AgentFailureKind.Fatal)]
    [InlineData("unknown", AgentFailureKind.Fatal)]
    public void An_assistant_error_category_decides_when_the_result_has_no_status(string category, AgentFailureKind expected)
    {
        var ended = Last(Init,
            $$$"""{"type":"assistant","error":"{{{category}}}","message":{"content":[{"type":"text","text":"x"}]}}""",
            Result("""
                "subtype":"success","is_error":true,"result":"x"
                """));

        Assert.Equal(expected, ended.Failure!.Kind);
    }

    [Fact] // an ordinary frame is `allowed`, even while spending overage: never a limit
    public void An_allowed_rate_limit_frame_is_not_a_limit()
    {
        var ended = Last(Init,
            $$$"""{"type":"rate_limit_event","rate_limit_info":{"status":"allowed","resetsAt":{{{ResetsAt}}},"rateLimitType":"five_hour","isUsingOverage":true}}""",
            Result("""
                "subtype":"success","is_error":true,"api_error_status":500,"result":"API Error: 500"
                """));

        Assert.Equal(AgentFailureKind.Transient, ended.Failure!.Kind);
    }

    [Fact] // the agent's own words and a tool's output are never evidence, on a good turn or a failed one
    public void Agent_prose_and_tool_output_never_classify()
    {
        var reader = new StreamJsonAgentReader(FixedLocalClock.Wednesday);
        var good = new[]
        {
            Init,
            """{"type":"assistant","message":{"content":[{"type":"text","text":"You've hit your limit. API Error: 529 Overloaded. 429 Too Many Requests. Incorrect API key provided."},{"type":"tool_use","id":"t1","name":"Bash","input":{}}]}}""",
            """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t1","is_error":true,"content":"HTTP 429 rate limit exceeded; out of credits"}]}}""",
            Result("""
                "subtype":"success","is_error":false,"result":"done"
                """),
        };
        var ended = good.SelectMany(reader.Read).OfType<SessionEnded>().Single();
        Assert.Equal(ProviderVerdict.Ok, ended.Verdict);
        Assert.Null(ended.Failure);

        // …and when the turn failed for a reason the door did not word, that prose does not name it
        var failed = new StreamJsonAgentReader(FixedLocalClock.Wednesday);
        var failure = good[..3].Append(Result("""
            "subtype":"error_during_execution","is_error":true
            """)).SelectMany(failed.Read).OfType<SessionEnded>().Single().Failure!;
        Assert.Equal(AgentFailureKind.Fatal, failure.Kind);
    }
}
