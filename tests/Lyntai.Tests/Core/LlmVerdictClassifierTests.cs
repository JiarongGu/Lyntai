using System.Net;
using Lyntai.Llm;

namespace Lyntai.Tests.Core;

public class LlmVerdictClassifierTests
{
    [Theory]
    [InlineData("Rate limit exceeded, retry after 60s", LlmVerdict.RateLimited)]
    [InlineData("429 Too Many Requests", LlmVerdict.RateLimited)]
    [InlineData("HTTP 429", LlmVerdict.RateLimited)]
    [InlineData("status code: 429", LlmVerdict.RateLimited)]
    [InlineData("quota exceeded for this project", LlmVerdict.RateLimited)]
    [InlineData("RESOURCE_EXHAUSTED", LlmVerdict.RateLimited)]
    [InlineData("blocked by content_filter", LlmVerdict.Refused)]
    [InlineData("violates our content policy", LlmVerdict.Refused)]
    [InlineData("This model's maximum context length is 8192 tokens", LlmVerdict.ContextWindowExceeded)]
    [InlineData("error code: context_length_exceeded", LlmVerdict.ContextWindowExceeded)]
    [InlineData("prompt is too long: 210000 tokens", LlmVerdict.ContextWindowExceeded)]
    [InlineData("Incorrect API key provided", LlmVerdict.AuthFailed)]
    [InlineData("401 Unauthorized", LlmVerdict.AuthFailed)]
    [InlineData("authentication failed for this endpoint", LlmVerdict.AuthFailed)]
    [InlineData("something exploded", LlmVerdict.Failed)]
    [InlineData("", LlmVerdict.Failed)]
    [InlineData(null, LlmVerdict.Failed)]
    public void Error_text_classifies_conservatively(string? text, LlmVerdict expected)
    {
        Assert.Equal(expected, LlmVerdictClassifier.FromErrorText(text));
    }

    [Fact]
    public void A_429_in_a_stack_frame_is_not_a_rate_limit()
    {
        // the drifting-heuristics bug: a CLI crash printing a line number must stay Failed,
        // or the router benches a healthy provider on a phantom 429
        Assert.Equal(LlmVerdict.Failed,
            LlmVerdictClassifier.FromErrorText("TypeError: x is undefined\n    at file:///app/cli.js:429:17"));
        Assert.Equal(LlmVerdict.Failed, LlmVerdictClassifier.FromErrorText("processed 429 records"));
    }

    [Fact]
    public void Typed_http_429_wins_even_with_no_429_text()
    {
        // "Too Many Requests" surfaced through HttpRequestException carries the STATUS, not the text
        var ex = new HttpRequestException("boom", null, HttpStatusCode.TooManyRequests);

        Assert.Equal(LlmVerdict.RateLimited, LlmVerdictClassifier.FromException(ex));
    }

    [Fact]
    public void Typed_401_and_403_map_to_auth_failed()
    {
        Assert.Equal(LlmVerdict.AuthFailed, LlmVerdictClassifier.FromException(
            new HttpRequestException("nope", null, HttpStatusCode.Unauthorized)));
        Assert.Equal(LlmVerdict.AuthFailed, LlmVerdictClassifier.FromHttpFailure(HttpStatusCode.Forbidden, "denied"));
    }

    [Fact]
    public void Untyped_exceptions_classify_from_their_message()
    {
        Assert.Equal(LlmVerdict.RateLimited,
            LlmVerdictClassifier.FromException(new InvalidOperationException("rate_limit_error from upstream")));
        Assert.Equal(LlmVerdict.Failed,
            LlmVerdictClassifier.FromException(new InvalidOperationException("connection refused by proxy")));
        Assert.Equal(LlmVerdict.Timeout,
            LlmVerdictClassifier.FromException(new OperationCanceledException()));
    }
}
