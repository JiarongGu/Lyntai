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
    [InlineData("Unauthorized: invalid api key", LlmVerdict.AuthFailed)]      // auth context nearby
    [InlineData("user is unauthorized to read file config.json", LlmVerdict.Failed)] // NOT a provider-auth failure
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

    // Parity with the generation domain (GenerationVerdictClassifier): an auth failure with nothing to
    // authenticate WITH is a configuration gap, not a rejected credential — and the difference is not
    // cosmetic, because AuthFailed BENCHES the host for the cooldown window while NotConfigured advances
    // blamelessly. A backend nobody has configured would otherwise be penalised on every first attempt.
    [Fact]
    public void A_401_with_no_credentials_supplied_is_NotConfigured_not_AuthFailed()
    {
        Assert.Equal(LlmVerdict.NotConfigured,
            LlmVerdictClassifier.FromHttpFailure(HttpStatusCode.Unauthorized, "missing api key", hasCredentials: false));
        Assert.Equal(LlmVerdict.NotConfigured,
            LlmVerdictClassifier.FromHttpFailure(HttpStatusCode.Forbidden, null, hasCredentials: false));
        // the promotion follows the VERDICT, not the status — a body-text auth failure promotes too
        Assert.Equal(LlmVerdict.NotConfigured,
            LlmVerdictClassifier.FromHttpFailure(HttpStatusCode.BadRequest, "Incorrect API key provided", hasCredentials: false));
    }

    [Fact]
    public void A_401_with_credentials_supplied_stays_AuthFailed_because_that_key_is_wrong()
    {
        Assert.Equal(LlmVerdict.AuthFailed,
            LlmVerdictClassifier.FromHttpFailure(HttpStatusCode.Unauthorized, "invalid api key", hasCredentials: true));
    }

    [Fact]
    public void The_no_credentials_promotion_applies_only_to_auth_failures()
    {
        // "no key" alone cannot mean unconfigured: an OpenAI-compatible endpoint run locally (LM Studio,
        // vLLM, Ollama) legitimately needs none. Only "no key AND the server demanded one" does.
        Assert.Equal(LlmVerdict.RateLimited,
            LlmVerdictClassifier.FromHttpFailure(HttpStatusCode.TooManyRequests, null, hasCredentials: false));
        Assert.Equal(LlmVerdict.Failed,
            LlmVerdictClassifier.FromHttpFailure(HttpStatusCode.InternalServerError, "boom", hasCredentials: false));
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

    // R8 — a typed provider exception often wraps the real "too long" detail in an INNER exception; scanning
    // only the outer ex.Message misses it → Failed instead of ContextWindowExceeded, defeating the
    // big-context fallback.
    [Fact]
    public void FromException_reads_the_inner_exception_chain_for_context_window()
    {
        var ex = new InvalidOperationException("The request failed.",
            new InvalidOperationException("This model's maximum context length is 8192 tokens; your request had 90000."));

        Assert.Equal(LlmVerdict.ContextWindowExceeded, LlmVerdictClassifier.FromException(ex));
    }

    // R8 — the built-in patterns are English-only; an app can add its own matcher (e.g. a non-English
    // provider) via a scoped seam, consulted BEFORE the built-ins.
    [Fact]
    public void Custom_matcher_extends_classification_and_is_scoped()
    {
        const string german = "Ratenlimit überschritten"; // "rate limit exceeded" — not matched by built-ins
        Assert.Equal(LlmVerdict.Failed, LlmVerdictClassifier.FromErrorText(german));

        using (LlmVerdictClassifier.AddErrorTextMatcher(t =>
            t.Contains("Ratenlimit", StringComparison.OrdinalIgnoreCase) ? LlmVerdict.RateLimited : null))
        {
            Assert.Equal(LlmVerdict.RateLimited, LlmVerdictClassifier.FromErrorText(german)); // custom wins
        }

        Assert.Equal(LlmVerdict.Failed, LlmVerdictClassifier.FromErrorText(german)); // unregistered on dispose
    }
}
