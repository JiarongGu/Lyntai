using Lyntai.Inference;

namespace Lyntai.Tests.Lifecycle;

/// <summary>The call-site verdict helpers. They are deliberately CATEGORY predicates rather than one
/// method per enum member: <see cref="ProviderVerdict"/> grows (<see cref="ProviderVerdict.NotConfigured"/> was
/// appended after the 1.0 freeze), and a per-member helper set would make every growth a public-surface
/// change while leaving the newest member the only one without a helper.</summary>
public class ProviderVerdictExtensionsTests
{
    [Fact]
    public void IsOk_is_true_for_Ok_alone()
    {
        foreach (var verdict in Enum.GetValues<ProviderVerdict>())
            Assert.Equal(verdict == ProviderVerdict.Ok, verdict.IsOk());
    }

    /// <summary>The enum-growth gate and the behavior table in one. It demands the DECISION, not merely a
    /// registration: a new verdict fails the key-set assertion until someone adds a row, and a row cannot be
    /// added without writing <c>true</c> or <c>false</c> — which IS the classification. A gate that checked
    /// only membership would be greened by appending a name, letting a new verdict sail through to the
    /// <c>false</c> default undecided. D31 already states that the enum and the routing policy must move
    /// together; this makes the call-site helpers the third thing that moves with them.</summary>
    [Fact]
    public void Every_verdict_states_whether_it_is_transient()
    {
        // "may re-sending the SAME request later succeed?" — one row per member, no default
        var expected = new Dictionary<ProviderVerdict, bool>
        {
            [ProviderVerdict.Ok] = false,                      // nothing to retry
            [ProviderVerdict.Failed] = true,                   // availability fault — AND the classifier's catch-all
            [ProviderVerdict.Timeout] = true,                  // the host may answer in time on another attempt
            [ProviderVerdict.RateLimited] = true,              // recovers on its own once the window rolls
            [ProviderVerdict.AuthFailed] = false,              // the same credentials never start working
            [ProviderVerdict.NotConfigured] = false,           // nothing to call until setup happens
            [ProviderVerdict.ContextWindowExceeded] = false,   // the prompt must shrink or the model must grow
            [ProviderVerdict.Refused] = false,                 // content policy follows the prompt, not the moment
            [ProviderVerdict.Unsupported] = false,             // a capability gap this path cannot close
        };

        Assert.Equal(Enum.GetValues<ProviderVerdict>().OrderBy(v => v), expected.Keys.OrderBy(v => v));

        foreach (var (verdict, isTransient) in expected)
            Assert.Equal(isTransient, verdict.IsTransient());
    }

    /// <summary>The documented over-report, pinned so it stays a KNOWN cost rather than a surprise.
    /// <c>FromErrorText</c> falls back to <see cref="ProviderVerdict.Failed"/> for anything it cannot recognize,
    /// so that bucket holds permanent errors as well as transient ones. Kept deliberately:
    /// <see cref="Lyntai.Inference.RoutingPolicy"/> only ever re-sends to the SAME candidate for
    /// <c>Failed</c>/<c>Timeout</c>, so a predicate that said otherwise would contradict the router's own
    /// retry rule.</summary>
    [Fact]
    public void IsTransient_over_reports_on_the_classifiers_catch_all_and_that_is_deliberate()
    {
        // an unrecognized PERMANENT error (a 400 whose body matches no pattern) lands in the catch-all…
        var unrecognized = ProviderVerdictClassifier.FromHttpFailure(
            System.Net.HttpStatusCode.BadRequest, "invalid value for parameter 'top_p'");
        Assert.Equal(ProviderVerdict.Failed, unrecognized);

        // …and therefore reads transient. Documented on IsTransient: a caller needing certainty reads the
        // specific verdict, and a caller that retries must BOUND it rather than loop.
        Assert.True(unrecognized.IsTransient());

        // the recognized buckets still classify correctly on either side of the line
        Assert.False(ProviderVerdictClassifier
            .FromHttpFailure(System.Net.HttpStatusCode.Unauthorized, "invalid api key").IsTransient());
        Assert.True(ProviderVerdictClassifier
            .FromHttpFailure(System.Net.HttpStatusCode.TooManyRequests, "slow down").IsTransient());
    }

    [Fact]
    public void The_helpers_read_the_same_off_every_verdict_carrier()
    {
        // the helpers hang off the ENUM, not off TextResponse, precisely so the five carriers share one definition
        var reply = new TextResponse("hi", ProviderVerdict.Ok);
        var chunk = TextChunk.Error(ProviderVerdict.RateLimited, "slow down");

        Assert.True(reply.Verdict.IsOk());
        Assert.False(chunk.Verdict.IsOk());
        Assert.True(chunk.Verdict.IsTransient());
    }
}
