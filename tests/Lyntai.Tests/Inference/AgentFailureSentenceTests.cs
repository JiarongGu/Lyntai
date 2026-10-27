using Lyntai.Agents;
using Lyntai.Inference;

namespace Lyntai.Tests.Inference;

/// <summary>A clock at one instant, in a fixed local zone, so a time a sentence writes without a date or a zone
/// reads the same on every machine.</summary>
internal sealed class FixedLocalClock(DateTimeOffset now, TimeSpan offset) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone("fixed", offset, "fixed", "fixed");

    /// <summary>Wednesday 2026-10-28 10:00 at UTC+11.</summary>
    public static FixedLocalClock Wednesday { get; } = new(new DateTimeOffset(2026, 10, 28, 10, 0, 0, TimeSpan.FromHours(11)), TimeSpan.FromHours(11));
}

// serialized with the other classifier tests: AddErrorTextMatcher mutates a process-wide list
[Collection("verdict-matchers")]
public class AgentFailureSentenceTests
{
    private static readonly TimeSpan Plus11 = TimeSpan.FromHours(11);

    private static AgentFailure Read(string text) =>
        ProviderVerdictClassifier.ReadAgentFailure(text, FixedLocalClock.Wednesday) ?? throw new Xunit.Sdk.XunitException($"nothing read: {text}");

    [Theory] // each sentence recorded or source-derived (docs/agent-provider-layer.md §2), read as its kind and detail
    [InlineData("Internal error: Your workspace is out of credits. Ask your workspace owner to refill in order to continue.",
        AgentFailureKind.Limit, RestrictionCause.Credit, RestrictionSubject.Account, RecoveryKind.OwnerAction)]
    [InlineData("You’ve hit your usage limit. Upgrade to Pro, or try again later.",
        AgentFailureKind.Limit, RestrictionCause.Quota, RestrictionSubject.Account, RecoveryKind.Unknown)]
    [InlineData("You’ve hit your usage limit for gpt-5-codex. Switch to another model now, or try again later.",
        AgentFailureKind.Limit, RestrictionCause.Quota, RestrictionSubject.Model, RecoveryKind.Unknown)]
    [InlineData("You've hit your team's shared budget · resets 3pm",
        AgentFailureKind.Limit, RestrictionCause.Quota, RestrictionSubject.Pool, RecoveryKind.ResetAt)]
    [InlineData("Rate limit exceeded, retry after 60s",
        AgentFailureKind.Limit, RestrictionCause.Rate, RestrictionSubject.Unknown, RecoveryKind.RetryNotBefore)]
    [InlineData("429 Too Many Requests",
        AgentFailureKind.Limit, RestrictionCause.Rate, RestrictionSubject.Unknown, RecoveryKind.Unknown)]
    [InlineData("Selected model is at capacity. Please try a different model.",
        AgentFailureKind.Transient, RestrictionCause.Capacity, RestrictionSubject.Model, RecoveryKind.Unknown)]
    [InlineData("API Error: 529 {\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"Overloaded\"}}",
        AgentFailureKind.Transient, RestrictionCause.Capacity, RestrictionSubject.Endpoint, RecoveryKind.Unknown)]
    [InlineData("Incorrect API key provided", AgentFailureKind.Access, null, null, null)]
    [InlineData("unexpected status 503 Service Unavailable", AgentFailureKind.Transient, null, null, null)]
    public void A_recorded_sentence_reads_as_its_kind(string text, AgentFailureKind kind, RestrictionCause? cause,
        RestrictionSubject? subject, RecoveryKind? recovery)
    {
        var failure = Read(text);

        Assert.Equal(kind, failure.Kind);
        Assert.StartsWith("sentence:", failure.By);
        if (cause is null)
        {
            Assert.Null(failure.Restriction);
            return;
        }
        Assert.Equal(cause, failure.Restriction!.Cause);
        Assert.Equal(subject, failure.Restriction.Subject);
        Assert.Equal(recovery, failure.Restriction.Recovery.Kind);
    }

    [Theory] // …and a NEAR sentence that must not: a number, a clock time or a word the tables share
    [InlineData("processed 529 records")]
    [InlineData("TypeError: x is undefined\n    at file:///app/cli.js:529:17")]
    [InlineData("the overloaded method is chosen at compile time")]
    [InlineData("Try again at 4:05 PM.")]                     // a time with no limit to wait out
    [InlineData("The nightly build resets at 3pm each day")]
    [InlineData("I hit your endpoint with a request limit of ten")]
    [InlineData("the model is available")]
    [InlineData("something exploded")]
    [InlineData("This model's maximum context length is 8192 tokens")]   // the window is not an allowance
    [InlineData("")]
    [InlineData(null)]
    public void A_near_sentence_reads_as_nothing(string? text)
    {
        Assert.Null(ProviderVerdictClassifier.ReadAgentFailure(text, FixedLocalClock.Wednesday));
    }

    [Fact] // a time Codex writes with no date: the same local day
    public void A_stated_reset_time_without_a_date_is_the_next_occurrence_in_the_local_zone()
    {
        var failure = Read("You’ve hit your usage limit. Upgrade to Pro (https://example.invalid/plans), or try again at 4:05 PM.");

        var recovery = failure.Restriction!.Recovery;
        Assert.Equal(RecoveryKind.ResetAt, recovery.Kind);
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 16, 5, 0, Plus11), recovery.At);
    }

    [Fact] // …a time that has already passed today is tomorrow's
    public void A_stated_reset_time_already_past_today_is_tomorrows()
    {
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 3, 0, 0, Plus11),
            Read("You've hit your limit · resets 3am").Restriction!.Recovery.At);
    }

    [Fact] // Codex's dated form, with an ordinal and a year
    public void A_stated_reset_with_a_date_is_that_date()
    {
        var failure = Read("You’ve hit your usage limit for gpt-5-codex. Switch to another model now, or try again at Oct 3rd, 2026 4:05 PM.");

        Assert.Equal(new DateTimeOffset(2026, 10, 3, 16, 5, 0, Plus11), failure.Restriction!.Recovery.At);
        Assert.Equal(RestrictionSubject.Model, failure.Restriction.Subject);
    }

    [Fact] // Claude Code's weekly form names a weekday
    public void A_stated_reset_on_a_weekday_is_that_weekdays_next_one()
    {
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 0, 0, 0, Plus11),
            Read("You've hit your weekly limit · resets Mon 12:00am").Restriction!.Recovery.At);
    }

    [Fact] // a named zone is the sentence's, not the machine's
    public void A_stated_reset_with_a_zone_is_read_in_that_zone()
    {
        // 10:00 +11 is 23:00 the day before in London (GMT), so 3:45pm there is the next afternoon
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 15, 45, 0, TimeSpan.Zero),
            Read("You've hit your limit · resets 3:45pm (Europe/London)").Restriction!.Recovery.At);
    }

    [Fact] // a zone nobody can resolve is a deadline nobody can state, so it is unknown rather than guessed
    public void A_stated_reset_in_an_unknown_zone_is_unknown()
    {
        Assert.Equal(RecoveryKind.Unknown,
            Read("You've hit your limit · resets 3:45pm (Not/AZone)").Restriction!.Recovery.Kind);
    }

    [Fact] // a retry-after is a duration from now, never a guessed deadline
    public void A_retry_after_is_not_before_now_plus_the_duration()
    {
        Assert.Equal(FixedLocalClock.Wednesday.GetUtcNow().AddSeconds(60),
            Read("Rate limit exceeded, retry after 60s").Restriction!.Recovery.At);
    }

    [Theory] // the kinds map onto the verdicts and add none
    [InlineData(AgentFailureKind.Limit, ProviderVerdict.RateLimited)]
    [InlineData(AgentFailureKind.Access, ProviderVerdict.AuthFailed)]
    [InlineData(AgentFailureKind.Transient, ProviderVerdict.Failed)]
    [InlineData(AgentFailureKind.Fatal, ProviderVerdict.Failed)]
    public void A_kind_maps_onto_an_existing_verdict(AgentFailureKind kind, ProviderVerdict expected)
    {
        Assert.Equal(expected, ProviderVerdictClassifier.FromAgentFailure(new AgentFailure(kind, "test")));
    }

    [Theory] // a fatal failure is the classifier's to place when its words are a refusal or an over-long input
    [InlineData("blocked by content_filter", ProviderVerdict.Refused)]
    [InlineData("prompt is too long: 210000 tokens", ProviderVerdict.ContextWindowExceeded)]
    [InlineData("something exploded", ProviderVerdict.Failed)]
    public void A_fatal_failure_is_whatever_the_classifier_says_of_its_words(string text, ProviderVerdict expected)
    {
        Assert.Equal(expected, ProviderVerdictClassifier.FromAgentFailure(new AgentFailure(AgentFailureKind.Fatal, "test"), text));
    }

    [Theory] // the one table: the verdict of a sentence and the verdict of its failure agree
    [InlineData("Your workspace is out of credits. Ask your workspace owner to refill in order to continue.")]
    [InlineData("You’ve hit your usage limit. Try again later.")]
    [InlineData("429 Too Many Requests")]
    [InlineData("Incorrect API key provided")]
    [InlineData("Selected model is at capacity. Please try a different model.")]
    [InlineData("API Error: 529 overloaded_error")]
    [InlineData("something exploded")]
    public void The_text_verdict_and_the_failure_verdict_agree(string text)
    {
        var failure = ProviderVerdictClassifier.ReadAgentFailure(text) ?? new AgentFailure(AgentFailureKind.Fatal, "none");

        Assert.Equal(ProviderVerdictClassifier.FromErrorText(text), ProviderVerdictClassifier.FromAgentFailure(failure, text));
    }

    [Fact] // a consumer's matcher is consulted first here too
    public void A_consumer_matcher_teaches_the_failure_reader_as_it_teaches_the_verdict()
    {
        const string german = "Ratenlimit überschritten";
        Assert.Null(ProviderVerdictClassifier.ReadAgentFailure(german));

        using (ProviderVerdictClassifier.AddErrorTextMatcher(t => t.Contains("Ratenlimit") ? ProviderVerdict.RateLimited : null))
            Assert.Equal(AgentFailureKind.Limit, ProviderVerdictClassifier.ReadAgentFailure(german)!.Kind);

        Assert.Null(ProviderVerdictClassifier.ReadAgentFailure(german));
    }
}
