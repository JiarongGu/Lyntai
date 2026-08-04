using System.Net;
using Lyntai.Media;

namespace Lyntai.Tests.Media;

/// <summary>Media keeps its OWN verdict vocabulary, but NOT its own corpus of "what does this failure mean" —
/// that would be a second set of regexes to drift (the mistake <c>docs/DECISIONS.md</c> D27 exists to
/// prevent). The classifier maps transport/text failures through Core's shared classifier and translates the
/// answer.</summary>
public class MediaVerdictClassifierTests
{
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, MediaVerdict.RateLimited)]
    [InlineData(HttpStatusCode.Unauthorized, MediaVerdict.AuthFailed)]
    [InlineData(HttpStatusCode.Forbidden, MediaVerdict.AuthFailed)]
    [InlineData(HttpStatusCode.InternalServerError, MediaVerdict.Failed)]
    public void An_http_failure_maps_to_a_media_verdict(HttpStatusCode status, MediaVerdict expected)
    {
        Assert.Equal(expected, MediaVerdictClassifier.FromHttpFailure(status, body: null));
    }

    [Fact]
    public void A_content_policy_refusal_surfaces_as_refused()
    {
        // image backends refuse prompts; shopping a refused prompt around backends is not the platform's call
        var verdict = MediaVerdictClassifier.FromErrorText("Your request was rejected by our content policy");

        Assert.Equal(MediaVerdict.Refused, verdict);
    }

    [Fact]
    public void A_rate_limit_phrased_in_prose_is_recognized()
    {
        Assert.Equal(MediaVerdict.RateLimited, MediaVerdictClassifier.FromErrorText("quota exceeded, try later"));
    }

    [Fact]
    public void An_unrecognized_failure_is_plain_Failed()
    {
        Assert.Equal(MediaVerdict.Failed, MediaVerdictClassifier.FromErrorText("something odd happened"));
    }

    [Fact]
    public void A_context_window_verdict_has_no_media_meaning_and_becomes_Failed()
    {
        // the LLM taxonomy has verdicts media cannot have; they must not leak through as a media verdict
        Assert.Equal(MediaVerdict.Failed, MediaVerdictClassifier.FromErrorText("maximum context length exceeded"));
    }

    [Fact]
    public void A_cancellation_style_exception_is_a_timeout()
    {
        Assert.Equal(MediaVerdict.Timeout, MediaVerdictClassifier.FromException(new OperationCanceledException()));
    }
}
