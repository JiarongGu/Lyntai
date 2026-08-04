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

    /// <summary>The enum-growth gate and the behavior table in one. It demands the DECISION, not merely a
    /// registration: a new verdict fails the key-set assertion until someone adds a row, and a row cannot be
    /// added without writing <c>true</c> or <c>false</c> — which IS the classification. A gate that checked
    /// only membership would be greened by appending a name, letting a new verdict sail through to the
    /// <c>false</c> default undecided. D38 already states that the enum and the routing policy must move
    /// together; this makes the call-site helpers the third thing that moves with them.</summary>
    [Fact]
    public void Every_verdict_states_whether_it_is_transient()
    {
        // "may re-sending the SAME request later succeed?" — one row per member, no default
        var expected = new Dictionary<LlmVerdict, bool>
        {
            [LlmVerdict.Ok] = false,                      // nothing to retry
            [LlmVerdict.Failed] = true,                   // availability fault — AND the classifier's catch-all
            [LlmVerdict.Timeout] = true,                  // the host may answer in time on another attempt
            [LlmVerdict.RateLimited] = true,              // recovers on its own once the window rolls
            [LlmVerdict.AuthFailed] = false,              // the same credentials never start working
            [LlmVerdict.NotConfigured] = false,           // nothing to call until setup happens
            [LlmVerdict.ContextWindowExceeded] = false,   // the prompt must shrink or the model must grow
            [LlmVerdict.Refused] = false,                 // content policy follows the prompt, not the moment
            [LlmVerdict.Unsupported] = false,             // a capability gap this path cannot close
        };

        Assert.Equal(Enum.GetValues<LlmVerdict>().OrderBy(v => v), expected.Keys.OrderBy(v => v));

        foreach (var (verdict, isTransient) in expected)
            Assert.Equal(isTransient, verdict.IsTransient());
    }

    /// <summary>The documented over-report, pinned so it stays a KNOWN cost rather than a surprise.
    /// <c>FromErrorText</c> falls back to <see cref="LlmVerdict.Failed"/> for anything it cannot recognize,
    /// so that bucket holds permanent errors as well as transient ones. Kept deliberately:
    /// <see cref="Lyntai.Llm.Routing.RoutingPolicy"/> only ever re-sends to the SAME candidate for
    /// <c>Failed</c>/<c>Timeout</c>, so a predicate that said otherwise would contradict the router's own
    /// retry rule.</summary>
    [Fact]
    public void IsTransient_over_reports_on_the_classifiers_catch_all_and_that_is_deliberate()
    {
        // an unrecognized PERMANENT error (a 400 whose body matches no pattern) lands in the catch-all…
        var unrecognized = LlmVerdictClassifier.FromHttpFailure(
            System.Net.HttpStatusCode.BadRequest, "invalid value for parameter 'top_p'");
        Assert.Equal(LlmVerdict.Failed, unrecognized);

        // …and therefore reads transient. Documented on IsTransient: a caller needing certainty reads the
        // specific verdict, and a caller that retries must BOUND it rather than loop.
        Assert.True(unrecognized.IsTransient());

        // the recognized buckets still classify correctly on either side of the line
        Assert.False(LlmVerdictClassifier
            .FromHttpFailure(System.Net.HttpStatusCode.Unauthorized, "invalid api key").IsTransient());
        Assert.True(LlmVerdictClassifier
            .FromHttpFailure(System.Net.HttpStatusCode.TooManyRequests, "slow down").IsTransient());
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
