using Lyntai.Lifecycle;
using System.Net;
using Lyntai.Generation;
using Lyntai.Generation.Routing;
using Lyntai.Llm;
using Lyntai.Llm.Routing;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>Media keeps its OWN verdict vocabulary, but NOT its own corpus of "what does this failure mean" —
/// that would be a second set of regexes to drift (the mistake <c>docs/DECISIONS.md</c> D21 exists to
/// prevent). The classifier maps transport/text failures through Core's shared classifier and translates the
/// answer.</summary>
// serialized with every other class that REGISTERS one: ProviderVerdictClassifier.AddErrorTextMatcher mutates a
// PROCESS-WIDE list, so two registrants running in parallel would see each other's matchers. It does NOT
// protect the rest of the suite — every other collection keeps running and every FromErrorText call in it
// reads the same list — so a matcher registered here must answer for its OWN probe text and nothing else.
[Collection("verdict-matchers")]
public class GenerationVerdictRoutingTests
{
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, ProviderVerdict.RateLimited)]
    [InlineData(HttpStatusCode.Unauthorized, ProviderVerdict.AuthFailed)]
    [InlineData(HttpStatusCode.Forbidden, ProviderVerdict.AuthFailed)]
    [InlineData(HttpStatusCode.InternalServerError, ProviderVerdict.Failed)]
    public void An_http_failure_maps_to_a_media_verdict(HttpStatusCode status, ProviderVerdict expected)
    {
        Assert.Equal(expected, ProviderVerdictClassifier.FromHttpFailure(status, body: null));
    }

    [Fact]
    public void A_content_policy_refusal_surfaces_as_refused()
    {
        // image backends refuse prompts; shopping a refused prompt around backends is not the platform's call
        var verdict = ProviderVerdictClassifier.FromErrorText("Your request was rejected by our content policy");

        Assert.Equal(ProviderVerdict.Refused, verdict);
    }

    [Fact]
    public void A_rate_limit_phrased_in_prose_is_recognized()
    {
        Assert.Equal(ProviderVerdict.RateLimited, ProviderVerdictClassifier.FromErrorText("quota exceeded, try later"));
    }

    [Fact]
    public void An_unrecognized_failure_is_plain_Failed()
    {
        Assert.Equal(ProviderVerdict.Failed, ProviderVerdictClassifier.FromErrorText("something odd happened"));
    }

    [Fact]
    public void A_context_window_failure_ADVANCES_here_rather_than_benching_a_healthy_backend()
    {
        // It collapsed to Failed for a whole release, and as Failed it took PenalizeAndAdvance — so repeated
        // oversized prompts benched a perfectly healthy backend (TASKS Part 40, D36). It used to reach the
        // media domain through a translation table; D136 deleted the table, and this now asserts the thing
        // that always mattered: whatever the member is called, the generation policy must not PENALISE it.
        var verdict = ProviderVerdictClassifier.FromErrorText("maximum context length exceeded");

        Assert.Equal(ProviderVerdict.ContextWindowExceeded, verdict);
        Assert.Equal(GenerationFallbackAction.Advance, new GenerationRoutingPolicy().ActionFor(verdict));
    }

    [Fact]
    public void A_NotConfigured_verdict_from_the_shared_corpus_stays_NotConfigured()
    {
        // both domains now have this verdict and it means the same thing in each. Flattening it to Failed
        // would convert a blameless skip into a penalised failure on the way across the boundary.
        using var _ = ProviderVerdictClassifier.AddErrorTextMatcher(t =>
            t.Contains("no endpoint configured", StringComparison.OrdinalIgnoreCase)
                ? ProviderVerdict.NotConfigured
                : null);

        Assert.Equal(ProviderVerdict.NotConfigured,
            ProviderVerdictClassifier.FromErrorText("no endpoint configured"));
    }

    [Fact]
    public void A_cancellation_style_exception_is_a_timeout()
    {
        Assert.Equal(ProviderVerdict.Timeout, ProviderVerdictClassifier.FromException(new OperationCanceledException()));
    }

    [Fact]
    public void An_Unsupported_verdict_from_the_shared_corpus_stays_Unsupported()
    {
        // a capability gap is not a fault. Flattening it to Failed on the way across the boundary turns the
        // policy's Advance into PenalizeAndAdvance, so repeated gaps bench a perfectly healthy backend.
        using var _ = ProviderVerdictClassifier.AddErrorTextMatcher(t =>
            t.Contains("this model cannot take an image input", StringComparison.OrdinalIgnoreCase)
                ? ProviderVerdict.Unsupported
                : null);

        var verdict = ProviderVerdictClassifier.FromErrorText("this model cannot take an image input");

        Assert.Equal(ProviderVerdict.Unsupported, verdict);
        // the mapping only matters because of what routing does with it
        Assert.Equal(GenerationFallbackAction.Advance, new GenerationRoutingPolicy().ActionFor(verdict));
    }

    /// <summary>The mapping's POINT, end to end: a translated capability gap must not count toward the
    /// dead-host threshold. Asserted through the router rather than the policy table alone, because a
    /// translation that did not change the routing outcome would not have been worth changing.</summary>
    [Fact]
    public async Task A_capability_gap_does_not_bench_a_healthy_backend()
    {
        using var _ = ProviderVerdictClassifier.AddErrorTextMatcher(t =>
            t.Contains("cannot take an image input", StringComparison.OrdinalIgnoreCase)
                ? ProviderVerdict.Unsupported
                : null);

        // what the backend would report: its own error text, classified through the one corpus
        var verdict = ProviderVerdictClassifier.FromErrorText("cannot take an image input");

        // one penalised failure is enough to bench, so the second run is the whole assertion
        var deadHosts = new DeadHostTracker(threshold: 1);
        var gap = new FakeGenerationProvider { Id = "a" };
        gap.Verdicts.Enqueue(verdict);
        var working = new FakeGenerationProvider { Id = "b" };
        var router = new GenerationRouter([gap, working], deadHosts: deadHosts);
        ProviderCandidate[] candidates = [new("a"), new("b")];
        var request = new GenerationRequest { Kind = GenerationKinds.Image, Prompt = "a red square" };

        var first = await router.GenerateAsync(candidates, request);
        var second = await router.GenerateAsync(candidates, request);

        Assert.True(first.IsOk);
        Assert.True(second.IsOk);
        Assert.Equal(2, gap.GenerateCalls);   // still in rotation: a capability gap is not evidence of ill health
    }
}
