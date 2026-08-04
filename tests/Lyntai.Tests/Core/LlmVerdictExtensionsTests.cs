using Lyntai.Llm;

namespace Lyntai.Tests.Core;

/// <summary>The call-site verdict helpers. They are deliberately CATEGORY predicates rather than one
/// method per enum member: <see cref="LlmVerdict"/> grows (<see cref="LlmVerdict.NotConfigured"/> was
/// appended after the 1.0 freeze), and a per-member helper set would make every growth a public-surface
/// change while leaving the newest member the only one without a helper.</summary>
public class LlmVerdictExtensionsTests
{
    [Fact]
    public void IsOk_is_true_for_Ok_alone()
    {
        foreach (var verdict in Enum.GetValues<LlmVerdict>())
            Assert.Equal(verdict == LlmVerdict.Ok, verdict.IsOk());
    }

    [Fact]
    public void IsTransient_is_true_only_where_the_same_request_may_later_succeed()
    {
        LlmVerdict[] transient = [LlmVerdict.Failed, LlmVerdict.Timeout, LlmVerdict.RateLimited];

        foreach (var verdict in Enum.GetValues<LlmVerdict>())
            Assert.Equal(transient.Contains(verdict), verdict.IsTransient());
    }

    [Fact]
    public void IsTransient_says_no_to_the_verdicts_a_retry_cannot_fix()
    {
        // each of these is terminal for the request AS SENT: the credentials are wrong, the backend was
        // never set up, the prompt is too big, the content was refused, the path cannot carry the request
        Assert.False(LlmVerdict.AuthFailed.IsTransient());
        Assert.False(LlmVerdict.NotConfigured.IsTransient());
        Assert.False(LlmVerdict.ContextWindowExceeded.IsTransient());
        Assert.False(LlmVerdict.Refused.IsTransient());
        Assert.False(LlmVerdict.Unsupported.IsTransient());
    }

    /// <summary>The enum-growth forcing function. A verdict added without a deliberate classification here
    /// would silently answer <c>false</c> to <see cref="LlmVerdictExtensions.IsTransient"/> — safe, but
    /// undecided. D38 already states that the enum and the routing policy must move together; this makes
    /// the call-site helpers the third thing that moves with them, and fails the build's test gate if not.</summary>
    [Fact]
    public void Every_verdict_is_deliberately_classified()
    {
        LlmVerdict[] classified =
        [
            LlmVerdict.Ok, LlmVerdict.RateLimited, LlmVerdict.Refused, LlmVerdict.Failed, LlmVerdict.Timeout,
            LlmVerdict.ContextWindowExceeded, LlmVerdict.AuthFailed, LlmVerdict.Unsupported, LlmVerdict.NotConfigured,
        ];

        Assert.Equal(classified.OrderBy(v => v), Enum.GetValues<LlmVerdict>().OrderBy(v => v));
    }

    [Fact]
    public void The_helpers_read_the_same_off_every_verdict_carrier()
    {
        // the helpers hang off the ENUM, not off LlmReply, precisely so the five carriers share one definition
        var reply = new LlmReply("hi", LlmVerdict.Ok);
        var chunk = LlmChunk.Error(LlmVerdict.RateLimited, "slow down");

        Assert.True(reply.Verdict.IsOk());
        Assert.False(chunk.Verdict.IsOk());
        Assert.True(chunk.Verdict.IsTransient());
    }
}
