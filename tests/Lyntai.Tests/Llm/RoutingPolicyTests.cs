using Lyntai.Inference;
using Lyntai.Llm;
using Lyntai.Llm.Routing;

namespace Lyntai.Tests.Llm;

public class RoutingPolicyTests
{
    [Fact]
    public void Defaults_reproduce_design_6_exactly()
    {
        var p = new RoutingPolicy();

        Assert.Equal(FallbackAction.PenalizeAndAdvance, p.ActionFor(ProviderVerdict.Failed));
        Assert.Equal(FallbackAction.PenalizeAndAdvance, p.ActionFor(ProviderVerdict.Timeout));
        Assert.Equal(FallbackAction.CooldownAndAdvance, p.ActionFor(ProviderVerdict.RateLimited));
        Assert.Equal(FallbackAction.CooldownAndAdvance, p.ActionFor(ProviderVerdict.AuthFailed));
        Assert.Equal(FallbackAction.Advance, p.ActionFor(ProviderVerdict.ContextWindowExceeded));
        Assert.Equal(FallbackAction.Surface, p.ActionFor(ProviderVerdict.Refused));
        Assert.Equal(FallbackAction.Surface, p.ActionFor(ProviderVerdict.Unsupported)); // capability gap, distinct verdict
        // "not set up yet" is not a fault — advance WITHOUT blame (no penalty, no cooldown), or a backend
        // nobody configured gets benched on every first attempt
        Assert.Equal(FallbackAction.Advance, p.ActionFor(ProviderVerdict.NotConfigured));

        Assert.Equal(CooldownScope.Provider, p.CooldownScope);
        Assert.True(p.ExemptSoleCandidate);
        Assert.Equal(TimeSpan.Zero, p.RetryBackoff);
    }

    [Fact]
    public void No_retries_by_default()
    {
        var p = new RoutingPolicy();
        foreach (ProviderVerdict v in Enum.GetValues<ProviderVerdict>())
            Assert.Equal(0, p.RetriesFor(v));
    }

    [Fact]
    public void On_overrides_the_action()
    {
        // a consumer that wants a rate-limited primary to just surface (not fall back)
        var p = new RoutingPolicy().On(ProviderVerdict.RateLimited, FallbackAction.Surface);
        Assert.Equal(FallbackAction.Surface, p.ActionFor(ProviderVerdict.RateLimited));
    }

    [Fact]
    public void Retry_sets_the_count_clamped_at_zero()
    {
        var p = new RoutingPolicy().Retry(ProviderVerdict.Failed, 2).Retry(ProviderVerdict.Timeout, -5);
        Assert.Equal(2, p.RetriesFor(ProviderVerdict.Failed));
        Assert.Equal(0, p.RetriesFor(ProviderVerdict.Timeout));
    }

    [Fact]
    public void Unmapped_verdict_defaults_to_penalize_and_advance()
    {
        var p = new RoutingPolicy();
        // Ok is never asked (handled before the policy), but the fallback must be safe
        Assert.Equal(FallbackAction.PenalizeAndAdvance, p.ActionFor(ProviderVerdict.Ok));
    }

    [Theory]
    [InlineData(ProviderVerdict.Failed, 2, 1, true)]     // 1st retry, budget 2 → retry
    [InlineData(ProviderVerdict.Failed, 2, 2, true)]     // 2nd retry, budget 2 → retry
    [InlineData(ProviderVerdict.Failed, 2, 3, false)]    // 3rd would exceed budget → advance
    [InlineData(ProviderVerdict.Failed, 0, 1, false)]    // no budget → immediate advance
    [InlineData(ProviderVerdict.RateLimited, 5, 1, false)] // cooled verdicts never retry the same host
    [InlineData(ProviderVerdict.Refused, 5, 1, false)]     // surfaced verdicts never retry
    public void Should_retry_same_candidate_honors_action_and_budget(ProviderVerdict verdict, int budget, int attemptsSoFar, bool expected)
    {
        var p = new RoutingPolicy().Retry(verdict, budget);
        Assert.Equal(expected, p.ShouldRetrySameCandidate(verdict, attemptsSoFar));
    }
}
