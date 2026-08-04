using System.Net;
using Lyntai.Generation;
using Lyntai.Llm;

namespace Lyntai.Tests.Generation;

/// <summary>Media keeps its OWN verdict vocabulary, but NOT its own corpus of "what does this failure mean" —
/// that would be a second set of regexes to drift (the mistake <c>docs/DECISIONS.md</c> D27 exists to
/// prevent). The classifier maps transport/text failures through Core's shared classifier and translates the
/// answer.</summary>
public class GenerationVerdictClassifierTests
{
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, GenerationVerdict.RateLimited)]
    [InlineData(HttpStatusCode.Unauthorized, GenerationVerdict.AuthFailed)]
    [InlineData(HttpStatusCode.Forbidden, GenerationVerdict.AuthFailed)]
    [InlineData(HttpStatusCode.InternalServerError, GenerationVerdict.Failed)]
    public void An_http_failure_maps_to_a_media_verdict(HttpStatusCode status, GenerationVerdict expected)
    {
        Assert.Equal(expected, GenerationVerdictClassifier.FromHttpFailure(status, body: null));
    }

    [Fact]
    public void A_content_policy_refusal_surfaces_as_refused()
    {
        // image backends refuse prompts; shopping a refused prompt around backends is not the platform's call
        var verdict = GenerationVerdictClassifier.FromErrorText("Your request was rejected by our content policy");

        Assert.Equal(GenerationVerdict.Refused, verdict);
    }

    [Fact]
    public void A_rate_limit_phrased_in_prose_is_recognized()
    {
        Assert.Equal(GenerationVerdict.RateLimited, GenerationVerdictClassifier.FromErrorText("quota exceeded, try later"));
    }

    [Fact]
    public void An_unrecognized_failure_is_plain_Failed()
    {
        Assert.Equal(GenerationVerdict.Failed, GenerationVerdictClassifier.FromErrorText("something odd happened"));
    }

    [Fact]
    public void A_context_window_verdict_has_no_media_meaning_and_becomes_Failed()
    {
        // the LLM taxonomy has verdicts media cannot have; they must not leak through as a media verdict
        Assert.Equal(GenerationVerdict.Failed, GenerationVerdictClassifier.FromErrorText("maximum context length exceeded"));
    }

    [Fact]
    public void A_NotConfigured_verdict_from_the_shared_corpus_stays_NotConfigured()
    {
        // both domains now have this verdict and it means the same thing in each. Flattening it to Failed
        // would convert a blameless skip into a penalised failure on the way across the boundary.
        using var _ = LlmVerdictClassifier.AddErrorTextMatcher(t =>
            t.Contains("no endpoint configured", StringComparison.OrdinalIgnoreCase)
                ? LlmVerdict.NotConfigured
                : null);

        Assert.Equal(GenerationVerdict.NotConfigured,
            GenerationVerdictClassifier.FromErrorText("no endpoint configured"));
    }

    [Fact]
    public void A_cancellation_style_exception_is_a_timeout()
    {
        Assert.Equal(GenerationVerdict.Timeout, GenerationVerdictClassifier.FromException(new OperationCanceledException()));
    }
}
