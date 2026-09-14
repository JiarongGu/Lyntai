using Lyntai.Lifecycle;
using System.Net;
using Lyntai.Llm;

namespace Lyntai.Tests.Lifecycle;

// serialized with every other class that registers one: AddErrorTextMatcher mutates a PROCESS-WIDE list, so
// two of these running in parallel would see each other's matchers (today they are only disjoint by luck)
[Collection("verdict-matchers")]
public class ProviderVerdictClassifierTests
{
    [Theory]
    [InlineData("Rate limit exceeded, retry after 60s", ProviderVerdict.RateLimited)]
    [InlineData("429 Too Many Requests", ProviderVerdict.RateLimited)]
    [InlineData("HTTP 429", ProviderVerdict.RateLimited)]
    [InlineData("status code: 429", ProviderVerdict.RateLimited)]
    [InlineData("quota exceeded for this project", ProviderVerdict.RateLimited)]
    [InlineData("RESOURCE_EXHAUSTED", ProviderVerdict.RateLimited)]
    [InlineData("blocked by content_filter", ProviderVerdict.Refused)]
    [InlineData("violates our content policy", ProviderVerdict.Refused)]
    [InlineData("This model's maximum context length is 8192 tokens", ProviderVerdict.ContextWindowExceeded)]
    [InlineData("error code: context_length_exceeded", ProviderVerdict.ContextWindowExceeded)]
    [InlineData("prompt is too long: 210000 tokens", ProviderVerdict.ContextWindowExceeded)]
    [InlineData("Incorrect API key provided", ProviderVerdict.AuthFailed)]
    [InlineData("401 Unauthorized", ProviderVerdict.AuthFailed)]
    [InlineData("authentication failed for this endpoint", ProviderVerdict.AuthFailed)]
    [InlineData("Unauthorized: invalid api key", ProviderVerdict.AuthFailed)]      // auth context nearby
    [InlineData("user is unauthorized to read file config.json", ProviderVerdict.Failed)] // NOT a provider-auth failure
    [InlineData("something exploded", ProviderVerdict.Failed)]
    [InlineData("", ProviderVerdict.Failed)]
    [InlineData(null, ProviderVerdict.Failed)]
    public void Error_text_classifies_conservatively(string? text, ProviderVerdict expected)
    {
        Assert.Equal(expected, ProviderVerdictClassifier.FromErrorText(text));
    }

    [Fact]
    public void A_429_in_a_stack_frame_is_not_a_rate_limit()
    {
        // the drifting-heuristics bug: a CLI crash printing a line number must stay Failed,
        // or the router benches a healthy provider on a phantom 429
        Assert.Equal(ProviderVerdict.Failed,
            ProviderVerdictClassifier.FromErrorText("TypeError: x is undefined\n    at file:///app/cli.js:429:17"));
        Assert.Equal(ProviderVerdict.Failed, ProviderVerdictClassifier.FromErrorText("processed 429 records"));
    }

    [Fact]
    public void Typed_http_429_wins_even_with_no_429_text()
    {
        // "Too Many Requests" surfaced through HttpRequestException carries the STATUS, not the text
        var ex = new HttpRequestException("boom", null, HttpStatusCode.TooManyRequests);

        Assert.Equal(ProviderVerdict.RateLimited, ProviderVerdictClassifier.FromException(ex));
    }

    [Fact]
    public void Typed_401_and_403_map_to_auth_failed()
    {
        Assert.Equal(ProviderVerdict.AuthFailed, ProviderVerdictClassifier.FromException(
            new HttpRequestException("nope", null, HttpStatusCode.Unauthorized)));
        Assert.Equal(ProviderVerdict.AuthFailed, ProviderVerdictClassifier.FromHttpFailure(HttpStatusCode.Forbidden, "denied"));
    }

    // Parity with the generation domain (ProviderVerdictClassifier): an auth failure with nothing to
    // authenticate WITH is a configuration gap, not a rejected credential — and the difference is not
    // cosmetic, because AuthFailed BENCHES the host for the cooldown window while NotConfigured advances
    // blamelessly. A backend nobody has configured would otherwise be penalised on every first attempt.
    [Fact]
    public void A_401_with_no_credentials_supplied_is_NotConfigured_not_AuthFailed()
    {
        Assert.Equal(ProviderVerdict.NotConfigured,
            ProviderVerdictClassifier.FromHttpFailure(HttpStatusCode.Unauthorized, "missing api key", hasCredentials: false));
        Assert.Equal(ProviderVerdict.NotConfigured,
            ProviderVerdictClassifier.FromHttpFailure(HttpStatusCode.Forbidden, null, hasCredentials: false));
        // the promotion follows the VERDICT, not the status — a body-text auth failure promotes too
        Assert.Equal(ProviderVerdict.NotConfigured,
            ProviderVerdictClassifier.FromHttpFailure(HttpStatusCode.BadRequest, "Incorrect API key provided", hasCredentials: false));
    }

    [Fact]
    public void A_401_with_credentials_supplied_stays_AuthFailed_because_that_key_is_wrong()
    {
        Assert.Equal(ProviderVerdict.AuthFailed,
            ProviderVerdictClassifier.FromHttpFailure(HttpStatusCode.Unauthorized, "invalid api key", hasCredentials: true));
    }

    [Fact]
    public void The_no_credentials_promotion_applies_only_to_auth_failures()
    {
        // "no key" alone cannot mean unconfigured: an OpenAI-compatible endpoint run locally (LM Studio,
        // vLLM, Ollama) legitimately needs none. Only "no key AND the server demanded one" does.
        Assert.Equal(ProviderVerdict.RateLimited,
            ProviderVerdictClassifier.FromHttpFailure(HttpStatusCode.TooManyRequests, null, hasCredentials: false));
        Assert.Equal(ProviderVerdict.Failed,
            ProviderVerdictClassifier.FromHttpFailure(HttpStatusCode.InternalServerError, "boom", hasCredentials: false));
    }

    [Fact]
    public void Untyped_exceptions_classify_from_their_message()
    {
        Assert.Equal(ProviderVerdict.RateLimited,
            ProviderVerdictClassifier.FromException(new InvalidOperationException("rate_limit_error from upstream")));
        Assert.Equal(ProviderVerdict.Failed,
            ProviderVerdictClassifier.FromException(new InvalidOperationException("connection refused by proxy")));
        Assert.Equal(ProviderVerdict.Timeout,
            ProviderVerdictClassifier.FromException(new OperationCanceledException()));
    }

    // R8 — a typed provider exception often wraps the real "too long" detail in an INNER exception; scanning
    // only the outer ex.Message misses it → Failed instead of ContextWindowExceeded, defeating the
    // big-context fallback.
    [Fact]
    public void FromException_reads_the_inner_exception_chain_for_context_window()
    {
        var ex = new InvalidOperationException("The request failed.",
            new InvalidOperationException("This model's maximum context length is 8192 tokens; your request had 90000."));

        Assert.Equal(ProviderVerdict.ContextWindowExceeded, ProviderVerdictClassifier.FromException(ex));
    }

    // R8 — the built-in patterns are English-only; an app can add its own matcher (e.g. a non-English
    // provider) via a scoped seam, consulted BEFORE the built-ins.
    [Fact]
    public void Custom_matcher_extends_classification_and_is_scoped()
    {
        const string german = "Ratenlimit überschritten"; // "rate limit exceeded" — not matched by built-ins
        Assert.Equal(ProviderVerdict.Failed, ProviderVerdictClassifier.FromErrorText(german));

        using (ProviderVerdictClassifier.AddErrorTextMatcher(t =>
            t.Contains("Ratenlimit", StringComparison.OrdinalIgnoreCase) ? ProviderVerdict.RateLimited : null))
        {
            Assert.Equal(ProviderVerdict.RateLimited, ProviderVerdictClassifier.FromErrorText(german)); // custom wins
        }

        Assert.Equal(ProviderVerdict.Failed, ProviderVerdictClassifier.FromErrorText(german)); // unregistered on dispose
    }
}
